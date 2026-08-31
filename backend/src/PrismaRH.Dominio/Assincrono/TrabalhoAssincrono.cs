namespace PrismaRH.Dominio.Assincrono;

/// <summary>O que o trabalho faz. Vocabulario fechado.</summary>
public enum TipoTrabalho
{
    ImportacaoFuncionarios = 1,
}

/// <summary>
/// Em que ponto o trabalho esta.
///
/// <code>
/// Enfileirado ──> Processando ──> Concluido
///                      │
///                      └────────> Falhou ──> Enfileirado   (nova tentativa)
/// </code>
/// </summary>
public enum StatusTrabalho
{
    /// <summary>Na fila. Ninguem pegou ainda.</summary>
    Enfileirado = 1,

    /// <summary>Um worker pegou e esta processando.</summary>
    Processando = 2,

    /// <summary>Terminou, e o resultado esta em <see cref="TrabalhoAssincrono.IdRecurso"/>.</summary>
    Concluido = 3,

    /// <summary>Falhou depois de esgotar as tentativas. Foi para a DLQ.</summary>
    Falhou = 4,
}

/// <summary>
/// Um trabalho que roda fora da requisicao HTTP (Fase 9).
///
/// ## O problema que esta classe existe para resolver
///
/// O Security Gate da Fase 9 nomeia a ameaca sem rodeio: *"job perdendo o
/// tenant e processando dado da organizacao errada - **o vazamento mais
/// provavel do produto inteiro**"*.
///
/// A razao e concreta. O `PrismaRhDbContext` tem filtro global por organizacao,
/// e ele resolve o isolamento de **toda consulta dentro de uma requisicao**,
/// sem ninguem precisar lembrar de escrever `where`. Mas o filtro le a
/// organizacao do **usuario autenticado** - e um worker nao tem requisicao,
/// nao tem usuario e nao tem `HttpContext`.
///
/// Fora da requisicao o filtro nao desaparece: ele passa a devolver
/// `Guid.Empty`, que nao casa com nada. Falha fechada, e nao aberta - o worker
/// ingenuo nao vaza, ele simplesmente nao acha nada.
///
/// Por isso o tenant **viaja explicitamente**: fica gravado aqui e vai dentro
/// da mensagem, e o worker abre o contexto a partir dele. Nunca de estado
/// ambiente, nunca do "ultimo usuario que passou por aqui".
///
/// ## Idempotencia
///
/// A SQS entrega **pelo menos uma vez**, e nao exatamente uma. Isso nao e
/// defeito: e a garantia que ela oferece, e quem usa fila precisa conviver com
/// a entrega repetida. Sem chave de idempotencia, a mesma planilha entregue
/// duas vezes criaria os funcionarios duas vezes.
///
/// A chave e o **hash do arquivo** mais a organizacao e o tipo - dois envios do
/// mesmo arquivo pela mesma empresa sao o mesmo trabalho, e o segundo encontra
/// o primeiro em vez de criar outro.
/// </summary>
public sealed class TrabalhoAssincrono
{
    public const int TamanhoMaximoChave = 200;
    public const int TamanhoMaximoErro = 500;

    private TrabalhoAssincrono()
    {
    }

    public TrabalhoAssincrono(
        Guid idOrganizacao,
        Guid idUsuario,
        TipoTrabalho tipo,
        string chaveIdempotencia,
        DateTimeOffset agora)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Trabalho precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        if (idUsuario == Guid.Empty)
        {
            throw new ArgumentException("Trabalho precisa ter um solicitante.", nameof(idUsuario));
        }

        if (string.IsNullOrWhiteSpace(chaveIdempotencia))
        {
            throw new ArgumentException("Chave de idempotencia e obrigatoria.", nameof(chaveIdempotencia));
        }

        if (chaveIdempotencia.Length > TamanhoMaximoChave)
        {
            throw new ArgumentException(
                $"Chave de idempotencia excede {TamanhoMaximoChave} caracteres.", nameof(chaveIdempotencia));
        }

        // O Id é o identificador de correlacao: o mesmo numero aparece no log
        // do CloudWatch, na mensagem da fila e na trilha de auditoria. E por
        // ele que se sai de um e se chega no outro.
        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdUsuario = idUsuario;
        Tipo = tipo;
        ChaveIdempotencia = chaveIdempotencia.Trim();
        Status = StatusTrabalho.Enfileirado;
        CriadoEm = agora;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdUsuario { get; private set; }
    public TipoTrabalho Tipo { get; private set; }
    public string ChaveIdempotencia { get; private set; } = string.Empty;
    public StatusTrabalho Status { get; private set; }
    public int Tentativas { get; private set; }

    /// <summary>O que o trabalho produziu. Para importacao, o `Id` da <c>Importacao</c>.</summary>
    public Guid? IdRecurso { get; private set; }

