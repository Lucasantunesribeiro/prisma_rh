namespace PrismaRH.Dominio.Folha;

/// <summary>
/// Quanto vale uma base de calculo dentro de um holerite.
///
/// E resultado gravado, e nao soma refeita a cada leitura, pelo mesmo motivo
/// dos totais em FolhaFuncionario: o CLAUDE.md secao 4.3 exige que uma folha
/// fechada continue dizendo o que ela usou. Se a lei mudar as incidencias no
/// ano que vem, a base de agosto tem que continuar sendo a de agosto.
/// </summary>
public sealed class BaseApurada
{
    private BaseApurada()
    {
    }

    internal BaseApurada(Guid idOrganizacao, Guid idFolhaFuncionario, BaseCalculo baseCalculo)
    {
        if (baseCalculo == BaseCalculo.Nenhuma || !BasesDeCalculo.Conhecidas(baseCalculo))
        {
            throw new ArgumentException(
                "Base apurada precisa corresponder a uma base conhecida.", nameof(baseCalculo));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdFolhaFuncionario = idFolhaFuncionario;
        Base = baseCalculo;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdFolhaFuncionario { get; private set; }

    public BaseCalculo Base { get; private set; }

    public decimal Valor { get; private set; }

    internal void DefinirValor(decimal valor) => Valor = Dinheiro.Arredondar(valor);
}
