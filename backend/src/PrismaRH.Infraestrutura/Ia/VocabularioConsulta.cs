using System.Globalization;
using PrismaRH.Dominio.Analises;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Workflow;

namespace PrismaRH.Infraestrutura.Ia;

/// <summary>Os campos que uma pergunta pode alcançar. Lista fechada.</summary>
public enum CampoConsulta
{
    Severidade = 1,
    Status = 2,
    Categoria = 3,
    Regra = 4,
    Competencia = 5,
    ValorEncontrado = 6,
    Diferenca = 7,
}

/// <summary>As comparações permitidas. Lista fechada.</summary>
public enum OperadorConsulta
{
    Igual = 1,
    Diferente = 2,
    Maior = 3,
    Menor = 4,
    MaiorOuIgual = 5,
    MenorOuIgual = 6,
}

/// <summary>Um filtro já validado. Só existe se passou pelo vocabulário.</summary>
public sealed record FiltroConsulta(CampoConsulta Campo, OperadorConsulta Operador, string Valor)
{
    /// <summary>Como a tela mostra o que a aplicação entendeu.</summary>
    public string Descrever() => $"{Campo} {Simbolo(Operador)} {Valor}";

    private static string Simbolo(OperadorConsulta o) => o switch
    {
        OperadorConsulta.Igual => "=",
        OperadorConsulta.Diferente => "≠",
        OperadorConsulta.Maior => ">",
        OperadorConsulta.Menor => "<",
        OperadorConsulta.MaiorOuIgual => "≥",
        _ => "≤",
    };
}

/// <summary>Por que um filtro proposto foi recusado.</summary>
public enum RecusaFiltro
{
    Aceito = 0,
    CampoDesconhecido = 1,
    OperadorNaoPermitidoNesteCampo = 2,
    ValorForaDoTipo = 3,
    FiltrosDemais = 4,
    SemFiltro = 5,
}

/// <summary>
/// O vocabulário fechado da consulta em linguagem natural (Fase 11C).
///
/// ## O desenho, e por que ele é assim
///
/// `CLAUDE.md §37.9` e o `ROADMAP.md` da Fase 11C dizem a mesma coisa: **não há
/// SQL arbitrário gerado e executado pelo modelo.** O modelo escolhe dentro de
/// um conjunto fechado de campos e operadores que a aplicação declara, e o que
/// estiver fora é recusado **antes** de virar consulta.
///
/// ```text
/// Pergunta em português
///        ↓
///     Modelo            ← propõe. Não decide.
///        ↓
/// Filtro proposto       ← texto, dado não confiável
///        ↓
/// ESTA CLASSE           ← campo existe? operador vale aqui? valor é do tipo?
///        ↓
/// Consulta em C# sobre o DbContext, com o filtro global de organização intacto
/// ```
///
/// ## Por que a validação é por CAMPO, e não uma lista global de operadores
///
/// Porque `Severidade &gt; Alta` não quer dizer nada. Um enum tem igualdade, não
/// ordem de negócio — `Alta` ser o valor 1 é detalhe de armazenamento, não
/// afirmação de que ela é "menor" que `Media`. Deixar `&gt;` passar num enum
/// produziria resultado que **parece** uma resposta e não é, que é o pior tipo
/// de defeito num relatório de conferência.
///
/// Então cada campo declara os seus operadores, e o resto é recusa.
///
/// ## Isto não é uma abstração especulativa
///
/// `CLAUDE.md §20` proíbe abstração sem uso real. Esta existe porque é
/// **exatamente** o controle de segurança que a fase exige — sem ela, a única
/// barreira entre a pergunta do usuário e o banco seria o bom comportamento do
/// modelo.
/// </summary>
public static class VocabularioConsulta
{
    /// <summary>
    /// Teto de filtros por pergunta.
    ///
    /// Cinco cobre qualquer pergunta humana sobre inconsistências. Acima disso
    /// não é pergunta: é alguém montando consulta cara por engenharia de prompt
    /// (`CLAUDE.md §24.18`).
    /// </summary>
    public const int MaximoFiltros = 5;

    private static readonly OperadorConsulta[] SoIgualdade =
        [OperadorConsulta.Igual, OperadorConsulta.Diferente];

    private static readonly OperadorConsulta[] Ordenaveis =
    [
        OperadorConsulta.Igual, OperadorConsulta.Diferente,
        OperadorConsulta.Maior, OperadorConsulta.Menor,
        OperadorConsulta.MaiorOuIgual, OperadorConsulta.MenorOuIgual,
    ];

    /// <summary>
    /// O catálogo, campo a campo: quais operadores valem e quais valores existem.
    ///
    /// É esta tabela que vai no prompt. O modelo não adivinha o vocabulário —
    /// ele o recebe, e mesmo assim o que ele devolve é conferido aqui.
    /// </summary>
    public static IReadOnlyList<DescricaoCampo> Catalogo { get; } =
    [
        new(CampoConsulta.Severidade, "severidade da inconsistencia", SoIgualdade, Nomes<Severidade>()),
        new(CampoConsulta.Status, "situacao no workflow", SoIgualdade, Nomes<StatusInconsistencia>()),
        new(CampoConsulta.Categoria, "assunto da regra", SoIgualdade, Nomes<CategoriaRegra>()),
        new(CampoConsulta.Regra, "qual regra encontrou", SoIgualdade, Nomes<CodigoRegra>()),
        new(CampoConsulta.Competencia, "competencia da folha, no formato aaaa-mm", Ordenaveis, ["2026-08"]),
        new(CampoConsulta.ValorEncontrado, "valor encontrado, em reais", Ordenaveis, ["1500.00"]),
        new(CampoConsulta.Diferenca, "diferenca entre esperado e encontrado, em reais", Ordenaveis, ["-250.00"]),
    ];

