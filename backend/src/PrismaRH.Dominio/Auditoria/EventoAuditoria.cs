namespace PrismaRH.Dominio.Auditoria;

/// <summary>
/// O que aconteceu.
///
/// Vocabulario fechado, como todo o resto do sistema: nao ha string vinda de
/// fora que vire acao auditada. Uma acao nova exige uma linha aqui, num diff
/// que alguem le.
///
/// A lista sai do `ROADMAP.md` da Fase 7 e do `CLAUDE.md secao 24.17` - e
/// contem **so o que existe**. Evento previsto e nao implementado viraria uma
/// trilha com buracos que ninguem sabe explicar.
/// </summary>
public enum AcaoAuditada
{
    // ------------------------------------------------------------ cadastro
    FuncionarioCriado = 1,
    FuncionarioAlterado = 2,

    /// <summary>Nova vigencia contratual - inclui alteracao salarial.</summary>
    VigenciaContratualRegistrada = 10,

    ContratoDesligado = 11,

    // --------------------------------------------------------------- folha
    FolhaCalculada = 20,
    FolhaFechada = 21,

    /// <summary>Lancamento digitado a mao num holerite.</summary>
    LancamentoManualCriado = 22,

    LancamentoManualRemovido = 23,

    // ------------------------------------------------------------ rubricas
    RubricaCriada = 30,
    RubricaAlterada = 31,

    // ------------------------------------------------------- valor do FGTS
    /// <summary>
    /// O Valor Base do FGTS rescisorio.
    ///
    /// ⚠️ Resolve a pendencia do `CLAUDE.md secao 24.19 item 6`, aberta na
    /// Fase 4G: e entrada humana que MULTIPLICA dinheiro - 40% ou 20% dela
    /// viram a indenizacao compensatoria - e ate aqui era sobrescrita sem
    /// deixar rastro de quem, de quando, nem do valor anterior.
    /// </summary>
    ValorBaseFgtsInformado = 40,

    // ------------------------------------------------------------- analise
    /// <summary>
    /// Configuracao de regra de analise.
    ///
    /// ⚠️ Resolve a pendencia do `CLAUDE.md secao 24.19 item 7`, aberta na
    /// Fase 6: afrouxar uma tolerancia e o jeito mais barato de fazer uma
    /// divergencia sumir do relatorio.
    /// </summary>
    RegraAnaliseConfigurada = 50,

    AnaliseExecutada = 51,

    // ------------------------------------------------------------ workflow
    InconsistenciaTransitada = 60,
    InconsistenciaAtribuida = 61,
    InconsistenciaComentada = 62,
    EvidenciaRegistrada = 63,

    // --------------------------------------------------------- importacao
    ImportacaoAplicada = 70,
    ImportacaoRecusada = 71,

    // --------------------------------------------- integracao externa (Fase 8)

    /// <summary>
    /// O Prisma RH perguntou por um CNPJ a um servico de fora.
    ///
    /// Isto e auditado porque **enviar dado para fora e decisao de
    /// privacidade**, e o Security Gate da Fase 8 pede "registro do que foi
    /// enviado". Aqui o que sai da nossa rede e exatamente o CNPJ digitado - e
    /// e ele que fica no contexto do evento, para a pergunta "o que este
    /// sistema ja contou a terceiros?" ter resposta.
    /// </summary>
    CnpjConsultado = 80,
}

/// <summary>Sobre o que a acao foi.</summary>
public enum EntidadeAuditada
{
    Funcionario = 1,
    ContratoTrabalho = 2,
    FolhaPagamento = 3,
    Rubrica = 4,
    ValorBaseFgtsRescisorio = 5,
    RegraAnalise = 6,
    ExecucaoAnalise = 7,
    ResultadoAnalise = 8,
    Importacao = 9,

