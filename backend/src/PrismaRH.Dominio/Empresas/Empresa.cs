namespace PrismaRH.Dominio.Empresas;

/// <summary>
/// Empresa administrada por uma organizacao. E a fronteira de tenant: toda
/// consulta a empresas nasce filtrada por IdOrganizacao.
/// </summary>
public sealed class Empresa
{
    public const int TamanhoMaximoRazaoSocial = 250;
    public const int TamanhoMaximoNomeFantasia = 250;

    private Empresa()
    {
    }

    public Empresa(
        Guid idOrganizacao,
        string razaoSocial,
        Cnpj cnpj,
        DateTimeOffset criadaEm,
        string? nomeFantasia = null)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Empresa precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        RazaoSocial = ValidarTexto(razaoSocial, TamanhoMaximoRazaoSocial, "Razao social", nameof(razaoSocial));
        NomeFantasia = ValidarOpcional(nomeFantasia, TamanhoMaximoNomeFantasia, "Nome fantasia", nameof(nomeFantasia));
        Cnpj = cnpj;
        Ativa = true;
        CriadaEm = criadaEm;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public string RazaoSocial { get; private set; } = string.Empty;
    public string? NomeFantasia { get; private set; }
    public Cnpj Cnpj { get; private set; }
    public bool Ativa { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }

    public void Atualizar(string razaoSocial, string? nomeFantasia)
    {
        RazaoSocial = ValidarTexto(razaoSocial, TamanhoMaximoRazaoSocial, "Razao social", nameof(razaoSocial));
        NomeFantasia = ValidarOpcional(nomeFantasia, TamanhoMaximoNomeFantasia, "Nome fantasia", nameof(nomeFantasia));
    }

    public void Inativar() => Ativa = false;

    public void Reativar() => Ativa = true;

    internal static string ValidarTexto(string valor, int maximo, string rotulo, string parametro)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new ArgumentException($"{rotulo} e obrigatorio.", parametro);
        }

        var limpo = valor.Trim();

        if (limpo.Length > maximo)
        {
            throw new ArgumentException($"{rotulo} excede {maximo} caracteres.", parametro);
        }

        return limpo;
    }

    internal static string? ValidarOpcional(string? valor, int maximo, string rotulo, string parametro)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        return ValidarTexto(valor, maximo, rotulo, parametro);
    }
}
