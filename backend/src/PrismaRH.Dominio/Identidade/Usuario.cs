namespace PrismaRH.Dominio.Identidade;

/// <summary>
/// Pessoa com acesso ao Prisma RH. Pertence a exatamente uma organizacao.
///
/// A entidade nunca conhece a senha em texto puro: recebe e guarda apenas o
/// hash. Quem sabe hashear e a camada de infraestrutura.
/// </summary>
public sealed class Usuario
{
    public const int TamanhoMaximoNome = 200;

    // 320 = 64 (parte local) + 1 (@) + 255 (dominio), limite da RFC 5321.
    public const int TamanhoMaximoEmail = 320;

    private Usuario()
    {
    }

    public Usuario(
        Guid idOrganizacao,
        string nome,
        string email,
        string senhaHash,
        Perfil perfil,
        DateTimeOffset criadoEm)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Usuario precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        Nome = ValidarNome(nome);
        Email = NormalizarEmail(email);
        SenhaHash = ValidarSenhaHash(senhaHash);
        Perfil = perfil;
        Ativo = true;
        CriadoEm = criadoEm;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string SenhaHash { get; private set; } = string.Empty;
    public Perfil Perfil { get; private set; }
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    public void AlterarSenha(string novoHash) => SenhaHash = ValidarSenhaHash(novoHash);

    public void AlterarPerfil(Perfil perfil) => Perfil = perfil;

    public void Renomear(string nome) => Nome = ValidarNome(nome);

    public void Inativar() => Ativo = false;

    public void Reativar() => Ativo = true;

    /// <summary>
    /// Normaliza o e-mail para comparacao. Precisa ser usada TAMBEM na busca do
    /// login: se o cadastro grava minusculo e a busca procura o que o usuario
    /// digitou, "Lucas@x.com" nunca acha "lucas@x.com" e o indice unico deixa
    /// de impedir duplicata.
    /// </summary>
    public static string NormalizarEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("E-mail e obrigatorio.", nameof(email));
        }

        var limpo = email.Trim().ToLowerInvariant();

        if (limpo.Length > TamanhoMaximoEmail)
        {
            throw new ArgumentException(
                $"E-mail excede {TamanhoMaximoEmail} caracteres.", nameof(email));
        }

        // Validacao deliberadamente rasa. Validar e-mail pela RFC inteira e um
        // poco sem fundo, e nao impede endereco inexistente. Quem prova que o
        // endereco existe e a confirmacao por e-mail, que nao pertence a Fase 1.
        var arroba = limpo.IndexOf('@');
        var valido = arroba > 0
            && arroba == limpo.LastIndexOf('@')
            && arroba < limpo.Length - 1
            && !limpo.Contains(' ')
            && limpo.IndexOf('.', arroba) > arroba + 1;

        if (!valido)
        {
            throw new ArgumentException($"E-mail invalido: '{email}'.", nameof(email));
        }

        return limpo;
    }

    private static string ValidarNome(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ArgumentException("Nome do usuario e obrigatorio.", nameof(nome));
        }

        var limpo = nome.Trim();

        if (limpo.Length > TamanhoMaximoNome)
        {
            throw new ArgumentException(
                $"Nome do usuario excede {TamanhoMaximoNome} caracteres.", nameof(nome));
        }

        return limpo;
    }

    private static string ValidarSenhaHash(string senhaHash)
    {
        if (string.IsNullOrWhiteSpace(senhaHash))
        {
            throw new ArgumentException("Hash da senha e obrigatorio.", nameof(senhaHash));
        }

        return senhaHash;
    }
}
