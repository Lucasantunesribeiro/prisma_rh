using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrismaRH.Dominio.Empresas;

namespace PrismaRH.Infraestrutura.Integracoes;

/// <summary>Como terminou a consulta. Vocabulario fechado.</summary>
public enum SituacaoConsulta
{
    Encontrada = 1,

    /// <summary>CNPJ valido, sem registro na base da Receita.</summary>
    NaoEncontrada = 2,

    /// <summary>
    /// O parceiro recusou o numero. Nao deveria acontecer - o Prisma RH confere
    /// os digitos antes -, e chegar aqui significa que as duas validacoes
    /// discordam.
    /// </summary>
    Recusada = 3,

    /// <summary>Fora do ar, lento, com limite estourado ou respondendo besteira.</summary>
    Indisponivel = 4,
}

/// <summary>
/// O que o Prisma RH aproveita da resposta.
///
/// ## Tres campos de quarenta
///
/// A BrasilAPI devolve endereco, telefone, e-mail, CNAEs, capital social,
/// regime tributario e o **quadro societario** - com nome, faixa etaria e CPF
/// parcial de cada socio, que sao pessoas fisicas.
///
/// Nada disso e lido. `CLAUDE.md secao 24.13` chama isso de minimizacao, e aqui
/// ela nem exigiu disciplina: `Empresa` so tem razao social, CNPJ e nome
/// fantasia, entao o que o modelo nao guarda o codigo nao tem onde por.
///
/// A situacao cadastral e a excecao proposital: ela **nao e persistida**, so
/// aparece na tela no instante da consulta, para o usuario ver que o CNPJ
/// digitado esta BAIXADO antes de cadastrar a empresa.
/// </summary>
public sealed record EmpresaNaReceita(
    string RazaoSocial,
    string? NomeFantasia,
    string SituacaoCadastral);

public sealed record ResultadoConsultaCnpj(
    SituacaoConsulta Situacao,
    EmpresaNaReceita? Empresa,
    string Mensagem);

