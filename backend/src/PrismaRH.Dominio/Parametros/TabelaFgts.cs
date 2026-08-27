namespace PrismaRH.Dominio.Parametros;

/// <summary>
/// A aliquota de FGTS que valeu a partir de uma data.
///
/// Nao pertence a organizacao alguma, pela mesma razao da TabelaInss: FGTS e
/// lei federal, e deixar cada organizacao ter a sua permitiria que uma delas
/// depositasse errado.
///
/// E uma aliquota unica, e nao faixas: o FGTS incide linearmente sobre a
/// remuneracao (Lei 8.036/90, art. 15). Por isso NAO reusa TabelaInss - forcar
/// as duas no mesmo tipo criaria uma tabela de INSS com uma faixa so, ou uma
/// tabela de FGTS com um teto que ela nao tem. Sao duas regras diferentes que
/// por acaso se parecem.
/// </summary>
public sealed class TabelaFgts
{
    public const int TamanhoMaximoFonte = 300;

    private TabelaFgts()
    {
    }

    public TabelaFgts(DateOnly vigenciaInicio, decimal aliquota, string fonte, DateTimeOffset criadoEm)
    {
        if (aliquota <= 0 || aliquota >= 1)
        {
            // Fracao, nao percentual: 8% e 0.08. Recusar 8 aqui evita o erro
            // que depositaria oito vezes o salario do funcionario.
            throw new ArgumentOutOfRangeException(
                nameof(aliquota), aliquota,
                "Aliquota e fracao entre 0 e 1 (8% = 0.08), nao percentual.");
        }

        // A fonte e obrigatoria por regra do projeto (CLAUDE.md secao 29): sem
        // ela, daqui a um ano ninguem sabe se aquele 8% saiu de lei ou de chute.
        var fonteLimpa = (fonte ?? string.Empty).Trim();

        if (fonteLimpa.Length == 0)
        {
            throw new ArgumentException(
                "Informe a fonte oficial da aliquota: lei, decreto ou URL do orgao.", nameof(fonte));
        }

        if (fonteLimpa.Length > TamanhoMaximoFonte)
        {
            throw new ArgumentException(
                $"Fonte pode ter no maximo {TamanhoMaximoFonte} caracteres.", nameof(fonte));
        }

        Id = Guid.CreateVersion7();
        VigenciaInicio = vigenciaInicio;
        Aliquota = aliquota;
        Fonte = fonteLimpa;
        CriadoEm = criadoEm;
    }

    public Guid Id { get; private set; }

    /// <summary>A partir de quando esta aliquota vale. Nao ha data de fim.</summary>
    public DateOnly VigenciaInicio { get; private set; }

    /// <summary>Fracao, nao percentual: 8% e 0.08.</summary>
    public decimal Aliquota { get; private set; }

    /// <summary>A aliquota como percentual, para exibir na memoria de calculo.</summary>
    public decimal AliquotaPercentual => Aliquota * 100m;

    public string Fonte { get; private set; } = string.Empty;

    public DateTimeOffset CriadoEm { get; private set; }

    /// <summary>
    /// Escolhe, entre as tabelas informadas, a que valia na data.
    ///
    /// Devolve null quando nenhuma comecou ate ali - e devolver null e
    /// deliberado: a folha calcula sem o FGTS e diz isso, em vez de aplicar a
    /// aliquota mais proxima que encontrar.
    /// </summary>
    public static TabelaFgts? VigenteEm(IEnumerable<TabelaFgts> tabelas, DateOnly data)
    {
        ArgumentNullException.ThrowIfNull(tabelas);

        return tabelas
            .Where(t => t.VigenciaInicio <= data)
            .OrderByDescending(t => t.VigenciaInicio)
            .FirstOrDefault();
    }
}
