using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Parametros;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// Fase 4A: incidencias por rubrica e bases apuradas no holerite.
///
/// Nenhum teste aqui usa numero legal - aliquota, faixa ou teto. Esta subfase
/// cria a estrutura da base; quem aplica imposto sobre ela e a 4B em diante.
/// </summary>
public class BasesDeCalculoTestes
{
    /// <summary>Sem dependentes: o cenario padrao da maioria dos testes.</summary>
    private static readonly Dictionary<Guid, int> SemDependentes = [];

    private static readonly DateTimeOffset Agora = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Empresa = Guid.CreateVersion7();
    private static readonly Competencia Agosto = new(2026, 8);

    private const BaseCalculo IntegraTudo = BaseCalculo.Inss | BaseCalculo.Fgts | BaseCalculo.Irrf;

    private static Rubrica Salario(BaseCalculo bases = IntegraTudo) =>
        new(Org, "SAL", "Salario base",
            TipoRubrica.Provento, EstrategiaRubrica.SalarioBaseProporcional, bases, Agora);

    private static Rubrica Provento(string codigo, BaseCalculo bases) =>
        new(Org, codigo, codigo, TipoRubrica.Provento, EstrategiaRubrica.ValorInformado, bases, Agora);

    private static Rubrica Informativa(string codigo, BaseCalculo bases) =>
        new(Org, codigo, codigo, TipoRubrica.Informativo, EstrategiaRubrica.ValorInformado, bases, Agora);

    private static readonly Guid Cargo = Guid.CreateVersion7();
    private static readonly Guid Matriz = Guid.CreateVersion7();

    private static ContratoTrabalho Contrato(decimal salario = 3000m) =>
        new(Org, Guid.CreateVersion7(), Empresa, "1001",
            new DateOnly(2025, 3, 1), salario, Cargo, Matriz, 220, Agora);

    private static FolhaPagamento FolhaCalculada(Rubrica salario, decimal valorSalario = 3000m) =>
        FolhaComContrato(salario, valorSalario).Folha;

    /// <summary>
    /// Devolve a folha E o contrato usado.
    ///
    /// Recalcular com um contrato NOVO (outro Id) faz o holerite anterior
    /// deixar de ser elegivel e ser removido - junto com os lancamentos
    /// manuais. Quem testa recalculo precisa do mesmo contrato nas duas
    /// chamadas.
    /// </summary>
    private static (FolhaPagamento Folha, ContratoTrabalho Contrato) FolhaComContrato(
        Rubrica salario, decimal valorSalario = 3000m)
    {
        var folha = new FolhaPagamento(Org, Empresa, Agosto, Agora);
        var contrato = Contrato(valorSalario);
        folha.Calcular(
            [contrato], salario, [salario],
            ParametrosEncargos.Nenhum, SemDependentes, Agora);
        return (folha, contrato);
    }

    // ---------------------------------------------------------------- enum

    [Fact]
    public void EnumDeBases_TodoValorEPotenciaDeDois_E_EstaEmIndividuais()
    {
        // Numerar em sequencia (1, 2, 3) faria o terceiro valor colidir com a
        // combinacao dos dois primeiros: Inss|Fgts ja vale 3. A consulta
        // passaria a mentir sem erro nenhum.
        foreach (var valor in Enum.GetValues<BaseCalculo>())
        {
            if (valor == BaseCalculo.Nenhuma)
            {
                continue;
            }

            var numero = (int)valor;

            Assert.True(
                (numero & (numero - 1)) == 0,
                $"{valor} vale {numero}, que nao e potencia de dois.");

            Assert.Contains(valor, BasesDeCalculo.Individuais);
        }
    }

    [Fact]
    public void EnumDeBases_IndividuaisNaoTemValorInventado() =>
        Assert.All(
            BasesDeCalculo.Individuais,
            b => Assert.True(Enum.IsDefined(b), $"{b} nao existe no enum."));

    [Theory]
    [InlineData(BaseCalculo.Nenhuma)]
    [InlineData(BaseCalculo.Inss)]
    [InlineData(IntegraTudo)]
    public void Conhecidas_AceitaCombinacaoValida(BaseCalculo bases) =>
        Assert.True(BasesDeCalculo.Conhecidas(bases));

    [Fact]
    public void Conhecidas_RecusaBitInexistente() =>
        Assert.False(BasesDeCalculo.Conhecidas((BaseCalculo)64));

