namespace PrismaRH.Dominio.Contratos;

/// <summary>
/// Catalogo de cargos da organizacao. E catalogo, e nao texto livre no
/// contrato, para que "Analista de RH" seja sempre a mesma coisa e para que a
/// mudanca de cargo tenha o que apontar no historico.
/// </summary>
public sealed class Cargo
{
    public const int TamanhoMaximoNome = 150;
    public const int TamanhoMaximoCodigo = 30;

    private Cargo()
    {
    }

    public Cargo(Guid idOrganizacao, string codigo, string nome, DateTimeOffset criadoEm)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Cargo precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        Codigo = ValidarTexto(codigo, TamanhoMaximoCodigo, "Codigo do cargo", nameof(codigo));
        Nome = ValidarTexto(nome, TamanhoMaximoNome, "Nome do cargo", nameof(nome));
        Ativo = true;
        CriadoEm = criadoEm;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public string Codigo { get; private set; } = string.Empty;
    public string Nome { get; private set; } = string.Empty;
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    public void Atualizar(string codigo, string nome)
    {
        Codigo = ValidarTexto(codigo, TamanhoMaximoCodigo, "Codigo do cargo", nameof(codigo));
        Nome = ValidarTexto(nome, TamanhoMaximoNome, "Nome do cargo", nameof(nome));
    }

    public void Inativar() => Ativo = false;

    public void Reativar() => Ativo = true;

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
}
