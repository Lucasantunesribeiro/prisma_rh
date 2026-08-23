namespace PrismaRH.Dominio.Identidade;

/// <summary>
/// Tenant do Prisma RH: escritorio de BPO, grupo empresarial ou departamento
/// pessoal centralizado. Toda empresa e todo usuario pertencem a uma.
/// </summary>
public sealed class Organizacao
{
    public const int TamanhoMaximoNome = 200;

    // Construtor sem parametros exigido pelo EF Core para materializar a entidade.
    private Organizacao()
    {
    }

    public Organizacao(string nome, DateTimeOffset criadaEm)
    {
        Id = Guid.CreateVersion7();
        Nome = ValidarNome(nome);
        Ativa = true;
        CriadaEm = criadaEm;
    }

    public Guid Id { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public bool Ativa { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }

    public void Renomear(string nome) => Nome = ValidarNome(nome);

    public void Inativar() => Ativa = false;

    public void Reativar() => Ativa = true;

    private static string ValidarNome(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ArgumentException("Nome da organizacao e obrigatorio.", nameof(nome));
        }

        var limpo = nome.Trim();

        if (limpo.Length > TamanhoMaximoNome)
        {
            throw new ArgumentException(
                $"Nome da organizacao excede {TamanhoMaximoNome} caracteres.", nameof(nome));
        }

        return limpo;
    }
}
