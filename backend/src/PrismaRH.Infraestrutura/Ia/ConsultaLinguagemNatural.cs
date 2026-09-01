using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Dominio.Analises;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Workflow;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Infraestrutura.Ia;

/// <summary>
/// O que a aplicação entendeu da pergunta.
///
/// <see cref="Filtros"/> é o que ela vai executar; <see cref="Recusados"/> é o
/// que o modelo propôs e o vocabulário barrou. Os dois vão para a tela: quem
/// pergunta precisa ver o que foi entendido, senão confia num resultado que não
/// responde o que perguntou.
/// </summary>
public sealed record ConsultaInterpretada(
    SituacaoIa Situacao,
    IReadOnlyList<FiltroConsulta> Filtros,
    IReadOnlyList<string> Recusados,
    int TokensUsados);

/// <summary>
/// A consulta em linguagem natural (Fase 11C).
///
/// ## O modelo propõe; a aplicação decide
///
/// ```text
/// "Quais inconsistencias criticas ainda estao abertas?"
///        ↓  modelo
/// [{campo:"Severidade", operador:"Igual", valor:"Alta"},
///  {campo:"Status",     operador:"Diferente", valor:"Resolvida"}]
///        ↓  VocabularioConsulta.Conferir — campo? operador? tipo?
///        ↓  EF Core sobre o DbContext, filtro global intacto
///     resultado
/// ```
///
/// **Não existe SQL gerado pelo modelo.** A saída dele é uma lista de tuplas de
/// texto; quem monta `Where` é o C# desta classe, com `Expression` tipada.
///
/// ## Duas coisas que a validação NÃO faz
///
/// 1. **Não corrige.** Campo desconhecido é recusado, não aproximado para o
///    mais parecido. Adivinhar o que o modelo quis dizer reabre exatamente o
///    buraco que a lista fechada fecha.
/// 2. **Não ignora em silêncio.** O filtro recusado vai para a resposta e
///    aparece na tela. Ignorar devolveria a lista inteira para quem pediu um
///    recorte — e a pessoa acharia que aquilo era o recorte.
///
/// ## O isolamento não passa por aqui
///
/// `AplicarAsync` parte de `db.ResultadosAnalise`, que já nasce sob o *global
/// query filter*. Nenhum filtro proposto pelo modelo pode alcançar
/// `IdOrganizacao` — o campo **não está no vocabulário**, e mesmo que estivesse
/// a consulta continuaria restrita. O isolamento é arquitetural (`§37.5`).
/// </summary>
public sealed class ConsultaLinguagemNatural(ClienteGemini cliente)
{
    /// <summary>
    /// Teto de linhas devolvidas.
    ///
    /// A consulta é montada por máquina a partir de texto livre: é o caminho
    /// mais fácil do sistema para pedir "tudo" sem querer. O teto é o que
    /// impede uma pergunta ampla de virar varredura da tabela inteira
    /// (`CLAUDE.md §24.18`).
    /// </summary>
    public const int MaximoLinhas = 50;

    /// <summary>Teto da pergunta. Pergunta humana não tem mil caracteres.</summary>
    public const int MaximoCaracteresPergunta = 500;

    public bool Disponivel => cliente.Configurada;

    /// <summary>
    /// Transforma a pergunta em filtros validados.
    ///
    /// A pergunta é **do usuário** — entrada não confiável por definição, e
    /// nesta fase ela é legítima por definição também, porque a funcionalidade
    /// é aceitar texto livre. Por isso a defesa não está em filtrar a pergunta,
    /// e sim em limitar o que a resposta consegue causar.
    /// </summary>
    public async Task<ConsultaInterpretada> InterpretarAsync(
        string pergunta, Guid correlacao, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pergunta);

        if (!cliente.Configurada)
        {
            return new ConsultaInterpretada(SituacaoIa.NaoConfigurada, [], [], 0);
        }

        var enxuta = pergunta.Trim();

