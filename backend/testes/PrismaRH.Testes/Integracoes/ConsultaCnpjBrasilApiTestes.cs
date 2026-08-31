using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Infraestrutura.Integracoes;

namespace PrismaRH.Testes.Integracoes;

/// <summary>
/// O cliente da BrasilAPI, exercitado sem rede.
///
/// A regra de ouro destes testes: a resposta do parceiro e **dado nao
/// confiavel**. Quase todos aqui descrevem o parceiro se comportando mal - e o
/// que se exige e sempre a mesma coisa, que o Prisma RH nao quebre e nao
/// cadastre nada errado por causa disso.
/// </summary>
public sealed class ConsultaCnpjBrasilApiTestes
{
    private static readonly Cnpj Numero = Cnpj.Criar("11222333000181");

    /// <summary>
    /// DNS que devolve endereco publico: a guarda passa, e o teste consegue
    /// chegar no comportamento que ele quer medir. Quem barra por IP tem suite
    /// propria em GuardaDestinoTestes.
    /// </summary>
    private static GuardaDestino DnsPublico() =>
        new((_, _) => Task.FromResult<IPAddress[]>([IPAddress.Parse("104.18.0.1")]));

    private static ConsultaCnpjBrasilApi Cliente(ParceiroFalso parceiro, GuardaDestino? guarda = null) =>
        new(new HttpClient(parceiro), guarda ?? DnsPublico(), NullLogger<ConsultaCnpjBrasilApi>.Instance);

    private static Task<ResultadoConsultaCnpj> Consultar(ParceiroFalso parceiro, GuardaDestino? guarda = null) =>
        Cliente(parceiro, guarda).ConsultarAsync(Numero, Guid.NewGuid(), CancellationToken.None);

    // ------------------------------------------------------------ caminho feliz

    [Fact]
    public async Task LeRazaoSocialNomeFantasiaESituacao()
    {
        var resultado = await Consultar(ParceiroFalso.ComJson(ParceiroFalso.RespostaValida));

        Assert.Equal(SituacaoConsulta.Encontrada, resultado.Situacao);
        Assert.Equal("INDUSTRIA EXEMPLO S.A.", resultado.Empresa!.RazaoSocial);
        Assert.Equal("EXEMPLO", resultado.Empresa.NomeFantasia);
        Assert.Equal("ATIVA", resultado.Empresa.SituacaoCadastral);
    }

