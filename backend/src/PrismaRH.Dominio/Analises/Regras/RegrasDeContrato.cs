using System.Globalization;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.Analises.Regras;

/// <summary>
/// Gente desligada ANTES da competencia aparecendo na folha mensal.
///
/// ## Por que importa
///
/// Pagar salario mensal a quem ja saiu e dinheiro que sai sem contrapartida, e
/// gera recolhimento de INSS e FGTS sobre um vinculo que nao existe mais. O
/// erro tipico e o desligamento cadastrado depois de a folha ja ter sido
/// calculada, sem que ninguem recalcule.
///
/// ## Por que so a folha MENSAL
///
/// Rescisao, ferias e 13o **devem** conter quem saiu - e literalmente para isso
/// que existem. Acusar essas folhas transformaria a regra em ruido, e regra que
/// da alarme falso e a primeira que alguem desliga.
/// </summary>
public sealed class DesligadoNaFolhaRegra : IRegraAnalise
{
    public CodigoRegra Codigo => CodigoRegra.DesligadoNaFolha;

    public int Versao => 1;

    public CategoriaRegra Categoria => CategoriaRegra.Contrato;

    public Severidade SeveridadePadrao => Severidade.Alta;

    public string Nome => "Desligado presente na folha mensal";

    public string Explicacao =>
        "Procura quem foi desligado antes do primeiro dia da competencia e mesmo assim "
        + "recebeu holerite na folha mensal.";

    public IReadOnlyList<DefinicaoParametro> Parametros => [];

    public IEnumerable<Achado> Executar(ContextoAnalise contexto, ValoresParametros parametros)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        if (contexto.Tipo != TipoFolha.Mensal)
        {
            yield break;
        }

        var contratos = contexto.ContratosDaEmpresa.ToDictionary(c => c.IdContrato);

        foreach (var holerite in contexto.Holerites)
        {
            if (!contratos.TryGetValue(holerite.IdContrato, out var contrato))
            {
                continue;
            }

            if (contrato.DataDesligamento is not { } saida
                || saida >= contexto.Competencia.PrimeiroDia)
            {
                continue;
            }

            yield return new Achado(
                $"Desligado em {saida:dd/MM/yyyy}, antes de {contexto.Competencia}, "
                + "e mesmo assim tem holerite nesta folha mensal.",
                holerite.IdFolhaFuncionario,
                holerite.IdFuncionario,
                holerite.Matricula,
                holerite.NomeFuncionario,
                ValorEncontrado: holerite.Liquido,
                Contexto: $"desligamento={saida:yyyy-MM-dd}");
        }
    }
}

/// <summary>
/// Contrato vivo na competencia, sem holerite na folha mensal.
///
/// ## Por que importa
///
/// E o erro mais silencioso da folha: ninguem reclama de um lancamento a mais,
/// mas a pessoa que ficou de fora descobre no dia do pagamento. Acontece quando
/// o contrato e criado depois de a folha ter sido calculada.
///
/// ## O que conta como "vivo"
///
/// Um unico dia dentro da competencia basta - quem foi admitido no dia 31 tem
/// direito a um dia de salario. Exigir o mes inteiro deixaria de fora
/// exatamente as admissoes e os desligamentos, que sao os casos em que a folha
/// mais erra.
/// </summary>
public sealed class AusenteDaFolhaRegra : IRegraAnalise
{
    public CodigoRegra Codigo => CodigoRegra.AusenteDaFolha;

    public int Versao => 1;

    public CategoriaRegra Categoria => CategoriaRegra.Ausencia;

    public Severidade SeveridadePadrao => Severidade.Alta;

    public string Nome => "Funcionario elegivel ausente da folha";

    public string Explicacao =>
        "Procura contrato vigente em algum dia da competencia que nao recebeu holerite "
        + "na folha mensal.";

    public IReadOnlyList<DefinicaoParametro> Parametros => [];

    public IEnumerable<Achado> Executar(ContextoAnalise contexto, ValoresParametros parametros)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        if (contexto.Tipo != TipoFolha.Mensal)
        {
            yield break;
        }

        var comHolerite = contexto.Holerites.Select(h => h.IdContrato).ToHashSet();

        foreach (var contrato in contexto.ContratosDaEmpresa)
        {
            if (comHolerite.Contains(contrato.IdContrato)
                || !contrato.VigenteEm(contexto.Competencia))
            {
                continue;
            }

            var periodo = contrato.DataDesligamento is { } saida
                ? $"admitido em {contrato.DataAdmissao:dd/MM/yyyy}, desligado em {saida:dd/MM/yyyy}"
                : $"admitido em {contrato.DataAdmissao:dd/MM/yyyy}";

            yield return new Achado(
                $"Contrato vigente em {contexto.Competencia} ({periodo}) sem holerite nesta folha.",
                IdFuncionario: contrato.IdFuncionario,
                Matricula: contrato.Matricula,
                NomeFuncionario: contrato.NomeFuncionario,
                Contexto: $"admissao={contrato.DataAdmissao:yyyy-MM-dd}");
        }
    }
}

/// <summary>
/// Comum as regras que comparam valores: formatar dinheiro em pt-BR.
///
/// Existe para que "R$ 1.234,56" seja escrito num lugar so. Tres regras
/// formatando por conta propria produziriam tres formatos, e o relatorio
/// pareceria montado por tres pessoas.
/// </summary>
internal static class TextoMonetario
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    internal static string Reais(decimal valor) => valor.ToString("C2", Brasil);

    internal static string Percentual(decimal valor) =>
        valor.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',') + "%";
}