        if (enxuta.Length > MaximoCaracteresPergunta)
        {
            enxuta = enxuta[..MaximoCaracteresPergunta];
        }

        var resposta = await cliente.ExplicarAsync(
            Instrucao(), $"Pergunta do usuario: {enxuta}", correlacao, ct, respostaEmJson: true);

        if (resposta.Situacao != SituacaoIa.Respondeu)
        {
            return new ConsultaInterpretada(resposta.Situacao, [], [], resposta.TokensUsados);
        }

        var (filtros, recusados) = Conferir(resposta.Texto);

        return new ConsultaInterpretada(
            SituacaoIa.Respondeu, filtros, recusados, resposta.TokensUsados);
    }

    /// <summary>
    /// Confere a proposta do modelo, item a item.
    ///
    /// ⚠️ Tudo aqui é `try`/recusa em vez de exceção que sobe: **JSON torto do
    /// modelo é o caso esperado**, não a exceção. Um `JsonException` vazando
    /// daqui viraria 500 numa rota cuja falha normal é "não entendi a pergunta".
    /// </summary>
    private static (List<FiltroConsulta> Filtros, List<string> Recusados) Conferir(string bruto)
    {
        var filtros = new List<FiltroConsulta>();
        var recusados = new List<string>();

        JsonDocument documento;

        try
        {
            documento = JsonDocument.Parse(bruto);
        }
        catch (JsonException)
        {
            recusados.Add("A proposta do assistente nao veio no formato esperado.");
            return (filtros, recusados);
        }

        using (documento)
        {
            if (!documento.RootElement.TryGetProperty("filtros", out var lista)
                || lista.ValueKind != JsonValueKind.Array)
            {
                recusados.Add("A proposta do assistente nao trouxe filtro nenhum.");
                return (filtros, recusados);
            }

            foreach (var item in lista.EnumerateArray())
            {
                if (filtros.Count >= VocabularioConsulta.MaximoFiltros)
                {
                    recusados.Add(
                        $"Filtros acima do limite de {VocabularioConsulta.MaximoFiltros} foram descartados.");
                    break;
                }

                if (item.ValueKind != JsonValueKind.Object)
                {
                    recusados.Add("Filtro em formato invalido.");
                    continue;
                }

                var campo = Texto(item, "campo");
                var operador = Texto(item, "operador");
                var valor = Texto(item, "valor");

                var veredito = VocabularioConsulta.Conferir(campo, operador, valor, out var filtro);

                if (veredito == RecusaFiltro.Aceito && filtro is not null)
                {
                    filtros.Add(filtro);
                    continue;
                }

                // ⚠️ O motivo cita o que o MODELO propôs, e o texto vai para a
                // tela. Ele é renderizado como texto pelo React, como qualquer
                // conteúdo de terceiro (`CLAUDE.md §24.9`).
                recusados.Add(veredito switch
                {
                    RecusaFiltro.CampoDesconhecido =>
                        $"Campo '{Curto(campo)}' nao existe na consulta.",
                    RecusaFiltro.OperadorNaoPermitidoNesteCampo =>
                        $"Comparacao '{Curto(operador)}' nao vale para o campo '{Curto(campo)}'.",
                    _ => $"Valor '{Curto(valor)}' nao serve para o campo '{Curto(campo)}'.",
                });
            }
        }

        return (filtros, recusados);
    }

    private static string? Texto(JsonElement item, string nome) =>
        item.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;

    /// <summary>
    /// Corta o que vai para a mensagem de erro.
    ///
    /// O que o modelo devolveu pode ser qualquer coisa, inclusive um parágrafo.
    /// Ecoar isso inteiro numa mensagem é ecoar entrada não confiável de volta
    /// para a tela sem teto de tamanho.
    /// </summary>
    private static string Curto(string? bruto)
    {
        var limpo = (bruto ?? string.Empty).Trim();

        if (limpo.Length == 0)
        {
            return "(vazio)";
        }

        return limpo.Length > 30 ? limpo[..30] : limpo;
    }

    /// <summary>
    /// Monta a instrução com o catálogo dentro.
    ///
    /// O vocabulário é **gerado a partir do catálogo**, e não escrito à mão no
    /// prompt: um campo novo entra nos dois lugares de uma vez, e o prompt
    /// nunca oferece ao modelo algo que a validação vai recusar.
    /// </summary>
    private static string Instrucao()
    {
        var catalogo = new StringBuilder();

        foreach (var campo in VocabularioConsulta.Catalogo)
        {
            catalogo.Append("- ").Append(campo.Campo).Append(" (").Append(campo.Significado).Append("). ")
                .Append("Comparacoes: ").AppendJoin(", ", campo.Operadores).Append(". ")
                .Append("Valores: ").AppendJoin(", ", campo.ValoresPossiveis).Append('\n');
        }

        // `$$` de proposito: com um `$` so, a chave literal do exemplo de JSON
        // viraria interpolacao. Com dois, `{` e chave e `{{expr}}` e valor.
        return $$"""
            Voce converte a pergunta de um analista de RH brasileiro em filtros
            sobre a lista de inconsistencias de folha de pagamento.

            Responda SOMENTE um JSON assim, sem texto em volta:
            {"filtros":[{"campo":"...","operador":"...","valor":"..."}]}

            Campos disponiveis, com as comparacoes e os valores que cada um aceita:
            {{catalogo}}
            Regras:
            - Use SOMENTE campos, comparacoes e valores desta lista. Nao invente.
            - Numero decimal com PONTO, nunca virgula. Competencia como aaaa-mm.
            - No maximo {{VocabularioConsulta.MaximoFiltros}} filtros.
            - Se a pergunta nao der para responder com estes campos, devolva
              {"filtros":[]}.
            """;
    }

    /// <summary>
    /// Aplica os filtros já validados.
    ///
    /// Recebe `IQueryable` e não `DbContext` de propósito: quem chama entrega a
    /// consulta **já sob o filtro global**, e esta função não tem como escapar
    /// dele — não há `IgnoreQueryFilters` aqui, e a ausência é visível em
    /// revisão.
    /// </summary>
    public static IQueryable<ResultadoAnalise> Aplicar(
        IQueryable<ResultadoAnalise> consulta,
        IReadOnlyList<FiltroConsulta> filtros,
        IQueryable<FolhaPagamento> folhas)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        ArgumentNullException.ThrowIfNull(filtros);
        ArgumentNullException.ThrowIfNull(folhas);

        foreach (var filtro in filtros)
        {
            consulta = filtro.Campo switch
            {
                CampoConsulta.Severidade => PorEnum<Severidade>(
                    consulta, filtro, (c, v, igual) => igual
                        ? c.Where(r => r.Severidade == v)
                        : c.Where(r => r.Severidade != v)),

                CampoConsulta.Status => PorEnum<StatusInconsistencia>(
                    consulta, filtro, (c, v, igual) => igual
                        ? c.Where(r => r.Status == v)
                        : c.Where(r => r.Status != v)),

                CampoConsulta.Categoria => PorEnum<CategoriaRegra>(
                    consulta, filtro, (c, v, igual) => igual
                        ? c.Where(r => r.Categoria == v)
                        : c.Where(r => r.Categoria != v)),

                CampoConsulta.Regra => PorEnum<CodigoRegra>(
                    consulta, filtro, (c, v, igual) => igual
                        ? c.Where(r => r.Codigo == v)
                        : c.Where(r => r.Codigo != v)),

                CampoConsulta.Competencia => PorCompetencia(consulta, filtro, folhas),

                CampoConsulta.ValorEncontrado => PorDecimal(consulta, filtro, r => r.ValorEncontrado),

                _ => PorDecimal(consulta, filtro, r => r.Diferenca),
            };
        }

        return consulta;
    }

    private static IQueryable<ResultadoAnalise> PorEnum<T>(
        IQueryable<ResultadoAnalise> consulta,
        FiltroConsulta filtro,
        Func<IQueryable<ResultadoAnalise>, T, bool, IQueryable<ResultadoAnalise>> aplicar)
        where T : struct, Enum
    {
        // O valor já passou pelo vocabulário; aqui o parse não pode falhar. Se
        // falhar, a consulta some em vez de virar filtro ignorado.
        if (!Enum.TryParse<T>(filtro.Valor, out var valor))
        {
            return consulta.Where(_ => false);
        }

        return aplicar(consulta, valor, filtro.Operador == OperadorConsulta.Igual);
    }

    /// <summary>
    /// Competência mora na folha, não no resultado.
    ///
    /// A subconsulta parte de `folhas`, que **também** está sob o filtro global
    /// — então nem por este caminho um id de outra organização entra.
    ///
    /// A comparação funciona porque `Competencia` é convertida para o inteiro
    /// `202608`, que é monotônico: ordenar o código é ordenar a competência.
    /// </summary>
    private static IQueryable<ResultadoAnalise> PorCompetencia(
        IQueryable<ResultadoAnalise> consulta,
        FiltroConsulta filtro,
        IQueryable<FolhaPagamento> folhas)
    {
        if (!Competencia.TryParse(filtro.Valor, out var alvo))
        {
            return consulta.Where(_ => false);
        }

        var ids = filtro.Operador switch
        {
            OperadorConsulta.Igual => folhas.Where(f => f.Competencia == alvo),
            OperadorConsulta.Diferente => folhas.Where(f => f.Competencia != alvo),
            OperadorConsulta.Maior => folhas.Where(f => f.Competencia > alvo),
            OperadorConsulta.Menor => folhas.Where(f => f.Competencia < alvo),
            OperadorConsulta.MaiorOuIgual => folhas.Where(f => f.Competencia >= alvo),
            _ => folhas.Where(f => f.Competencia <= alvo),
        };

        return consulta.Where(r => ids.Select(f => f.Id).Contains(r.IdFolha));
    }

    private static IQueryable<ResultadoAnalise> PorDecimal(
        IQueryable<ResultadoAnalise> consulta,
        FiltroConsulta filtro,
        System.Linq.Expressions.Expression<Func<ResultadoAnalise, decimal?>> campo)
    {
        if (!decimal.TryParse(
                filtro.Valor,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var alvo))
        {
            return consulta.Where(_ => false);
        }

        // Montado por `Expression` porque o campo varia. O EF traduz isto para
        // SQL parametrizado, como qualquer outra consulta do projeto — nao ha
        // concatenacao de texto em lugar nenhum (`CLAUDE.md §24.8`).
        var parametro = campo.Parameters[0];
        var corpo = campo.Body;
        var constante = System.Linq.Expressions.Expression.Constant(
            (decimal?)alvo, typeof(decimal?));

        var comparacao = filtro.Operador switch
        {
            OperadorConsulta.Igual => System.Linq.Expressions.Expression.Equal(corpo, constante),
            OperadorConsulta.Diferente => System.Linq.Expressions.Expression.NotEqual(corpo, constante),
            OperadorConsulta.Maior => System.Linq.Expressions.Expression.GreaterThan(corpo, constante),
            OperadorConsulta.Menor => System.Linq.Expressions.Expression.LessThan(corpo, constante),
            OperadorConsulta.MaiorOuIgual =>
                System.Linq.Expressions.Expression.GreaterThanOrEqual(corpo, constante),
            _ => System.Linq.Expressions.Expression.LessThanOrEqual(corpo, constante),
        };

        return consulta.Where(
            System.Linq.Expressions.Expression.Lambda<Func<ResultadoAnalise, bool>>(
                comparacao, parametro));
    }
}