    /// <summary>
    /// A consulta externa em si (Fase 8).
    ///
    /// Nao existe tabela `consultas_cnpj`, e nao deveria existir: a consulta nao
    /// e uma entidade do sistema, e sim um **fato que aconteceu**. O
    /// `IdEntidade` do evento e o identificador de correlacao gerado para
    /// aquela chamada - o mesmo que aparece no log tecnico -, o que permite
    /// partir da trilha de negocio e achar a linha do log, e vice-versa.
    /// </summary>
    ConsultaCnpj = 10,
}

/// <summary>
/// Um evento de negocio, registrado para sempre.
///
/// ## Isto NAO e log tecnico
///
/// O `CLAUDE.md secao 26` separa as duas coisas, e a separacao e pratica: o log
/// tecnico e rotativo, descartavel, tem retencao curta e acesso mais amplo. Ele
/// responde "por que a requisicao demorou". Esta tabela responde **"quem
/// alterou o salario dela, quando, e de quanto para quanto"** - e essa pergunta
/// pode aparecer anos depois, numa reclamacao trabalhista.
///
/// ## Somente-insercao, e sem excecao
///
/// Nao ha metodo de alteracao. Nao ha metodo de remocao. Nao ha endpoint de
/// edicao nem de exclusao, **para perfil nenhum** - inclusive Administrador da
/// Plataforma. O `CLAUDE.md secao 24.17` e o Security Gate da Fase 7 exigem
/// isso, e o teste de integracao percorre as rotas provando que nao existem.
///
/// ## O que ele NAO guarda
///
/// Conteudo sensivel. <see cref="Contexto"/> carrega identificadores e valores
/// que explicam a mudanca - "de 3000,00 para 3500,00" -, e nao o cadastro
/// inteiro. O `CLAUDE.md secao 24.16` e claro: a auditoria precisa permitir
/// entender o que houve **sem virar um segundo banco de dados pessoais**.
/// </summary>
public sealed class EventoAuditoria
{
    public const int TamanhoMaximoDescricao = 300;
    public const int TamanhoMaximoContexto = 500;

    private EventoAuditoria()
    {
    }

    public EventoAuditoria(
        Guid idOrganizacao,
        Guid? idUsuario,
        AcaoAuditada acao,
        EntidadeAuditada entidade,
        Guid idEntidade,
        string descricao,
        DateTimeOffset agora,
        string? contexto = null)
    {
        if (idOrganizacao == Guid.Empty)
        {
            // Auditoria SEMPRE registra a organizacao (Security Gate, item 4).
            // Sem ela o evento nao pertence a ninguem e some no filtro global.
            throw new ArgumentException("Organizacao e obrigatoria.", nameof(idOrganizacao));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdUsuario = idUsuario == Guid.Empty ? null : idUsuario;
        Acao = acao;
        Entidade = entidade;
        IdEntidade = idEntidade;
        Descricao = Cortar(descricao, TamanhoMaximoDescricao);
        Contexto = string.IsNullOrWhiteSpace(contexto)
            ? null
            : Cortar(contexto, TamanhoMaximoContexto);
        OcorridoEm = agora;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }

    /// <summary>
    /// Quem fez. Anulavel so para a semeadura e para rotinas do proprio
    /// sistema - toda acao vinda de requisicao autenticada tem usuario.
    /// </summary>
    public Guid? IdUsuario { get; private set; }

    public AcaoAuditada Acao { get; private set; }
    public EntidadeAuditada Entidade { get; private set; }
    public Guid IdEntidade { get; private set; }

    /// <summary>Uma frase em portugues, legivel por quem nao conhece o sistema.</summary>
    public string Descricao { get; private set; } = string.Empty;

    /// <summary>Identificadores e valores que explicam a mudanca. Nunca o cadastro inteiro.</summary>
    public string? Contexto { get; private set; }

    public DateTimeOffset OcorridoEm { get; private set; }

    private static string Cortar(string? texto, int maximo)
    {
        var limpo = (texto ?? string.Empty).Trim();

        return limpo.Length > maximo ? limpo[..maximo] : limpo;
    }
}
