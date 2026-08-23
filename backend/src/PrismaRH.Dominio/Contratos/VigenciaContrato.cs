namespace PrismaRH.Dominio.Contratos;

/// <summary>
/// O que valia para um contrato durante um periodo: salario, cargo, lotacao e
/// jornada, juntos.
///
/// Estao juntos de proposito. A pergunta que a folha faz e sempre "como este
/// contrato estava na competencia X?" - e com tudo numa linha so, a resposta e
/// uma consulta. Separado por tipo, seriam quatro consultas ou quatro juncoes,
/// e cada nova dimensao futura viraria mais uma.
///
/// Uma vigencia NUNCA e alterada depois de fechada. Corrigir o passado no
/// lugar destruiria a rastreabilidade que o CLAUDE.md secao 4.3 exige.
/// </summary>
public sealed class VigenciaContrato
{
    private VigenciaContrato()
    {
    }

    internal VigenciaContrato(
        Guid idOrganizacao,
        Guid idContrato,
        DateOnly validoDe,
        decimal salario,
        Guid idCargo,
        Guid idEstabelecimento,
        int jornadaMensalHoras,
        MotivoVigencia motivo,
        DateTimeOffset criadoEm)
    {
        if (salario <= 0)
        {
            throw new ArgumentException("Salario precisa ser maior que zero.", nameof(salario));
        }

        if (idCargo == Guid.Empty)
        {
            throw new ArgumentException("Vigencia precisa de um cargo.", nameof(idCargo));
        }

        if (idEstabelecimento == Guid.Empty)
        {
            throw new ArgumentException("Vigencia precisa de um estabelecimento.", nameof(idEstabelecimento));
        }

        if (jornadaMensalHoras is <= 0 or > 400)
        {
            throw new ArgumentException(
                "Jornada mensal precisa ficar entre 1 e 400 horas.", nameof(jornadaMensalHoras));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdContrato = idContrato;
        ValidoDe = validoDe;
        Salario = salario;
        IdCargo = idCargo;
        IdEstabelecimento = idEstabelecimento;
        JornadaMensalHoras = jornadaMensalHoras;
        Motivo = motivo;
        CriadoEm = criadoEm;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdContrato { get; private set; }

    /// <summary>Primeiro dia em que esta configuracao vale.</summary>
    public DateOnly ValidoDe { get; private set; }

    /// <summary>Ultimo dia em que valeu. Nulo enquanto for a vigencia atual.</summary>
    public DateOnly? ValidoAte { get; private set; }

    public decimal Salario { get; private set; }
    public Guid IdCargo { get; private set; }
    public Guid IdEstabelecimento { get; private set; }
    public int JornadaMensalHoras { get; private set; }
    public MotivoVigencia Motivo { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    public bool EstaAberta => ValidoAte is null;

    /// <summary>Esta vigencia valia nesta data?</summary>
    public bool Cobre(DateOnly data) =>
        ValidoDe <= data && (ValidoAte is null || data <= ValidoAte.Value);

    internal void Fechar(DateOnly ultimoDia)
    {
        if (ultimoDia < ValidoDe)
        {
            throw new ArgumentException(
                "Fim da vigencia nao pode ser anterior ao inicio.", nameof(ultimoDia));
        }

        ValidoAte = ultimoDia;
    }
}