    public string? Erro { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset? IniciadoEm { get; private set; }
    public DateTimeOffset? ConcluidoEm { get; private set; }

    /// <summary>Ainda vai acontecer alguma coisa com este trabalho?</summary>
    public bool Pendente => Status is StatusTrabalho.Enfileirado or StatusTrabalho.Processando;

    /// <summary>
    /// ⚠️ **A conferencia que impede o vazamento entre organizacoes.**
    ///
    /// A mensagem da fila e **dado nao confiavel** - item 2 do Security Gate.
    /// Ela pode ter sido adulterada, reprocessada de um contexto antigo, ou
    /// simplesmente montada errada por um defeito.
    ///
    /// O worker nao aceita o tenant da mensagem de bom grado: ele carrega o
    /// trabalho pelo id e **confere** contra o que esta gravado aqui. Se
    /// divergirem, a mensagem e recusada sem processar nada.
    ///
    /// Sem isto, trocar um `Guid` na mensagem faria o worker processar a
    /// planilha de uma empresa dentro da organizacao de outra.
    /// </summary>
    public bool PertenceA(Guid idOrganizacao) =>
        idOrganizacao != Guid.Empty && idOrganizacao == IdOrganizacao;

    /// <summary>
    /// Um worker pegou o trabalho.
    ///
    /// Devolve <c>false</c> quando ja terminou - e e isso que torna o retry
    /// seguro: a segunda entrega da mesma mensagem encontra um trabalho
    /// `Concluido` e vai embora sem refazer nada.
    /// </summary>
    public bool Iniciar(DateTimeOffset agora)
    {
        if (Status is StatusTrabalho.Concluido)
        {
            return false;
        }

        Status = StatusTrabalho.Processando;
        Tentativas++;
        IniciadoEm ??= agora;
        Erro = null;

        return true;
    }

    public void Concluir(Guid idRecurso, DateTimeOffset agora)
    {
        if (idRecurso == Guid.Empty)
        {
            throw new ArgumentException("Trabalho concluido precisa apontar para o que produziu.", nameof(idRecurso));
        }

        Status = StatusTrabalho.Concluido;
        IdRecurso = idRecurso;
        ConcluidoEm = agora;
        Erro = null;
    }

    /// <summary>
    /// O trabalho falhou.
    ///
    /// Enquanto houver tentativa sobrando ele volta para `Enfileirado`, e a
    /// fila o entrega de novo. Esgotadas as tentativas ele vira `Falhou` de
    /// vez, e a mensagem vai para a DLQ.
    ///
    /// O teto existe porque **retry sem limite e como um defeito vira
    /// despesa**: a mensagem que sempre falha volta para sempre, e cada volta
    /// consome invocacao e GB-segundo.
    /// </summary>
    public void Falhar(string motivo, int maximoTentativas, DateTimeOffset agora)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximoTentativas);

        var limpo = string.IsNullOrWhiteSpace(motivo) ? "Falha nao informada." : motivo.Trim();

        Erro = limpo.Length > TamanhoMaximoErro ? limpo[..TamanhoMaximoErro] : limpo;

        if (Tentativas >= maximoTentativas)
        {
            Status = StatusTrabalho.Falhou;
            ConcluidoEm = agora;
            return;
        }

        Status = StatusTrabalho.Enfileirado;
    }

    /// <summary>
    /// Falha que **nao adianta tentar de novo**, independente das tentativas
    /// restantes.
    ///
    /// Existe porque <see cref="Falhar"/> devolve o trabalho para a fila
    /// enquanto houver tentativa sobrando - o que esta certo para banco fora do
    /// ar, e errado para o arquivo que nao existe mais: ele nao vai reaparecer.
    ///
    /// Sem esta distincao, um trabalho cujo blob expirou ficaria `Enfileirado`
    /// para sempre, com a mensagem ja descartada da fila - **pendente eterno**,
    /// que e pior que falho, porque a tela promete que ainda vai acontecer.
    /// Um teste pegou exatamente isso.
    /// </summary>
    public void FalharDefinitivamente(string motivo, DateTimeOffset agora)
    {
        var limpo = string.IsNullOrWhiteSpace(motivo) ? "Falha nao informada." : motivo.Trim();

        Erro = limpo.Length > TamanhoMaximoErro ? limpo[..TamanhoMaximoErro] : limpo;
        Status = StatusTrabalho.Falhou;
        ConcluidoEm = agora;
    }

    /// <summary>
    /// A chave de idempotencia de uma importacao.
    ///
    /// Hash do conteudo + organizacao + tipo. O hash sozinho nao serve: duas
    /// organizacoes podem importar o **mesmo arquivo modelo**, e sao dois
    /// trabalhos distintos - juntar os dois seria vazamento, nao economia.
    /// </summary>
    public static string ChaveDeImportacao(Guid idOrganizacao, string hashSha256) =>
        string.IsNullOrWhiteSpace(hashSha256)
            ? throw new ArgumentException("Hash e obrigatorio.", nameof(hashSha256))
            : $"{TipoTrabalho.ImportacaoFuncionarios}:{idOrganizacao:N}:{hashSha256}";
}
