namespace PrismaRH.Dominio.Empresas;

/// <summary>
/// Filial ou unidade de uma empresa.
///
/// Guarda IdOrganizacao alem de IdEmpresa de proposito. E redundante - daria
/// para chegar na organizacao via empresa - mas permite que o filtro global por
/// tenant seja aplicado direto, sem join. Filtro que depende de join e filtro
/// que pode ser contornado por uma consulta escrita de outro jeito.
/// </summary>
public sealed class Estabelecimento
{
    public const int TamanhoMaximoNome = 250;
    public const int TamanhoMaximoCodigo = 30;

    private Estabelecimento()
    {
    }

    public Estabelecimento(
        Guid idOrganizacao,
        Guid idEmpresa,
        string codigo,
        string nome,
        DateTimeOffset criadoEm)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Estabelecimento precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        if (idEmpresa == Guid.Empty)
        {
            throw new ArgumentException("Estabelecimento precisa pertencer a uma empresa.", nameof(idEmpresa));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdEmpresa = idEmpresa;
        Codigo = Empresa.ValidarTexto(codigo, TamanhoMaximoCodigo, "Codigo", nameof(codigo));
        Nome = Empresa.ValidarTexto(nome, TamanhoMaximoNome, "Nome", nameof(nome));
        Ativo = true;
        CriadoEm = criadoEm;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdEmpresa { get; private set; }
    public string Codigo { get; private set; } = string.Empty;
    public string Nome { get; private set; } = string.Empty;
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    public void Atualizar(string codigo, string nome)
    {
        Codigo = Empresa.ValidarTexto(codigo, TamanhoMaximoCodigo, "Codigo", nameof(codigo));
        Nome = Empresa.ValidarTexto(nome, TamanhoMaximoNome, "Nome", nameof(nome));
    }

    public void Inativar() => Ativo = false;

    public void Reativar() => Ativo = true;
}
