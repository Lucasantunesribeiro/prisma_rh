using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Pessoas;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// Fase 4D, etapa 1: o cadastro de dependentes.
///
/// O IRRF ainda nao existe - esta etapa entrega so quem depende de quem, e a
/// declaracao de quando essa pessoa abate imposto. A conta vem depois, quando
/// a tabela oficial estiver confirmada.
/// </summary>
public class DependenteTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Funcionario = Guid.CreateVersion7();

    private static Dependente Criar(
        DateOnly? inicio = null,
        DateOnly? fim = null,
        RelacaoDependente relacao = RelacaoDependente.Filho,
        string nome = "Joana Ribeiro Alves",
        DateOnly? nascimento = null) =>
        new(Org, Funcionario, nome, nascimento ?? new DateOnly(2015, 4, 10),
            relacao, inicio, fim, Agora);

    // ------------------------------------------------------------ cadastro

    [Fact]
    public void Criado_SemPeriodo_NaoAbateIrrf()
    {
        var dependente = Criar();

        Assert.False(dependente.DedutivelIrrf);
        Assert.Null(dependente.InicioDeducaoIrrf);
        Assert.Null(dependente.FimDeducaoIrrf);
    }

    [Fact]
    public void Criado_ComInicio_AbateIrrf()
    {
        var dependente = Criar(inicio: new DateOnly(2026, 1, 1));

        Assert.True(dependente.DedutivelIrrf);
    }

    [Fact]
    public void NomeVazio_ERecusado() =>
        Assert.Throws<ArgumentException>(() => Criar(nome: "   "));

    [Fact]
    public void NomeELimpo() =>
        Assert.Equal("Joana Ribeiro Alves", Criar(nome: "  Joana Ribeiro Alves  ").Nome);

    [Fact]
    public void NascimentoNoFuturo_ERecusado() =>
        Assert.Throws<ArgumentException>(() => Criar(nascimento: new DateOnly(2026, 8, 28)));

    [Fact]
    public void NascimentoHoje_EAceito() =>
        // Recem-nascido ja e dependente. O limite e "nao pode ser no futuro",
        // nao "precisa ter algum tempo de vida".
        Assert.Equal(new DateOnly(2026, 8, 27), Criar(nascimento: new DateOnly(2026, 8, 27)).DataNascimento);

    [Fact]
    public void RelacaoDesconhecida_ERecusada() =>
        Assert.Throws<ArgumentException>(() => Criar(relacao: (RelacaoDependente)77));

    [Fact]
    public void SemOrganizacao_ERecusado() =>
        Assert.Throws<ArgumentException>(() => new Dependente(
            Guid.Empty, Funcionario, "X", new DateOnly(2015, 1, 1),
            RelacaoDependente.Filho, null, null, Agora));

    [Fact]
    public void SemFuncionario_ERecusado() =>
        Assert.Throws<ArgumentException>(() => new Dependente(
            Org, Guid.Empty, "X", new DateOnly(2015, 1, 1),
            RelacaoDependente.Filho, null, null, Agora));

    // -------------------------------------------------------------- periodo

    [Fact]
    public void FimSemInicio_ERecusado()
    {
        // Estado impossivel: "deixa de abater em dezembro" sem nunca ter
        // comecado a abater.
        var erro = Assert.Throws<ArgumentException>(() => Criar(fim: new DateOnly(2026, 12, 31)));

        Assert.Contains("sem inicio", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FimAntesDoInicio_ERecusado() =>
        Assert.Throws<ArgumentException>(() =>
            Criar(inicio: new DateOnly(2026, 6, 1), fim: new DateOnly(2026, 5, 31)));

    [Fact]
    public void FimIgualAoInicio_EAceito() =>
        // Um unico dia de vigencia e legitimo, e cai dentro de uma competencia.
        Assert.True(Criar(inicio: new DateOnly(2026, 6, 1), fim: new DateOnly(2026, 6, 1))
            .DedutivelEm(new Competencia(2026, 6)));

    [Fact]
    public void RemoverPeriodo_DesligaADeducao()
    {
        var dependente = Criar(inicio: new DateOnly(2026, 1, 1));

        dependente.DefinirDeducaoIrrf(null, null);

        Assert.False(dependente.DedutivelIrrf);
    }

    // ---------------------------------------------------------- competencia

    [Theory]
    // Comeca em 15/06: conta o mes de junho INTEIRO. A deducao do IRRF e
    // mensal, nao proporcional aos dias.
    [InlineData(2026, 5, false)]
    [InlineData(2026, 6, true)]
    [InlineData(2026, 7, true)]
    public void ComecaNoMeioDoMes_ContaOMesInteiro(int ano, int mes, bool esperado) =>
        Assert.Equal(esperado, Criar(inicio: new DateOnly(2026, 6, 15)).DedutivelEm(new Competencia(ano, mes)));

    [Theory]
    // Termina em 10/09: setembro ainda conta inteiro.
    [InlineData(2026, 8, true)]
    [InlineData(2026, 9, true)]
    [InlineData(2026, 10, false)]
    public void TerminaNoMeioDoMes_ContaOMesInteiro(int ano, int mes, bool esperado) =>
        Assert.Equal(esperado, Criar(new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 10))
            .DedutivelEm(new Competencia(ano, mes)));

    [Fact]
    public void SemPeriodo_NaoContaEmCompetenciaAlguma()
    {
        var dependente = Criar();

        Assert.False(dependente.DedutivelEm(new Competencia(2026, 1)));
        Assert.False(dependente.DedutivelEm(new Competencia(2026, 8)));
        Assert.False(dependente.DedutivelEm(new Competencia(2030, 12)));
    }

    [Fact]
    public void SemFim_ContaIndefinidamente()
    {
        var dependente = Criar(inicio: new DateOnly(2026, 1, 1));

        Assert.True(dependente.DedutivelEm(new Competencia(2026, 1)));
        Assert.True(dependente.DedutivelEm(new Competencia(2040, 12)));
        Assert.False(dependente.DedutivelEm(new Competencia(2025, 12)));
    }

    [Fact]
    public void AlterarPeriodo_ValeDaProximaConsultaEmDiante()
    {
        // Prova de que a deducao e uma pergunta feita ao dependente NA
        // competencia, e nao um numero congelado nele. O congelamento de quem
        // ja calculou fica no holerite, nao aqui.
        var dependente = Criar(inicio: new DateOnly(2026, 1, 1));
        var agosto = new Competencia(2026, 8);

        Assert.True(dependente.DedutivelEm(agosto));

        dependente.DefinirDeducaoIrrf(new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 31));

        Assert.False(dependente.DedutivelEm(agosto));
    }
}