    // ------------------------------------------------------------- rubrica

    [Fact]
    public void RubricaDeDesconto_ComIncidencia_ERecusada()
    {
        // Desconto nao reduz base. O que reduz e deducao, e isso e a Fase 4D.
        var erro = Assert.Throws<ArgumentException>(() => new Rubrica(
            Org, "VT", "Vale-transporte",
            TipoRubrica.Desconto, EstrategiaRubrica.ValorInformado, BaseCalculo.Inss, Agora));

        Assert.Contains("desconto", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RubricaDeDesconto_SemIncidencia_EAceita()
    {
        var rubrica = new Rubrica(
            Org, "VT", "Vale-transporte",
            TipoRubrica.Desconto, EstrategiaRubrica.ValorInformado, BaseCalculo.Nenhuma, Agora);

        Assert.Equal(BaseCalculo.Nenhuma, rubrica.BasesIncidentes);
    }

    [Fact]
    public void RubricaInformativa_PodeComporBase()
    {
        // E para isso que o tipo Informativo existe: nao mexe no liquido, mas
        // entra na base.
        var rubrica = Informativa("BFG", BaseCalculo.Fgts);

        Assert.Equal(BaseCalculo.Fgts, rubrica.BasesIncidentes);
    }

    [Fact]
    public void Rubrica_ComBitInexistente_ERecusada() =>
        Assert.Throws<ArgumentException>(() => Provento("X", (BaseCalculo)64));

    [Fact]
    public void AlterarIncidencias_MudaARubrica()
    {
        var rubrica = Provento("COM", BaseCalculo.Inss);

        rubrica.AlterarIncidencias(IntegraTudo);

        Assert.Equal(IntegraTudo, rubrica.BasesIncidentes);
    }

    [Fact]
    public void AlterarIncidencias_ParaDesconto_ERecusado()
    {
        var rubrica = new Rubrica(
            Org, "VT", "VT", TipoRubrica.Desconto,
            EstrategiaRubrica.ValorInformado, BaseCalculo.Nenhuma, Agora);

        Assert.Throws<ArgumentException>(() => rubrica.AlterarIncidencias(BaseCalculo.Inss));
    }

    // --------------------------------------------------------------- bases

    [Fact]
    public void HoleriteRecemCalculado_TemAsTresBases()
    {
        var holerite = FolhaCalculada(Salario()).Funcionarios[0];

        Assert.Equal(3, holerite.Bases.Count);
        Assert.Equal(
            [BaseCalculo.Inss, BaseCalculo.Fgts, BaseCalculo.Irrf],
            holerite.Bases.Select(b => b.Base));
    }

    [Fact]
    public void SalarioQueIntegraTudo_FormaAsTresBases()
    {
        var holerite = FolhaCalculada(Salario()).Funcionarios[0];

        Assert.Equal(3000m, holerite.BaseDe(BaseCalculo.Inss));
        Assert.Equal(3000m, holerite.BaseDe(BaseCalculo.Fgts));
        Assert.Equal(3000m, holerite.BaseDe(BaseCalculo.Irrf));
    }

    [Fact]
    public void SalarioSemIncidencia_DeixaAsTresBasesZeradas()
    {
        var holerite = FolhaCalculada(Salario(BaseCalculo.Nenhuma)).Funcionarios[0];

        Assert.All(holerite.Bases, b => Assert.Equal(0m, b.Valor));

        // Mas o liquido continua sendo pago: base zero nao e salario zero.
        Assert.Equal(3000m, holerite.Liquido);
    }

    [Fact]
    public void RubricaQueSoIncideEmFgts_NaoEntraNasOutrasDuas()
    {
        var folha = FolhaCalculada(Salario(BaseCalculo.Inss));
        var holerite = folha.Funcionarios[0];

        folha.AdicionarLancamentoManual(
            holerite.Id, Provento("PREMIO", BaseCalculo.Fgts), 500m, null,
            ParametrosEncargos.Nenhum);

        Assert.Equal(3000m, holerite.BaseDe(BaseCalculo.Inss));
        Assert.Equal(500m, holerite.BaseDe(BaseCalculo.Fgts));
        Assert.Equal(0m, holerite.BaseDe(BaseCalculo.Irrf));
    }

    [Fact]
    public void ComissaoSomaNaBase_E_ValeTransporteNaoReduz()
    {
        var folha = FolhaCalculada(Salario());
        var holerite = folha.Funcionarios[0];

        folha.AdicionarLancamentoManual(
            holerite.Id, Provento("COM", IntegraTudo), 500m, null,
            ParametrosEncargos.Nenhum);

        var vt = new Rubrica(
            Org, "VT", "Vale-transporte",
            TipoRubrica.Desconto, EstrategiaRubrica.ValorInformado, BaseCalculo.Nenhuma, Agora);

        folha.AdicionarLancamentoManual(
            holerite.Id, vt, 180m, null,
            ParametrosEncargos.Nenhum);

        // 3000 + 500. O desconto de 180 sai do liquido, nao da base.
        Assert.Equal(3500m, holerite.BaseDe(BaseCalculo.Inss));
        Assert.Equal(3320m, holerite.Liquido);
    }

    [Fact]
    public void RubricaInformativa_EntraNaBase_E_NaoMexeNoLiquido()
    {
        var folha = FolhaCalculada(Salario());
        var holerite = folha.Funcionarios[0];

        folha.AdicionarLancamentoManual(
            holerite.Id, Informativa("BFG", BaseCalculo.Fgts), 250m, null,
            ParametrosEncargos.Nenhum);

        Assert.Equal(3250m, holerite.BaseDe(BaseCalculo.Fgts));
        Assert.Equal(3000m, holerite.BaseDe(BaseCalculo.Inss));
        Assert.Equal(3000m, holerite.Liquido);
    }

    [Fact]
    public void RemoverLancamento_TiraOValorDaBase()
    {
        var folha = FolhaCalculada(Salario());
        var holerite = folha.Funcionarios[0];

        var lancamento = folha.AdicionarLancamentoManual(
            holerite.Id, Provento("COM", IntegraTudo), 500m, null,
            ParametrosEncargos.Nenhum);

        Assert.Equal(3500m, holerite.BaseDe(BaseCalculo.Inss));

        folha.RemoverLancamento(holerite.Id, lancamento.Id, ParametrosEncargos.Nenhum);

        Assert.Equal(3000m, holerite.BaseDe(BaseCalculo.Inss));
    }

    // ---------------------------------------------------------- congelamento

    [Fact]
    public void LancamentoCongelaAIncidencia_DaRubricaNoMomentoDoCalculo()
    {
        var salario = Salario(BaseCalculo.Inss);
        var holerite = FolhaCalculada(salario).Funcionarios[0];

        Assert.Equal(BaseCalculo.Inss, holerite.Lancamentos[0].BasesIncidentes);
    }

    [Fact]
    public void AlterarIncidenciaDaRubrica_NaoMexeEmHoleriteJaCalculado()
    {
        var salario = Salario(BaseCalculo.Inss);
        var folha = FolhaCalculada(salario);
        var holerite = folha.Funcionarios[0];

        Assert.Equal(3000m, holerite.BaseDe(BaseCalculo.Inss));
        Assert.Equal(0m, holerite.BaseDe(BaseCalculo.Fgts));

        // A lei muda, e a rubrica passa a compor tambem o FGTS.
        salario.AlterarIncidencias(BaseCalculo.Inss | BaseCalculo.Fgts);

        // O holerite ja calculado continua o que era. Este e o teste que
        // separa "congelou" de "so parece que congelou".
        Assert.Equal(0m, holerite.BaseDe(BaseCalculo.Fgts));
    }

    [Fact]
    public void DepoisDeRecalcular_AIncidenciaNovaVale()
    {
        var salario = Salario(BaseCalculo.Inss);
        var (folha, contrato) = FolhaComContrato(salario);

        salario.AlterarIncidencias(BaseCalculo.Inss | BaseCalculo.Fgts);
        folha.Calcular(
            [contrato], salario, [salario],
            ParametrosEncargos.Nenhum, SemDependentes, Agora);

        Assert.Equal(3000m, folha.Funcionarios[0].BaseDe(BaseCalculo.Fgts));
    }

    [Fact]
    public void Recalcular_ReaplicaAIncidenciaAtualNoLancamentoManual()
    {
        // O lancamento manual e do analista quanto a rubrica e ao valor; a
        // incidencia e do catalogo. Sem isto, corrigir uma rubrica mal
        // parametrizada nao consertaria nenhuma folha aberta.
        var salario = Salario();
        var premio = Provento("PRE", BaseCalculo.Inss);
        var (folha, contrato) = FolhaComContrato(salario);
        var holerite = folha.Funcionarios[0];

        folha.AdicionarLancamentoManual(
            holerite.Id, premio, 200m, null,
            ParametrosEncargos.Nenhum);

        Assert.Equal(3200m, holerite.BaseDe(BaseCalculo.Inss));
        Assert.Equal(3000m, holerite.BaseDe(BaseCalculo.Fgts));

        premio.AlterarIncidencias(BaseCalculo.Inss | BaseCalculo.Fgts);
        folha.Calcular(
            [contrato], salario, [salario, premio],
            ParametrosEncargos.Nenhum, SemDependentes, Agora);

        Assert.Equal(3200m, holerite.BaseDe(BaseCalculo.Fgts));

        // E o que era do analista continua intocado.
        var lancamento = holerite.Lancamentos.Single(l => l.CodigoRubrica == "PRE");
        Assert.Equal(200m, lancamento.Valor);
        Assert.Equal(OrigemLancamento.Manual, lancamento.Origem);
    }

    [Fact]
    public void Recalcular_SemOCatalogo_NaoMexeNoLancamentoManual()
    {
        // Rubrica que sumiu do catalogo mantem o que tinha: apagar a
        // incidencia zeraria a base sem ninguem ter pedido.
        var salario = Salario();
        var premio = Provento("PRE", BaseCalculo.Inss);
        var (folha, contrato) = FolhaComContrato(salario);

        folha.AdicionarLancamentoManual(
            folha.Funcionarios[0].Id, premio, 200m, null,
            ParametrosEncargos.Nenhum);
        folha.Calcular(
            [contrato], salario, [salario],
            ParametrosEncargos.Nenhum, SemDependentes, Agora);

        Assert.Equal(3200m, folha.Funcionarios[0].BaseDe(BaseCalculo.Inss));
    }

    [Fact]
    public void Recalcular_NaoDuplicaAsBases()
    {
        var salario = Salario();
        var (folha, contrato) = FolhaComContrato(salario);

        folha.Calcular(
            [contrato], salario, [salario],
            ParametrosEncargos.Nenhum, SemDependentes, Agora);
        folha.Calcular(
            [contrato], salario, [salario],
            ParametrosEncargos.Nenhum, SemDependentes, Agora);

        var holerite = folha.Funcionarios[0];

        Assert.Equal(3, holerite.Bases.Count);
        Assert.Equal(3000m, holerite.BaseDe(BaseCalculo.Inss));
    }

    // ------------------------------------------------------------ memoria

    [Fact]
    public void Compoe_DizQuaisLancamentosFormaramABase()
    {
        var folha = FolhaCalculada(Salario(BaseCalculo.Inss));
        var holerite = folha.Funcionarios[0];

        folha.AdicionarLancamentoManual(
            holerite.Id, Provento("PREMIO", BaseCalculo.Fgts), 500m, null,
            ParametrosEncargos.Nenhum);

        // E esta a memoria de calculo da base: derivada do que ja esta
        // gravado, sem duplicar nada.
        var doInss = holerite.Lancamentos.Where(l => l.Compoe(BaseCalculo.Inss)).ToList();

        Assert.Single(doInss);
        Assert.Equal("SAL", doInss[0].CodigoRubrica);
    }

    [Fact]
    public void Compoe_ComNenhuma_NaoDevolveOHoleriteInteiro()
    {
        // HasFlag(Nenhuma) devolve true para qualquer valor: zero esta contido
        // em tudo. Sem a guarda explicita, isto devolveria todos.
        var holerite = FolhaCalculada(Salario()).Funcionarios[0];

        Assert.DoesNotContain(holerite.Lancamentos, l => l.Compoe(BaseCalculo.Nenhuma));
    }

    [Fact]
    public void EfeitoNaBase_DeDesconto_EZero()
    {
        var folha = FolhaCalculada(Salario());
        var holerite = folha.Funcionarios[0];

        var vt = new Rubrica(
            Org, "VT", "VT", TipoRubrica.Desconto,
            EstrategiaRubrica.ValorInformado, BaseCalculo.Nenhuma, Agora);

        var lancamento = folha.AdicionarLancamentoManual(
            holerite.Id, vt, 180m, null,
            ParametrosEncargos.Nenhum);

        Assert.Equal(180m, lancamento.Valor);
        Assert.Equal(0m, lancamento.EfeitoNaBase);
    }
}
