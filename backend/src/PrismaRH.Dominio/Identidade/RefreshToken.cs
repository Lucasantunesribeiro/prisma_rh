namespace PrismaRH.Dominio.Identidade;

/// <summary>
/// Permissao de longa duracao para obter novos access tokens.
///
/// Duas decisoes que valem explicar:
///
/// 1. Guardamos apenas o HASH do token, igual a senha. Um dump do banco nao
///    vira sessao de ninguem.
/// 2. NAO tem IdOrganizacao e NAO entra no filtro global por organizacao. No
///    momento em que ele e lido, o usuario ainda nao esta autenticado e nao
///    existe organizacao no contexto: filtra-lo tornaria o login impossivel.
///    O vinculo com o tenant vem do Usuario dono do token.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public RefreshToken(Guid idUsuario, string tokenHash, DateTimeOffset criadoEm, DateTimeOffset expiraEm)
    {
        if (idUsuario == Guid.Empty)
        {
            throw new ArgumentException("Token precisa pertencer a um usuario.", nameof(idUsuario));
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Hash do token e obrigatorio.", nameof(tokenHash));
        }

        if (expiraEm <= criadoEm)
        {
            throw new ArgumentException("Expiracao precisa ser posterior a criacao.", nameof(expiraEm));
        }

        Id = Guid.CreateVersion7();
        IdUsuario = idUsuario;
        TokenHash = tokenHash;
        CriadoEm = criadoEm;
        ExpiraEm = expiraEm;
    }

    public Guid Id { get; private set; }
    public Guid IdUsuario { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset ExpiraEm { get; private set; }
    public DateTimeOffset? RevogadoEm { get; private set; }

    /// <summary>
    /// Token que substituiu este numa rotacao. Forma a corrente que permite
    /// revogar a familia inteira quando um token ja usado reaparece.
    /// </summary>
    public Guid? SubstituidoPorId { get; private set; }

    public bool EstaRevogado => RevogadoEm is not null;

    public bool EstaExpirado(DateTimeOffset agora) => agora >= ExpiraEm;

    public bool EstaAtivo(DateTimeOffset agora) => !EstaRevogado && !EstaExpirado(agora);

    /// <summary>
    /// Revoga o token. Idempotente de proposito: revogar em cascata a familia
    /// passa por tokens ja revogados, e isso nao pode ser erro.
    /// </summary>
    public void Revogar(DateTimeOffset agora, Guid? substituidoPorId = null)
    {
        RevogadoEm ??= agora;

        if (substituidoPorId is not null)
        {
            SubstituidoPorId = substituidoPorId;
        }
    }
}