    /// <summary>
    /// ⚠️ Minimizacao (`CLAUDE.md secao 24.13`), com nome e sobrenome.
    ///
    /// A resposta real traz o **quadro societario** - nome, faixa etaria e CPF
    /// parcial de pessoas fisicas -, alem de e-mail e telefone. Nada disso e
    /// campo de `Empresa`, e nada disso pode sobreviver a fronteira.
    ///
    /// O teste olha o objeto inteiro por serializacao, e nao campo a campo: um
    /// campo novo acrescentado por distracao amanha reprova aqui.
    /// </summary>
    [Fact]
    public async Task NaoCarregaNadaAlemDosTresCamposQueOModeloUsa()
    {
        var resultado = await Consultar(ParceiroFalso.ComJson(ParceiroFalso.RespostaValida));

        var tudo = System.Text.Json.JsonSerializer.Serialize(resultado.Empresa);

        Assert.DoesNotContain("FULANO", tudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456", tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("@exemplo", tudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1130000000", tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("RUA DE EXEMPLO", tudo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MontaAUrlComOsQuatorzeDigitosENadaMais()
    {
        var parceiro = ParceiroFalso.ComJson(ParceiroFalso.RespostaValida);

        await Consultar(parceiro);

        Assert.Equal(
            "https://brasilapi.com.br/api/cnpj/v1/11222333000181",
            Assert.Single(parceiro.Chamadas).ToString());
    }

    // ------------------------------------------------- o parceiro respondendo mal

    [Theory]
    [InlineData(HttpStatusCode.NotFound, SituacaoConsulta.NaoEncontrada)]
    [InlineData(HttpStatusCode.BadRequest, SituacaoConsulta.Recusada)]
    [InlineData(HttpStatusCode.TooManyRequests, SituacaoConsulta.Indisponivel)]
    [InlineData(HttpStatusCode.InternalServerError, SituacaoConsulta.Indisponivel)]
    [InlineData(HttpStatusCode.BadGateway, SituacaoConsulta.Indisponivel)]
    [InlineData(HttpStatusCode.Forbidden, SituacaoConsulta.Indisponivel)]
    public async Task TraduzOStatusDoParceiro(HttpStatusCode status, SituacaoConsulta esperado)
    {
        var resultado = await Consultar(ParceiroFalso.ComStatus(status));

        Assert.Equal(esperado, resultado.Situacao);
        Assert.Null(resultado.Empresa);
    }

    [Theory]
    // Nao e JSON.
    [InlineData("<html>Bem-vindo ao portal</html>")]
    // JSON, mas nao objeto.
    [InlineData("[1, 2, 3]")]
    [InlineData("\"so um texto\"")]
    // Objeto sem o campo que importa.
    [InlineData("{\"mensagem\":\"ok\"}")]
    // Campo presente com o tipo errado - o classico que quebra parser ingenuo.
    [InlineData("{\"razao_social\": 12345}")]
    [InlineData("{\"razao_social\": null}")]
    [InlineData("{\"razao_social\": \"   \"}")]
    [InlineData("")]
    public async Task CorpoQueNaoPassaNoEsquemaViraIndisponibilidade(string corpo)
    {
        var resultado = await Consultar(ParceiroFalso.ComJson(corpo));

        Assert.Equal(SituacaoConsulta.Indisponivel, resultado.Situacao);
        Assert.Null(resultado.Empresa);
    }

    /// <summary>
    /// Corpo grande recusado pelo `Content-Length` declarado - a barreira barata,
    /// que evita ate comecar a ler.
    /// </summary>
    [Fact]
    public async Task CorpoComTamanhoDeclaradoAcimaDoTetoERecusado()
    {
        var gigante = "{\"razao_social\":\"" + new string('A', ConsultaCnpjBrasilApi.TamanhoMaximoResposta) + "\"}";

        var resultado = await Consultar(ParceiroFalso.ComJson(gigante));

        Assert.Equal(SituacaoConsulta.Indisponivel, resultado.Situacao);
    }

    /// <summary>
    /// ⚠️ O teste que importa de verdade: corpo grande **sem** `Content-Length`.
    ///
    /// O cabecalho e afirmacao de quem responde, e num corpo `chunked` ele nem
    /// existe. Quem confia no numero declarado nao tem teto nenhum - o parceiro
    /// simplesmente omite o cabecalho e responde para sempre, derrubando o
    /// processo por memoria.
    ///
    /// O fluxo abaixo nao diz o proprio tamanho, entao so a contagem feita
    /// **durante a leitura** pode barra-lo.
    /// </summary>
    [Fact]
    public async Task CorpoSemTamanhoDeclaradoERecusadoDuranteALeitura()
    {
        var gigante = "{\"razao_social\":\"" + new string('A', ConsultaCnpjBrasilApi.TamanhoMaximoResposta) + "\"}";

        var parceiro = new ParceiroFalso(_ =>
        {
            var conteudo = new StreamContent(
                new FluxoSemTamanho(Encoding.UTF8.GetBytes(gigante)));

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = conteudo };
        });

        var resultado = await Consultar(parceiro);

        Assert.Equal(SituacaoConsulta.Indisponivel, resultado.Situacao);
    }

    /// <summary>Fluxo que se recusa a dizer o proprio tamanho, como um `chunked`.</summary>
    private sealed class FluxoSemTamanho(byte[] dados) : Stream
    {
        private readonly MemoryStream _origem = new(dados);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _origem.Read(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task RazaoSocialCompridaEEncurtadaEmVezDeDerrubarAConsulta()
    {
        var comprida = new string('B', Empresa.TamanhoMaximoRazaoSocial + 80);

        var resultado = await Consultar(
            ParceiroFalso.ComJson($"{{\"razao_social\":\"{comprida}\"}}"));

        Assert.Equal(SituacaoConsulta.Encontrada, resultado.Situacao);
        Assert.Equal(Empresa.TamanhoMaximoRazaoSocial, resultado.Empresa!.RazaoSocial.Length);
    }

    [Fact]
    public async Task SemSituacaoCadastralAConsultaAindaFunciona()
    {
        var resultado = await Consultar(ParceiroFalso.ComJson("{\"razao_social\":\"EMPRESA X\"}"));

        Assert.Equal(SituacaoConsulta.Encontrada, resultado.Situacao);
        Assert.Null(resultado.Empresa!.NomeFantasia);
        Assert.Equal("NAO INFORMADA", resultado.Empresa.SituacaoCadastral);
    }

    // ----------------------------------------------------------------- redirects

    /// <summary>
    /// ⚠️ O ataque que o `AllowAutoRedirect = false` existe para impedir.
    ///
    /// O parceiro - ou quem sequestrar o nome dele - responde 302 apontando
    /// para o metadata service da nuvem. Se o `HttpClient` seguisse sozinho, a
    /// guarda teria validado apenas a primeira URL, que era legitima.
    /// </summary>
    [Fact]
    public async Task RedirectParaEnderecoInternoEBarrado()
    {
        var parceiro = ParceiroFalso.QueRedirecionaPara("https://brasilapi.com.br/desvio");

        // O host continua na allowlist; o que muda e para onde ele resolve na
        // hora do segundo salto. E o cenario de DNS rebinding.
        var chamadas = 0;
        var guarda = new GuardaDestino((_, _) =>
        {
            chamadas++;
            return Task.FromResult<IPAddress[]>([
                IPAddress.Parse(chamadas == 1 ? "104.18.0.1" : "169.254.169.254")]);
        });

        var resultado = await Consultar(parceiro, guarda);

        Assert.Equal(SituacaoConsulta.Indisponivel, resultado.Situacao);
        Assert.Equal(2, chamadas); // a guarda rodou de novo no salto
    }

    [Fact]
    public async Task RedirectParaHostForaDaAllowlistEBarrado()
    {
        var parceiro = ParceiroFalso.QueRedirecionaPara("https://servidor-do-atacante.com/dados");

        var resultado = await Consultar(parceiro);

        Assert.Equal(SituacaoConsulta.Indisponivel, resultado.Situacao);

        // O segundo destino foi recusado ANTES de virar requisicao: so a
        // primeira chamada aconteceu.
        Assert.Single(parceiro.Chamadas);
    }

    [Fact]
    public async Task RedirectLegitimoNoMesmoHostESeguido()
    {
        var parceiro = ParceiroFalso.QueRedirecionaPara("/api/cnpj/v1/11222333000181/");

        var resultado = await Consultar(parceiro);

        Assert.Equal(SituacaoConsulta.Encontrada, resultado.Situacao);
        Assert.Equal(2, parceiro.Chamadas.Count);

        // `Location` relativo resolvido contra a URL atual, e nao descartado.
        Assert.Equal(
            "https://brasilapi.com.br/api/cnpj/v1/11222333000181/",
            parceiro.Chamadas[1].ToString());
    }

    [Fact]
    public async Task CadeiaDeRedirectsSemFimEEncerrada()
    {
        var parceiro = new ParceiroFalso(_ =>
        {
            var resposta = new HttpResponseMessage(HttpStatusCode.Found);
            resposta.Headers.Location = new Uri("https://brasilapi.com.br/de-novo");
            return resposta;
        });

        var resultado = await Consultar(parceiro);

        Assert.Equal(SituacaoConsulta.Indisponivel, resultado.Situacao);
        Assert.Equal(GuardaDestino.MaximoRedirects + 1, parceiro.Chamadas.Count);
    }

    // -------------------------------------------------------------------- prazo

    /// <summary>
    /// Parceiro lento nao segura a requisicao do usuario.
    ///
    /// Sem prazo, um parceiro que nunca responde prende a conexao - e alguem
    /// que nem esta fora do ar derruba a API por exaustao de pool.
    /// </summary>
    [Fact]
    public async Task ParceiroQueNaoRespondeEAbandonado()
    {
        var parceiro = new ParceiroFalso(_ => throw new TaskCanceledException("prazo"));

        var resultado = await Consultar(parceiro);

        Assert.Equal(SituacaoConsulta.Indisponivel, resultado.Situacao);
        Assert.Contains("demorou", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FalhaDeRedeViraIndisponibilidadeENaoExcecao()
    {
        var parceiro = new ParceiroFalso(_ => throw new HttpRequestException("DNS nao resolveu"));

        var resultado = await Consultar(parceiro);

        Assert.Equal(SituacaoConsulta.Indisponivel, resultado.Situacao);
    }

    /// <summary>
    /// Desistencia do usuario nao e falha do parceiro.
    ///
    /// Quem fechou a aba foi a pessoa. Contar isso como indisponibilidade
    /// encheria o monitoramento de falha inventada - e o `CancellationToken`
    /// precisa continuar subindo, para a requisicao realmente parar.
    /// </summary>
    [Fact]
    public async Task DesistenciaDoUsuarioPropagaEmVezDeVirarIndisponibilidade()
    {
        using var desistencia = new CancellationTokenSource();
        await desistencia.CancelAsync();

        var parceiro = ParceiroFalso.ComJson(ParceiroFalso.RespostaValida);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Cliente(parceiro).ConsultarAsync(Numero, Guid.NewGuid(), desistencia.Token));
    }
}
