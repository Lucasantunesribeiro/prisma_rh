namespace PrismaRH.Infraestrutura.Identidade;

/// <summary>Configuracao da emissao de tokens. Lida de Jwt no appsettings ou do ambiente.</summary>
public sealed class OpcoesJwt
{
    public const string Secao = "Jwt";

    /// <summary>Chave de assinatura HMAC. Fora de Development vem SEMPRE de variavel de ambiente.</summary>
    public string ChaveAssinatura { get; set; } = string.Empty;

    public string Emissor { get; set; } = "prisma-rh";

    public string Audiencia { get; set; } = "prisma-rh";

    /// <summary>Curta de proposito: o refresh token e quem sustenta a sessao.</summary>
    public int MinutosAccessToken { get; set; } = 15;

    public int DiasRefreshToken { get; set; } = 7;
}
