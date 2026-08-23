namespace PrismaRH.Dominio.Pessoas;

/// <summary>
/// A PESSOA. Nao confundir com o vinculo: quem tem matricula, admissao e
/// salario e o ContratoTrabalho. Uma pessoa readmitida continua sendo o mesmo
/// funcionario, com um contrato novo.
/// </summary>
public sealed class Funcionario
{
    public const int TamanhoMaximoNome = 200;

    private Funcionario()
    {
    }

    public Funcionario(
        Guid idOrganizacao,
        string nome,
        Cpf cpf,
        DateOnly dataNascimento,
        DateTimeOffset criadoEm)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Funcionario precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        var hoje = DateOnly.FromDateTime(criadoEm.UtcDateTime);

        if (dataNascimento >= hoje)
        {
            throw new ArgumentException("Data de nascimento precisa ser no passado.", nameof(dataNascimento));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        Nome = ValidarNome(nome);
        Cpf = cpf;
        DataNascimento = dataNascimento;
        Ativo = true;
        CriadoEm = criadoEm;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public Cpf Cpf { get; private set; }
    public DateOnly DataNascimento { get; private set; }
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    /// <summary>
    /// O CPF NAO e alteravel. Se estiver errado, o cadastro esta errado - e
    /// corrigir CPF em silencio quebraria a rastreabilidade de tudo que ja foi
    /// calculado para essa pessoa.
    /// </summary>
    public void Atualizar(string nome, DateOnly dataNascimento)
    {
        Nome = ValidarNome(nome);
        DataNascimento = dataNascimento;
    }

    public void Inativar() => Ativo = false;

    public void Reativar() => Ativo = true;

    private static string ValidarNome(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ArgumentException("Nome do funcionario e obrigatorio.", nameof(nome));
        }

        var limpo = nome.Trim();

        if (limpo.Length > TamanhoMaximoNome)
        {
            throw new ArgumentException(
                $"Nome do funcionario excede {TamanhoMaximoNome} caracteres.", nameof(nome));
        }

        return limpo;
    }
}
