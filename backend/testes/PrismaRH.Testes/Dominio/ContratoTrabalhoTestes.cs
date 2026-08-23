using PrismaRH.Dominio.Contratos;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// O historico contratual e a fundacao do calculo da Fase 3. Um erro aqui nao
/// estoura: ele devolve o salario errado numa competencia antiga, meses depois,
/// longe da causa. Por isso os testes cobrem as BORDAS dos periodos.
/// </summary>
public class ContratoTrabalhoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Funcionario = Guid.CreateVersion7();
    private static readonly Guid Empresa = Guid.CreateVersion7();
    private static readonly Guid CargoJunior = Guid.CreateVersion7();
    private static readonly Guid CargoPleno = Guid.CreateVersion7();
    private static readonly Guid Matriz = Guid.CreateVersion7();
    private static readonly Guid Filial = Guid.CreateVersion7();

    private static readonly DateOnly Admissao = new(2026, 1, 15);

    private static ContratoTrabalho Novo() => new(
        Org, Funcionario, Empresa, "000123", Admissao,
        salarioInicial: 3000m, CargoJunior, Matriz, jornadaMensalHoras: 220, Agora);

    [Fact]
    public void Criar_JaNasceComAVigenciaDaAdmissao()
    {
        var contrato = Novo();

        var vigencia = Assert.Single(contrato.Vigencias);
        Assert.Equal(MotivoVigencia.Admissao, vigencia.Motivo);
        Assert.Equal(Admissao, vigencia.ValidoDe);
        Assert.True(vigencia.EstaAberta);
        Assert.Equal(3000m, vigencia.Salario);
        Assert.Equal(SituacaoContrato.Ativo, contrato.Situacao);
    }

    [Fact]
    public void RegistrarAlteracao_FechaAAnteriorNaVespera_E_NaoReescreveOPassado()
    {
        var contrato = Novo();
        var aumento = new DateOnly(2026, 6, 1);

        contrato.RegistrarAlteracao(aumento, 4000m, CargoPleno, Matriz, 220,
            MotivoVigencia.AlteracaoSalarial, Agora);

        Assert.Equal(2, contrato.Vigencias.Count);

        var antiga = contrato.Vigencias[0];
        var nova = contrato.Vigencias[1];

        // A antiga termina na VESPERA da nova: sem buraco e sem sobreposicao.
        Assert.Equal(new DateOnly(2026, 5, 31), antiga.ValidoAte);
        Assert.Equal(aumento, nova.ValidoDe);
        Assert.True(nova.EstaAberta);

        // O salario antigo continua consultavel: e isso que o ROADMAP exige.
        Assert.Equal(3000m, antiga.Salario);
        Assert.Equal(CargoJunior, antiga.IdCargo);
    }

    [Theory]
    [InlineData(2026, 5, 31, 3000)]   // vespera do aumento
    [InlineData(2026, 6, 1, 4000)]    // primeiro dia do aumento
    [InlineData(2026, 6, 2, 4000)]
    [InlineData(2026, 1, 15, 3000)]   // dia da admissao
    public void VigenciaEm_AcertaNasBordasDoPeriodo(int ano, int mes, int dia, int salarioEsperado)
    {
        var contrato = Novo();
        contrato.RegistrarAlteracao(new DateOnly(2026, 6, 1), 4000m, CargoPleno, Matriz, 220,
            MotivoVigencia.AlteracaoSalarial, Agora);

        var vigencia = contrato.VigenciaEm(new DateOnly(ano, mes, dia));

        Assert.NotNull(vigencia);
        Assert.Equal(salarioEsperado, vigencia.Salario);
    }

    [Fact]
    public void VigenciaEm_DevolveNuloAntesDaAdmissao()
    {
        var contrato = Novo();

        Assert.Null(contrato.VigenciaEm(Admissao.AddDays(-1)));
    }

    [Fact]
    public void RegistrarAlteracao_ExisteSempreUmaUnicaVigenciaAberta()
    {
        var contrato = Novo();

        contrato.RegistrarAlteracao(new DateOnly(2026, 4, 1), 3500m, CargoJunior, Filial, 220,
            MotivoVigencia.Transferencia, Agora);
        contrato.RegistrarAlteracao(new DateOnly(2026, 6, 1), 4000m, CargoPleno, Filial, 220,
            MotivoVigencia.AlteracaoSalarial, Agora);
        contrato.RegistrarAlteracao(new DateOnly(2026, 8, 1), 4000m, CargoPleno, Filial, 180,
            MotivoVigencia.AlteracaoJornada, Agora);

        Assert.Equal(4, contrato.Vigencias.Count);
        Assert.Single(contrato.Vigencias, v => v.EstaAberta);
    }

    [Theory]
    [InlineData(2026, 1, 15)]   // no mesmo dia da vigencia atual
    [InlineData(2026, 1, 14)]   // antes dela
    [InlineData(2025, 12, 1)]   // muito antes
    public void RegistrarAlteracao_RecusaDataQueCriariaSobreposicao(int ano, int mes, int dia)
    {
        var contrato = Novo();

        var erro = Assert.Throws<ArgumentException>(() =>
            contrato.RegistrarAlteracao(new DateOnly(ano, mes, dia), 4000m, CargoPleno, Matriz, 220,
                MotivoVigencia.AlteracaoSalarial, Agora));

        Assert.Contains("15/01/2026", erro.Message);
    }

    [Fact]
    public void RegistrarAlteracao_RecusaMotivoAdmissao()
    {
        var contrato = Novo();

        Assert.Throws<ArgumentException>(() =>
            contrato.RegistrarAlteracao(new DateOnly(2026, 6, 1), 4000m, CargoPleno, Matriz, 220,
                MotivoVigencia.Admissao, Agora));
    }

    [Fact]
    public void Desligar_FechaAVigenciaAberta_E_ImpedeNovaAlteracao()
    {
        var contrato = Novo();
        var saida = new DateOnly(2026, 7, 31);

        contrato.Desligar(saida);

        Assert.Equal(SituacaoContrato.Desligado, contrato.Situacao);
        Assert.Equal(saida, contrato.DataDesligamento);
        Assert.Null(contrato.VigenciaAtual);
        Assert.Equal(saida, contrato.Vigencias[0].ValidoAte);

        // O historico continua consultavel depois do desligamento.
        Assert.NotNull(contrato.VigenciaEm(new DateOnly(2026, 3, 1)));
        Assert.Null(contrato.VigenciaEm(saida.AddDays(1)));

        Assert.Throws<InvalidOperationException>(() =>
            contrato.RegistrarAlteracao(new DateOnly(2026, 9, 1), 5000m, CargoPleno, Matriz, 220,
                MotivoVigencia.AlteracaoSalarial, Agora));
    }

    [Fact]
    public void Desligar_RecusaDataAnteriorAAdmissao_E_DuplaChamada()
    {
        var contrato = Novo();

        Assert.Throws<ArgumentException>(() => contrato.Desligar(Admissao.AddDays(-1)));

        contrato.Desligar(new DateOnly(2026, 7, 31));
        Assert.Throws<InvalidOperationException>(() => contrato.Desligar(new DateOnly(2026, 8, 31)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Criar_RecusaSalarioNaoPositivo(decimal salario)
    {
        Assert.Throws<ArgumentException>(() => new ContratoTrabalho(
            Org, Funcionario, Empresa, "000123", Admissao,
            salario, CargoJunior, Matriz, 220, Agora));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(401)]
    public void Criar_RecusaJornadaForaDaFaixa(int jornada)
    {
        Assert.Throws<ArgumentException>(() => new ContratoTrabalho(
            Org, Funcionario, Empresa, "000123", Admissao,
            3000m, CargoJunior, Matriz, jornada, Agora));
    }
}
