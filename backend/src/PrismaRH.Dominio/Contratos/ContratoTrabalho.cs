namespace PrismaRH.Dominio.Contratos;

/// <summary>
/// O VINCULO entre uma pessoa e uma empresa. Tem matricula, admissao e,
/// eventualmente, desligamento.
///
/// E o agregado que guarda as vigencias e impoe as regras do historico. As
/// vigencias so podem ser criadas atraves daqui - o construtor delas e
/// internal de proposito, para que ninguem consiga inserir um periodo solto
/// que se sobreponha aos existentes.
/// </summary>
public sealed class ContratoTrabalho
{
    public const int TamanhoMaximoMatricula = 30;

    private readonly List<VigenciaContrato> _vigencias = [];

    private ContratoTrabalho()
    {
    }

    public ContratoTrabalho(
        Guid idOrganizacao,
        Guid idFuncionario,
        Guid idEmpresa,
        string matricula,
        DateOnly dataAdmissao,
        decimal salarioInicial,
        Guid idCargo,
        Guid idEstabelecimento,
        int jornadaMensalHoras,
        DateTimeOffset criadoEm)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Contrato precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        if (idFuncionario == Guid.Empty)
        {
            throw new ArgumentException("Contrato precisa de um funcionario.", nameof(idFuncionario));
        }

        if (idEmpresa == Guid.Empty)
        {
            throw new ArgumentException("Contrato precisa de uma empresa.", nameof(idEmpresa));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdFuncionario = idFuncionario;
        IdEmpresa = idEmpresa;
        Matricula = Cargo.ValidarTexto(matricula, TamanhoMaximoMatricula, "Matricula", nameof(matricula));
        DataAdmissao = dataAdmissao;
        Situacao = SituacaoContrato.Ativo;
        CriadoEm = criadoEm;

        // Contrato sem vigencia nao existe: no dia da admissao ja ha salario,
        // cargo e lotacao. Criar os dois juntos evita um estado intermediario
        // em que a folha nao saberia o que pagar.
        _vigencias.Add(new VigenciaContrato(
            idOrganizacao, Id, dataAdmissao, salarioInicial,
            idCargo, idEstabelecimento, jornadaMensalHoras,
            MotivoVigencia.Admissao, criadoEm));
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdFuncionario { get; private set; }
    public Guid IdEmpresa { get; private set; }
    public string Matricula { get; private set; } = string.Empty;
    public DateOnly DataAdmissao { get; private set; }
    public DateOnly? DataDesligamento { get; private set; }

    /// <summary>
    /// Por que o contrato terminou. Nulo enquanto ele estiver ativo.
    ///
    /// Nao ha metodo para alterar: corrigir o motivo de um desligamento ja
    /// registrado e operacao de correcao, com efeito financeiro, e nao um
    /// ajuste de cadastro. Entra quando a Fase 4G definir esse fluxo.
    /// </summary>
    public MotivoDesligamento? MotivoDesligamento { get; private set; }
    public SituacaoContrato Situacao { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    public IReadOnlyList<VigenciaContrato> Vigencias => _vigencias;

    /// <summary>A vigencia ainda aberta, ou nulo se o contrato foi desligado.</summary>
    public VigenciaContrato? VigenciaAtual => _vigencias.SingleOrDefault(v => v.EstaAberta);

    /// <summary>
    /// O que valia nesta data. E a pergunta que o motor de calculo da Fase 3
    /// fara para cada contrato, em cada competencia.
    /// </summary>
    public VigenciaContrato? VigenciaEm(DateOnly data) =>
        _vigencias.FirstOrDefault(v => v.Cobre(data));

    /// <summary>
    /// Registra uma alteracao contratual: fecha a vigencia aberta na vespera e
    /// abre uma nova. O passado NAO e reescrito.
    /// </summary>
    public VigenciaContrato RegistrarAlteracao(
        DateOnly validoDe,
        decimal salario,
        Guid idCargo,
        Guid idEstabelecimento,
        int jornadaMensalHoras,
        MotivoVigencia motivo,
        DateTimeOffset agora)
    {
        if (motivo == MotivoVigencia.Admissao)
        {
            throw new ArgumentException(
                "A vigencia de admissao e criada com o contrato e nao pode ser registrada de novo.",
                nameof(motivo));
        }

        if (Situacao == SituacaoContrato.Desligado)
        {
            throw new InvalidOperationException(
                "Contrato desligado nao aceita nova vigencia. Readmissao e um contrato novo.");
        }

        var aberta = VigenciaAtual
            ?? throw new InvalidOperationException("Contrato ativo sem vigencia aberta: estado inconsistente.");

        if (validoDe <= aberta.ValidoDe)
        {
            // Permitir isso criaria periodos sobrepostos, e a consulta por data
            // passaria a devolver duas linhas - com a folha escolhendo uma
            // delas em silencio.
            throw new ArgumentException(
                $"A alteracao precisa comecar depois de {aberta.ValidoDe:dd/MM/yyyy}, "
                + "que e o inicio da vigencia atual.",
                nameof(validoDe));
        }

        aberta.Fechar(validoDe.AddDays(-1));

        var nova = new VigenciaContrato(
            IdOrganizacao, Id, validoDe, salario,
            idCargo, idEstabelecimento, jornadaMensalHoras, motivo, agora);

        _vigencias.Add(nova);

        return nova;
    }

    /// <summary>Encerra o vinculo. A ultima vigencia fecha no dia do desligamento.</summary>
    /// <summary>
    /// Encerra o contrato.
    ///
    /// O MOTIVO e obrigatorio, e nao um detalhe de cadastro: ele decide quais
    /// verbas rescisorias sao devidas. Desligar sem motivo deixaria a Fase 4G
    /// sem a informacao mais importante que ela precisa, e preenche-la depois
    /// significaria reabrir um fato ja registrado.
    /// </summary>
    public void Desligar(DateOnly dataDesligamento, MotivoDesligamento motivo)
    {
        if (Situacao == SituacaoContrato.Desligado)
        {
            throw new InvalidOperationException("Contrato ja esta desligado.");
        }

        if (!Enum.IsDefined(motivo))
        {
            throw new ArgumentException("Motivo de desligamento desconhecido.", nameof(motivo));
        }

        if (dataDesligamento < DataAdmissao)
        {
            throw new ArgumentException(
                "Desligamento nao pode ser anterior a admissao.", nameof(dataDesligamento));
        }

        var aberta = VigenciaAtual
            ?? throw new InvalidOperationException("Contrato ativo sem vigencia aberta: estado inconsistente.");

        if (dataDesligamento < aberta.ValidoDe)
        {
            throw new ArgumentException(
                "Desligamento nao pode ser anterior ao inicio da vigencia atual.", nameof(dataDesligamento));
        }

        aberta.Fechar(dataDesligamento);

        DataDesligamento = dataDesligamento;
        MotivoDesligamento = motivo;
        Situacao = SituacaoContrato.Desligado;
    }
}