/// <summary>
/// Consulta de empresa por CNPJ na BrasilAPI (Fase 8).
///
/// ## Isto nao e fonte de verdade
///
/// O resultado nunca vira cadastro sozinho: ele volta para a tela, a pessoa
/// confere e decide. O `CLAUDE.md secao 1` diz que o Prisma RH nao depende de
/// outro sistema para funcionar, e essa propriedade continua valendo com a
/// integracao no ar - com a BrasilAPI fora, o cadastro manual funciona igual.
/// Por isso **nenhuma** falha daqui vira erro do cadastro: vira aviso.
///
/// ## Por que nao existe interface IIntegracaoX
///
/// `ROADMAP.md` Fase 8: *"A interface e nomes finais so deverao existir quando
/// houver integracao real a implementar. Nao criar abstracoes vazias
/// antecipadamente."* Ha uma integracao, com um metodo. Uma interface com uma
/// implementacao so serviria para o teste - e o teste aqui troca o
/// `HttpMessageHandler`, o que e melhor: exercita este codigo de verdade, com a
/// guarda, o redirect e o parsing reais.
/// </summary>
public sealed class ConsultaCnpjBrasilApi(
    HttpClient http,
    GuardaDestino guarda,
    ILogger<ConsultaCnpjBrasilApi> log)
{
    /// <summary>
    /// A base. Fixa em codigo, como a allowlist da <see cref="GuardaDestino"/> -
    /// e pelo mesmo motivo.
    /// </summary>
    public const string Base = "https://brasilapi.com.br/api/cnpj/v1/";

    /// <summary>
    /// Teto do corpo aceito. A resposta real tem uns 8 KB; meio mega e folga de
    /// sobra. O teto existe porque um parceiro comprometido ou um proxy
    /// hostil no caminho podem responder para sempre, e `ReadAsStringAsync`
    /// sem limite le tudo ate a memoria acabar.
    /// </summary>
    public const int TamanhoMaximoResposta = 512 * 1024;

    /// <summary>
    /// Toda chamada externa tem prazo. Sem isso, um parceiro lento segura a
    /// requisicao do usuario, e com ela uma conexao do pool - e um parceiro
    /// lento derruba a API inteira sem precisar estar fora do ar.
    /// </summary>
    public static readonly TimeSpan Prazo = TimeSpan.FromSeconds(8);

    private static readonly JsonDocumentOptions Opcoes = new() { MaxDepth = 16 };

    public async Task<ResultadoConsultaCnpj> ConsultarAsync(Cnpj cnpj, Guid correlacao, CancellationToken cancelamento)
    {
        // A URL e montada a partir de um `Cnpj` que ja passou pelo value object:
        // quatorze digitos, com digito verificador conferido. Nao ha texto do
        // usuario chegando na URL - nao por escapamento, mas porque o tipo que
        // chega aqui nao consegue carregar outra coisa.
        var destino = new Uri(Base + cnpj.Valor);

        var cronometro = Stopwatch.StartNew();

        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
        prazo.CancelAfter(Prazo);

        try
        {
            using var resposta = await EnviarSeguindoRedirectsAsync(destino, prazo.Token);

            log.LogInformation(
                "Consulta CNPJ {Correlacao} em {Host}: {Status} em {Duracao}ms",
                correlacao,
                destino.Host,
                (int)resposta.StatusCode,
                cronometro.ElapsedMilliseconds);

            return resposta.StatusCode switch
            {
                HttpStatusCode.OK => await LerAsync(resposta, prazo.Token),

                HttpStatusCode.NotFound => new ResultadoConsultaCnpj(
                    SituacaoConsulta.NaoEncontrada,
                    null,
                    "CNPJ nao encontrado na base da Receita Federal."),

                HttpStatusCode.BadRequest => new ResultadoConsultaCnpj(
                    SituacaoConsulta.Recusada,
                    null,
                    "A Receita Federal recusou este CNPJ."),

                // 429 e o parceiro dizendo "chega". Insistir e o que transforma
                // um limite temporario em bloqueio.
                HttpStatusCode.TooManyRequests => new ResultadoConsultaCnpj(
                    SituacaoConsulta.Indisponivel,
                    null,
                    "A consulta atingiu o limite do provedor. Tente de novo em alguns minutos."),

                _ => new ResultadoConsultaCnpj(
                    SituacaoConsulta.Indisponivel,
                    null,
                    "A consulta externa esta indisponivel no momento."),
            };
        }
        catch (DestinoRecusadoException excecao)
        {
            // Nao e indisponibilidade: e a guarda barrando. Merece nivel de
            // aviso justamente porque, em operacao normal, NUNCA acontece.
            log.LogWarning(
                "Consulta CNPJ {Correlacao} barrada pela guarda de destino: {Motivo}",
                correlacao,
                excecao.Message);

            return Indisponivel();
        }
        catch (OperationCanceledException) when (!cancelamento.IsCancellationRequested)
        {
            // O usuario nao desistiu - o prazo estourou. Quem desistiu foi o
            // parceiro, e a distincao importa para nao contar abandono de
            // navegador como falha de integracao.
            log.LogWarning(
                "Consulta CNPJ {Correlacao} estourou o prazo de {Prazo}s",
                correlacao,
                Prazo.TotalSeconds);

            return new ResultadoConsultaCnpj(
                SituacaoConsulta.Indisponivel,
                null,
                "A consulta externa demorou demais e foi encerrada.");
        }
        catch (HttpRequestException excecao)
        {
            log.LogWarning(
                "Consulta CNPJ {Correlacao} falhou na rede: {Motivo}",
                correlacao,
                excecao.Message);

            return Indisponivel();
        }
    }

    /// <summary>
    /// Envia e trata os redirects a mao.
    ///
    /// O `HttpClient` sabe seguir redirect sozinho - e por isso o cliente e
    /// registrado com `AllowAutoRedirect = false`. Seguir automaticamente
    /// significa pular a guarda em todos os saltos menos o primeiro, e o
    /// primeiro e justamente o unico que ninguem precisa atacar.
    /// </summary>
    private async Task<HttpResponseMessage> EnviarSeguindoRedirectsAsync(Uri destino, CancellationToken cancelamento)
    {
        for (var salto = 0; ; salto++)
        {
            await guarda.ConferirAsync(destino, cancelamento);

            using var pedido = new HttpRequestMessage(HttpMethod.Get, destino);
            pedido.Headers.Accept.Add(new("application/json"));

            var resposta = await http.SendAsync(
                pedido,
                HttpCompletionOption.ResponseHeadersRead,
                cancelamento);

            if (!EhRedirect(resposta.StatusCode) || resposta.Headers.Location is null)
            {
                return resposta;
            }

            var proximo = resposta.Headers.Location;
            resposta.Dispose();

            if (salto >= GuardaDestino.MaximoRedirects)
            {
                throw new DestinoRecusadoException(
                    $"Mais de {GuardaDestino.MaximoRedirects} redirects. Cadeia encerrada.");
            }

            // `Location` relativo e valido em HTTP e precisa ser resolvido
            // contra a URL atual antes de conferir - senao a guarda receberia
            // uma URL sem host e recusaria o que era legitimo.
            destino = proximo.IsAbsoluteUri ? proximo : new Uri(destino, proximo);
        }
    }

    private static bool EhRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Le e valida o corpo.
    ///
    /// A resposta do parceiro e **dado nao confiavel** - item 2 do Security Gate.
    /// Ela e conferida por esquema antes de encostar no dominio: se `razao_social`
    /// nao vier, ou vier como numero, ou o corpo nao for JSON, a consulta vira
    /// indisponibilidade em vez de virar cadastro errado.
    /// </summary>
    private static async Task<ResultadoConsultaCnpj> LerAsync(HttpResponseMessage resposta, CancellationToken cancelamento)
    {
        var declarado = resposta.Content.Headers.ContentLength;

        if (declarado > TamanhoMaximoResposta)
        {
            return Indisponivel();
        }

        await using var fluxo = await resposta.Content.ReadAsStreamAsync(cancelamento);

        var buffer = new MemoryStream();
        var pedaco = new byte[8 * 1024];
        int lidos;

        // Le com teto proprio, e nao pelo `Content-Length`: o cabecalho e
        // afirmacao de quem responde, e um corpo `chunked` nem o traz. Quem
        // confia no numero declarado nao tem teto nenhum.
        while ((lidos = await fluxo.ReadAsync(pedaco, cancelamento)) > 0)
        {
            if (buffer.Length + lidos > TamanhoMaximoResposta)
            {
                return Indisponivel();
            }

            buffer.Write(pedaco, 0, lidos);
        }

        try
        {
            using var json = JsonDocument.Parse(buffer.ToArray(), Opcoes);
            var raiz = json.RootElement;

            if (raiz.ValueKind != JsonValueKind.Object)
            {
                return Indisponivel();
            }

            var razaoSocial = Texto(raiz, "razao_social", Empresa.TamanhoMaximoRazaoSocial);

            if (string.IsNullOrWhiteSpace(razaoSocial))
            {
                return Indisponivel();
            }

            return new ResultadoConsultaCnpj(
                SituacaoConsulta.Encontrada,
                new EmpresaNaReceita(
                    razaoSocial,
                    Texto(raiz, "nome_fantasia", Empresa.TamanhoMaximoNomeFantasia),
                    Texto(raiz, "descricao_situacao_cadastral", 60) ?? "NAO INFORMADA"),
                "Dados encontrados na Receita Federal.");
        }
        catch (JsonException)
        {
            return Indisponivel();
        }
    }

    /// <summary>
    /// Campo de texto opcional, truncado no limite do dominio.
    ///
    /// Truncar em vez de recusar e proposital: a `Empresa` estoura acima de 250
    /// caracteres, e derrubar a consulta inteira porque a razao social de
    /// alguem e comprida seria trocar um dado utilizavel por erro. A pessoa
    /// confere na tela antes de salvar.
    /// </summary>
    private static string? Texto(JsonElement raiz, string campo, int maximo)
    {
        if (!raiz.TryGetProperty(campo, out var valor) || valor.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var texto = valor.GetString()?.Trim();

        if (string.IsNullOrEmpty(texto))
        {
            return null;
        }

        return texto.Length > maximo ? texto[..maximo] : texto;
    }

    private static ResultadoConsultaCnpj Indisponivel() => new(
        SituacaoConsulta.Indisponivel,
        null,
        "A consulta externa esta indisponivel no momento.");
}
