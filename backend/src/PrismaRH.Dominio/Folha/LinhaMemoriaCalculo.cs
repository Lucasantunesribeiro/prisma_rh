namespace PrismaRH.Dominio.Folha;

/// <summary>
/// Um passo da memoria de calculo de um lancamento, ja persistido.
///
/// O ROADMAP chama o conceito de "MemoriaCalculo". Aqui ele e uma COLECAO de
/// linhas em vez de um campo unico, porque um valor de folha quase nunca sai
/// de uma conta so: salario da vigencia, avos do mes, multiplicacao. Guardar
/// so o texto final impediria justamente o que o CLAUDE.md secao 4.2 pede -
/// descobrir quais bases foram usadas para chegar naquele numero.
///
/// A linha e imutavel depois de criada. Ninguem corrige memoria de calculo:
/// recalcula-se a folha, e a memoria nova substitui a antiga inteira.
/// </summary>
public sealed class LinhaMemoriaCalculo
{
    public const int TamanhoMaximoDescricao = 200;
    public const int TamanhoMaximoExpressao = 200;

    private LinhaMemoriaCalculo()
    {
    }

    internal LinhaMemoriaCalculo(
        Guid idOrganizacao,
        Guid idLancamento,
        int ordem,
        string descricao,
        string expressao,
        decimal valor)
    {
        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdLancamento = idLancamento;
        Ordem = ordem;
        Descricao = Encurtar(descricao, TamanhoMaximoDescricao);
        Expressao = Encurtar(expressao, TamanhoMaximoExpressao);
        Valor = valor;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdLancamento { get; private set; }

    /// <summary>Posicao do passo. A memoria so faz sentido lida na ordem em que foi produzida.</summary>
    public int Ordem { get; private set; }

    /// <summary>O que este passo fez, em portugues: "Vigencia de 01/08 a 14/08".</summary>
    public string Descricao { get; private set; } = string.Empty;

    /// <summary>A conta em si: "3.500,00 x 14/30".</summary>
    public string Expressao { get; private set; } = string.Empty;

    public decimal Valor { get; private set; }

    /// <summary>
    /// Corta em vez de recusar. Uma explicacao longa demais nao pode derrubar
    /// o fechamento de uma folha inteira - o valor calculado continua correto,
    /// e o texto e informativo.
    /// </summary>
    private static string Encurtar(string valor, int maximo)
    {
        var limpo = (valor ?? string.Empty).Trim();

        return limpo.Length <= maximo ? limpo : limpo[..maximo];
    }
}
