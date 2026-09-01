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

    // ------------------------------------------- bloqueio progressivo por conta

    /// <summary>
    /// Falhas de login consecutivas. Zera no primeiro acerto.
    ///
    /// Mora no BANCO, e nao em memoria, de proposito: a API roda em Lambda, e
    /// memoria de processo some no proximo cold start - o contador reiniciaria
    /// a cada invocacao e a defesa nao existiria.
    /// </summary>
    public int FalhasDeLogin { get; private set; }

    /// <summary>Ate quando as tentativas sao recusadas. Nulo = liberado.</summary>
    public DateTimeOffset? BloqueadoAte { get; private set; }

    /// <summary>Quando foi a ultima falha. Usado para esquecer o contador.</summary>
    public DateTimeOffset? UltimaFalhaEm { get; private set; }

    public bool EstaBloqueado(DateTimeOffset agora) =>
        BloqueadoAte is { } ate && agora < ate;

    /// <summary>
    /// Registra uma senha errada e devolve por quanto tempo a conta fica
    /// esperando.
    ///
    /// ⚠️ O contador e ESQUECIDO se a ultima falha for antiga
    /// (<see cref="PoliticaBloqueioConta.JanelaDeEsquecimento"/>). Sem isso,
    /// tres erros espalhados por seis meses somariam com o quarto e bloqueariam
    /// alguem que nunca foi atacado.
    /// </summary>
    public TimeSpan RegistrarFalhaDeLogin(DateTimeOffset agora)
    {
        var esqueceu = UltimaFalhaEm is not { } ultima
            || agora - ultima > PoliticaBloqueioConta.JanelaDeEsquecimento;

        FalhasDeLogin = esqueceu ? 1 : FalhasDeLogin + 1;
        UltimaFalhaEm = agora;

        var espera = PoliticaBloqueioConta.EsperaApos(FalhasDeLogin);

        BloqueadoAte = espera == TimeSpan.Zero ? null : agora + espera;

        return espera;
    }

    /// <summary>
    /// ⚠️ **Um acerto zera tudo.**
    ///
    /// E o que impede o bloqueio de virar arma: quem sabe a senha recupera o
    /// acesso assim que a espera termina, sem depender de administrador nenhum.
    /// </summary>
    public void RegistrarEntradaBemSucedida()
    {
        FalhasDeLogin = 0;
        BloqueadoAte = null;
        UltimaFalhaEm = null;
    }

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
