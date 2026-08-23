namespace PrismaRH.Api.Saude;

/// <summary>Resultado consolidado do health check, no formato consumido pelo frontend.</summary>
public sealed record RespostaSaude(string Status, IReadOnlyList<VerificacaoSaude> Verificacoes);

/// <summary>Resultado de uma verificacao individual (por exemplo, o banco de dados).</summary>
public sealed record VerificacaoSaude(string Nome, string Status, string? Descricao);
