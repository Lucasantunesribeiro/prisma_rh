namespace PrismaRH.Dominio.Rescisao;

/// <summary>
/// O valor base do FGTS para fins rescisorios de um contrato, INFORMADO e
/// GRAVADO.
///
/// Na etapa 2 ele era parametro de consulta. Virou entidade por duas razoes:
///
/// 1. **Auditoria.** E entrada humana que multiplica dinheiro - 40% dele viram
///    verba. Um numero desses precisa ficar registrado com quem informou e
///    quando, e nao viajar solto numa query string.
/// 2. **A folha de rescisao precisa dele.** Uma folha pode ter varios
///    desligados, cada um com o seu valor base; nao ha como passar todos por
///    parametro na hora de calcular.
///
/// FONTE: FGTS Digital - o valor base corresponde ao total dos depositos
/// devidos durante o contrato, acrescido da remuneracao das contas vinculadas,
/// e o sistema oficial permite informa-lo manualmente.
/// </summary>
public sealed class ValorBaseFgtsRescisorio
{
    public const int TamanhoMaximoObservacao = 500;

    private ValorBaseFgtsRescisorio()
    {
    }

    public ValorBaseFgtsRescisorio(
        Guid idOrganizacao,
        Guid idContrato,
        decimal valor,
        string? observacao,
        DateTimeOffset informadoEm)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        if (idContrato == Guid.Empty)
        {
            throw new ArgumentException("Precisa pertencer a um contrato.", nameof(idContrato));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdContrato = idContrato;
        InformadoEm = informadoEm;

        Informar(valor, observacao, informadoEm);
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdContrato { get; private set; }

    /// <summary>O valor informado. Nunca calculado pelo sistema.</summary>
    public decimal Valor { get; private set; }

    /// <summary>
    /// De onde o analista tirou o numero: extrato da Caixa, FGTS Digital,
    /// planilha. Nao e obrigatorio, mas e o que torna o valor conferivel.
    /// </summary>
    public string? Observacao { get; private set; }

    public DateTimeOffset InformadoEm { get; private set; }

    /// <summary>
    /// Corrige o valor.
    ///
    /// Ao contrario do motivo do desligamento, ESTE campo e alteravel: o
    /// analista pode receber o extrato depois, ou digitar errado. O que nao se
    /// altera e o motivo - aquele e a razao do fato; este e uma medida dele.
    /// </summary>
    public void Informar(decimal valor, string? observacao, DateTimeOffset agora)
    {
        if (valor < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(valor), valor, "Valor base do FGTS nao pode ser negativo.");
        }

        var limpa = observacao?.Trim();

        if (limpa is { Length: > TamanhoMaximoObservacao })
        {
            throw new ArgumentException(
                $"Observacao pode ter no maximo {TamanhoMaximoObservacao} caracteres.",
                nameof(observacao));
        }

        Valor = valor;
        Observacao = string.IsNullOrEmpty(limpa) ? null : limpa;
        InformadoEm = agora;
    }
}
