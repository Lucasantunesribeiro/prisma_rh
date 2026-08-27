namespace PrismaRH.Dominio.Parametros;

/// <summary>
/// A tabela progressiva do INSS que valeu a partir de uma data.
///
/// NAO pertence a organizacao alguma, e e a unica entidade do sistema assim.
/// INSS e lei federal: a mesma tabela vale para todo mundo, e deixar cada
/// organizacao ter a sua permitiria que uma delas descontasse errado sem
/// ninguem notar. Por isso ela fica fora do filtro global e so o
/// Administrador da Plataforma escreve nela.
///
/// A vigencia tem inicio e NAO tem fim: a tabela que vale numa data e a de
/// maior VigenciaInicio menor ou igual a ela. Guardar um fim tambem abriria
/// espaco para buraco entre duas tabelas - e um buraco aqui significa folha
/// que nao calcula, ou pior, que calcula com a tabela errada.
/// </summary>
public sealed class TabelaInss
{
    public const int TamanhoMaximoFonte = 300;

    private readonly List<FaixaInss> _faixas = [];

    private TabelaInss()
    {
    }

    public TabelaInss(
        DateOnly vigenciaInicio,
        string fonte,
        IEnumerable<(decimal LimiteSuperior, decimal Aliquota)> faixas,
        DateTimeOffset criadoEm)
    {
        ArgumentNullException.ThrowIfNull(faixas);

        var informadas = faixas.ToList();

        if (informadas.Count == 0)
        {
            throw new ArgumentException("Tabela de INSS precisa de pelo menos uma faixa.", nameof(faixas));
        }

        // A fonte e obrigatoria por regra do projeto: o CLAUDE.md secao 29
        // exige registrar de onde veio toda regra legal. Sem isso, daqui a um
        // ano ninguem sabe se aquele 14% saiu de portaria ou de chute.
        var fonteLimpa = (fonte ?? string.Empty).Trim();

        if (fonteLimpa.Length == 0)
        {
            throw new ArgumentException(
                "Informe a fonte oficial da tabela: portaria, lei ou URL do orgao.", nameof(fonte));
        }

        if (fonteLimpa.Length > TamanhoMaximoFonte)
        {
            throw new ArgumentException(
                $"Fonte pode ter no maximo {TamanhoMaximoFonte} caracteres.", nameof(fonte));
        }

        Id = Guid.CreateVersion7();
        VigenciaInicio = vigenciaInicio;
        Fonte = fonteLimpa;
        CriadoEm = criadoEm;

        var ordem = 1;
        var limiteAnterior = 0m;

        foreach (var (limite, aliquota) in informadas)
        {
            if (limite <= limiteAnterior)
            {
                throw new ArgumentException(
                    $"Os limites das faixas precisam ser crescentes: {limite} vem depois de {limiteAnterior}.",
                    nameof(faixas));
            }

            _faixas.Add(new FaixaInss(Id, ordem, limite, aliquota));

            ordem++;
            limiteAnterior = limite;
        }
    }

    public Guid Id { get; private set; }

    /// <summary>A partir de quando esta tabela vale. Nao ha data de fim.</summary>
    public DateOnly VigenciaInicio { get; private set; }

    /// <summary>De onde a tabela veio: portaria, lei ou URL oficial.</summary>
    public string Fonte { get; private set; } = string.Empty;

    public DateTimeOffset CriadoEm { get; private set; }

    public IReadOnlyList<FaixaInss> Faixas => [.. _faixas.OrderBy(f => f.Ordem)];

    /// <summary>
    /// Teto do salario-de-contribuicao: o limite da ultima faixa.
    ///
    /// E derivado em vez de guardado a parte porque teto e ultima faixa sao o
    /// mesmo numero por definicao. Guardar os dois permitiria que
    /// discordassem.
    /// </summary>
    public decimal Teto => _faixas.Count == 0 ? 0m : _faixas.Max(f => f.LimiteSuperior);

    /// <summary>
    /// Escolhe, entre as tabelas informadas, a que valia na data.
    ///
    /// Devolve null quando nenhuma tabela comecou ate ali - e devolver null e
    /// deliberado: a folha precisa recusar o calculo com uma mensagem
    /// compreensivel, e nao aplicar a tabela mais proxima que encontrar.
    /// </summary>
    public static TabelaInss? VigenteEm(IEnumerable<TabelaInss> tabelas, DateOnly data)
    {
        ArgumentNullException.ThrowIfNull(tabelas);

        return tabelas
            .Where(t => t.VigenciaInicio <= data)
            .OrderByDescending(t => t.VigenciaInicio)
            .FirstOrDefault();
    }
}