    /// <summary>
    /// Confere um filtro proposto pelo modelo.
    ///
    /// Devolve <see cref="RecusaFiltro.Aceito"/> e o filtro pronto, ou o motivo
    /// da recusa e `null`. **Nunca "corrige" o que veio errado** — adivinhar o
    /// que o modelo quis dizer é justamente o caminho que a validação existe
    /// para fechar.
    /// </summary>
    public static RecusaFiltro Conferir(
        string? campo, string? operador, string? valor, out FiltroConsulta? filtro)
    {
        filtro = null;

        if (!Enum.TryParse<CampoConsulta>(campo, ignoreCase: true, out var oCampo)
            || !Enum.IsDefined(oCampo))
        {
            return RecusaFiltro.CampoDesconhecido;
        }

        if (!Enum.TryParse<OperadorConsulta>(operador, ignoreCase: true, out var oOperador)
            || !Enum.IsDefined(oOperador))
        {
            // Operador inexistente é campo desconhecido do mesmo jeito: veio
            // algo fora do vocabulário.
            return RecusaFiltro.OperadorNaoPermitidoNesteCampo;
        }

        var descricao = Catalogo.Single(c => c.Campo == oCampo);

        if (!descricao.Operadores.Contains(oOperador))
        {
            return RecusaFiltro.OperadorNaoPermitidoNesteCampo;
        }

        if (!ValorValido(oCampo, valor, out var normalizado))
        {
            return RecusaFiltro.ValorForaDoTipo;
        }

        filtro = new FiltroConsulta(oCampo, oOperador, normalizado);

        return RecusaFiltro.Aceito;
    }

    /// <summary>
    /// O valor cabe no tipo do campo?
    ///
    /// Devolve a forma **normalizada** — `alta` vira `Alta`, `1.500,00` é
    /// recusado. Guardar a forma canônica é o que impede o mesmo filtro de
    /// existir escrito de cinco jeitos.
    /// </summary>
    private static bool ValorValido(CampoConsulta campo, string? valor, out string normalizado)
    {
        normalizado = string.Empty;

        if (string.IsNullOrWhiteSpace(valor))
        {
            return false;
        }

        var bruto = valor.Trim();

        switch (campo)
        {
            case CampoConsulta.Severidade:
                return Enumerado<Severidade>(bruto, out normalizado);

            case CampoConsulta.Status:
                return Enumerado<StatusInconsistencia>(bruto, out normalizado);

            case CampoConsulta.Categoria:
                return Enumerado<CategoriaRegra>(bruto, out normalizado);

            case CampoConsulta.Regra:
                return Enumerado<CodigoRegra>(bruto, out normalizado);

            case CampoConsulta.Competencia:
                // Formato único e explícito. Aceitar o que a cultura da máquina
                // entender faria `03/04/2026` virar março num servidor e abril
                // noutro — a mesma armadilha já documentada na importação.
                // Reusa o parser do domínio, que já desempata `08/2026` de
                // `2026-08` pelo número de quatro dígitos e recusa ano fora da
                // faixa. Escrever um segundo parser aqui criaria duas regras
                // para a mesma coisa, e a segunda é a que ninguém testa.
                if (!Competencia.TryParse(bruto, out var competencia))
                {
                    return false;
                }

                normalizado = competencia.ToString();
                return true;

            default:
                // ⚠️ Cultura INVARIANTE de propósito. O modelo escreve `1500.00`
                // com ponto; ler em pt-BR transformaria isso em cento e
                // cinquenta mil, sem erro nenhum aparecer.
                if (!decimal.TryParse(
                        bruto,
                        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var numero))
                {
                    return false;
                }

                normalizado = numero.ToString(CultureInfo.InvariantCulture);
                return true;
        }
    }

    private static bool Enumerado<T>(string bruto, out string normalizado)
        where T : struct, Enum
    {
        normalizado = string.Empty;

        // ⚠️ `Enum.TryParse` aceita NÚMERO: "7" vira o enum 7 mesmo sem existir.
        // Recusar dígito antes é o que fecha isso, junto com o `IsDefined`.
        if (bruto.Length > 0 && (char.IsDigit(bruto[0]) || bruto[0] is '-' or '+'))
        {
            return false;
        }

        if (!Enum.TryParse<T>(bruto, ignoreCase: true, out var valor) || !Enum.IsDefined(valor))
        {
            return false;
        }

        normalizado = valor.ToString();

        return true;
    }

    private static string[] Nomes<T>() where T : struct, Enum =>
        [.. Enum.GetNames<T>()];
}

/// <summary>Um campo do catálogo, como o prompt o descreve.</summary>
public sealed record DescricaoCampo(
    CampoConsulta Campo,
    string Significado,
    IReadOnlyList<OperadorConsulta> Operadores,
    IReadOnlyList<string> ValoresPossiveis);
