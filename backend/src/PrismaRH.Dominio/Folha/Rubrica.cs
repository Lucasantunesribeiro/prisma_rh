using PrismaRH.Dominio.Contratos;

namespace PrismaRH.Dominio.Folha;

/// <summary>
/// Um evento de folha do catalogo da organizacao: salario, comissao,
/// vale-transporte, adiantamento.
///
/// E catalogo, e nao texto livre no lancamento, pelo mesmo motivo de Cargo:
/// "Vale transporte", "vale-transporte" e "VT" digitados a mao viram tres
/// rubricas diferentes, e nenhum relatorio por rubrica volta a fechar.
/// </summary>
public sealed class Rubrica
{
    public const int TamanhoMaximoCodigo = 20;
    public const int TamanhoMaximoNome = 120;

    private Rubrica()
    {
    }

    public Rubrica(
        Guid idOrganizacao,
        string codigo,
        string nome,
        TipoRubrica tipo,
        EstrategiaRubrica estrategia,
        DateTimeOffset criadoEm)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Rubrica precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        if (estrategia == EstrategiaRubrica.SalarioBaseProporcional && tipo != TipoRubrica.Provento)
        {
            // Salario que desconta nao existe. Deixar passar produziria uma
            // folha com liquido negativo para todo mundo, e o erro so
            // apareceria depois de calcular a empresa inteira.
            throw new ArgumentException(
                "A rubrica de salario-base precisa ser um provento.", nameof(tipo));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        Codigo = Cargo.ValidarTexto(codigo, TamanhoMaximoCodigo, "Codigo da rubrica", nameof(codigo)).ToUpperInvariant();
        Nome = Cargo.ValidarTexto(nome, TamanhoMaximoNome, "Nome da rubrica", nameof(nome));
        Tipo = tipo;
        Estrategia = estrategia;
        Ativa = true;
        CriadoEm = criadoEm;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public string Codigo { get; private set; } = string.Empty;
    public string Nome { get; private set; } = string.Empty;
    public TipoRubrica Tipo { get; private set; }
    public EstrategiaRubrica Estrategia { get; private set; }
    public bool Ativa { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    /// <summary>
    /// Renomear e permitido; trocar tipo ou estrategia nao.
    ///
    /// Um desconto que vira provento mudaria o significado dos lancamentos ja
    /// gravados nas folhas fechadas - inclusive as que ninguem vai recalcular.
    /// Para mudar a natureza, inative esta e crie outra.
    /// </summary>
    public void Renomear(string nome) =>
        Nome = Cargo.ValidarTexto(nome, TamanhoMaximoNome, "Nome da rubrica", nameof(nome));

    public void Inativar() => Ativa = false;

    public void Reativar() => Ativa = true;
}
