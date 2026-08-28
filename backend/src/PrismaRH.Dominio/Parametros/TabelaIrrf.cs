namespace PrismaRH.Dominio.Parametros;

/// <summary>
/// A tabela do IRRF que valeu a partir de uma data.
///
/// Federal, como TabelaInss e TabelaFgts: sem id_organizacao e fora do filtro
/// global. So o Administrador da Plataforma escreve.
///
/// Guarda MAIS do que faixas, porque o IRRF mensal precisa de quatro coisas
/// que mudam juntas e por norma:
///
/// 1. as faixas progressivas, com parcela a deduzir;
/// 2. a deducao mensal por dependente;
/// 3. o desconto simplificado, que SUBSTITUI todas as deducoes legais;
/// 4. o redutor - o mecanismo que a Lei 15.270/2025 criou para isentar quem
///    ganha ate certo valor sem mexer nas faixas.
///
/// Separar isso em quatro tabelas versionadas permitiria combinar a faixa de
/// um ano com o dependente de outro. Elas vem na mesma norma e mudam juntas.
/// </summary>
public sealed class TabelaIrrf
{
    public const int TamanhoMaximoFonte = 300;

    private readonly List<FaixaIrrf> _faixas = [];

    private TabelaIrrf()
    {
    }

    public TabelaIrrf(
        DateOnly vigenciaInicio,
        string fonte,
        decimal deducaoPorDependente,
        decimal descontoSimplificado,
        decimal redutorBase,
        decimal redutorCoeficiente,
        IEnumerable<(decimal LimiteSuperior, decimal Aliquota, decimal ParcelaADeduzir)> faixas,
        DateTimeOffset criadoEm)
    {
        ArgumentNullException.ThrowIfNull(faixas);

        var informadas = faixas.ToList();

        if (informadas.Count == 0)
        {
            throw new ArgumentException("Tabela de IRRF precisa de pelo menos uma faixa.", nameof(faixas));
        }

        var fonteLimpa = (fonte ?? string.Empty).Trim();

        if (fonteLimpa.Length == 0)
        {
            throw new ArgumentException(
                "Informe a fonte oficial da tabela: lei, portaria ou URL do orgao.", nameof(fonte));
        }

        if (fonteLimpa.Length > TamanhoMaximoFonte)
        {
            throw new ArgumentException(
                $"Fonte pode ter no maximo {TamanhoMaximoFonte} caracteres.", nameof(fonte));
        }

        if (deducaoPorDependente < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deducaoPorDependente), deducaoPorDependente,
                "Deducao por dependente nao pode ser negativa.");
        }

        if (descontoSimplificado < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(descontoSimplificado), descontoSimplificado,
                "Desconto simplificado nao pode ser negativo.");
        }

        if (redutorBase < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(redutorBase), redutorBase, "Base do redutor nao pode ser negativa.");
        }

        // Fracao, como toda aliquota do sistema. O coeficiente da Lei
        // 15.270/2025 e 0,133145 - recusar 13,3145 evita o erro que zeraria o
        // redutor de todo mundo.
        if (redutorCoeficiente < 0 || redutorCoeficiente >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(redutorCoeficiente), redutorCoeficiente,
                "Coeficiente do redutor e fracao entre 0 e 1, nao percentual.");
        }

        // Redutor e opcional: uma tabela de 2025 nao tem nenhum. Mas base sem
        // coeficiente daria redutor constante para qualquer renda, e
        // coeficiente sem base daria redutor sempre negativo. Os dois ou
        // nenhum.
        if (redutorBase > 0 != redutorCoeficiente > 0)
        {
            throw new ArgumentException(
                "Base e coeficiente do redutor precisam vir juntos, ou nenhum dos dois.",
                nameof(redutorBase));
        }

        Id = Guid.CreateVersion7();
        VigenciaInicio = vigenciaInicio;
        Fonte = fonteLimpa;
        DeducaoPorDependente = deducaoPorDependente;
        DescontoSimplificado = descontoSimplificado;
        RedutorBase = redutorBase;
        RedutorCoeficiente = redutorCoeficiente;
        CriadoEm = criadoEm;

        var ordem = 1;
        var limiteAnterior = 0m;

        foreach (var (limite, aliquota, parcela) in informadas)
        {
            var ultima = ordem == informadas.Count;

            // A ultima faixa nao tem teto: o que vier informado nela e
            // ignorado em favor de nulo. O IRRF nao para de subir.
            decimal? limiteEfetivo = ultima ? null : limite;

            if (!ultima && limite <= limiteAnterior)
            {
                throw new ArgumentException(
                    $"Os limites das faixas precisam ser crescentes: {limite} vem depois de {limiteAnterior}.",
                    nameof(faixas));
            }

            _faixas.Add(new FaixaIrrf(Id, ordem, limiteEfetivo, aliquota, parcela));

            ordem++;
            limiteAnterior = limite;
        }
    }

    public Guid Id { get; private set; }

    public DateOnly VigenciaInicio { get; private set; }

    public string Fonte { get; private set; } = string.Empty;

    /// <summary>Quanto cada dependente abate da base, por mes.</summary>
    public decimal DeducaoPorDependente { get; private set; }

    /// <summary>
    /// A alternativa as deducoes legais. Nao se somam: aplica-se o que
    /// resultar na MENOR base.
    /// </summary>
    public decimal DescontoSimplificado { get; private set; }

    /// <summary>Termo constante do redutor. Zero quando a vigencia nao tem redutor.</summary>
    public decimal RedutorBase { get; private set; }

    /// <summary>Coeficiente que multiplica os rendimentos tributaveis no redutor.</summary>
    public decimal RedutorCoeficiente { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public IReadOnlyList<FaixaIrrf> Faixas => [.. _faixas.OrderBy(f => f.Ordem)];

    public bool TemRedutor => RedutorBase > 0;

    /// <summary>
    /// A partir de qual rendimento o redutor zera, derivado da formula.
    ///
    /// Nao e guardado: e RedutorBase / RedutorCoeficiente por definicao, e
    /// guardar os tres permitiria que discordassem. Serve para exibir.
    /// </summary>
    public decimal LimiteDoRedutor =>
        RedutorCoeficiente == 0 ? 0m : decimal.Round(RedutorBase / RedutorCoeficiente, 2);

    /// <summary>O valor ate onde a base fica isenta: o limite da primeira faixa.</summary>
    public decimal LimiteIsencao =>
        _faixas.OrderBy(f => f.Ordem).FirstOrDefault()?.LimiteSuperior ?? 0m;

    public static TabelaIrrf? VigenteEm(IEnumerable<TabelaIrrf> tabelas, DateOnly data)
    {
        ArgumentNullException.ThrowIfNull(tabelas);

        return tabelas
            .Where(t => t.VigenciaInicio <= data)
            .OrderByDescending(t => t.VigenciaInicio)
            .FirstOrDefault();
    }
}
