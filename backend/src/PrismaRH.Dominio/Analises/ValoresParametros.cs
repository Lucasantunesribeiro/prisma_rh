namespace PrismaRH.Dominio.Analises;

/// <summary>
/// Os parametros de uma regra, ja convertidos e validados.
///
/// Uma regra so recebe isto - nunca o dicionario cru vindo do banco ou da
/// requisicao. A diferenca importa: com o dicionario cru, cada regra precisaria
/// converter e validar por conta propria, e a decima regra faria diferente da
/// primeira.
/// </summary>
public sealed class ValoresParametros
{
    private readonly IReadOnlyDictionary<string, decimal> _valores;

    private ValoresParametros(IReadOnlyDictionary<string, decimal> valores) =>
        _valores = valores;

    /// <summary>Todos no padrao. E o que uma regra sem configuracao recebe.</summary>
    public static ValoresParametros Padrao(IReadOnlyList<DefinicaoParametro> definicoes)
    {
        ArgumentNullException.ThrowIfNull(definicoes);

        return new ValoresParametros(
            definicoes.ToDictionary(d => d.Chave, d => d.Padrao, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Converte o que veio de fora contra as definicoes da regra.
    ///
    /// Duas recusas, e as duas importam:
    ///
    /// - **chave que a regra nao declarou** e recusada, e nao ignorada em
    ///   silencio. Ignorar faria a pessoa configurar `toleranciaMaxima`,
    ///   ver a tela salvar, e nunca entender por que nada mudou;
    /// - **valor fora da faixa** e recusado com a faixa na mensagem.
    ///
    /// Chave ausente cai no padrao - configurar so o que se quer mudar e o
    /// comportamento util.
    /// </summary>
    public static (ValoresParametros Valores, IReadOnlyList<string> Erros) Interpretar(
        IReadOnlyList<DefinicaoParametro> definicoes,
        IReadOnlyDictionary<string, string?> recebidos)
    {
        ArgumentNullException.ThrowIfNull(definicoes);
        ArgumentNullException.ThrowIfNull(recebidos);

        var erros = new List<string>();
        var valores = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var chave in recebidos.Keys)
        {
            if (!definicoes.Any(d => string.Equals(d.Chave, chave, StringComparison.OrdinalIgnoreCase)))
            {
                erros.Add($"Esta regra nao tem o parametro '{Curto(chave)}'.");
            }
        }

        foreach (var definicao in definicoes)
        {
            recebidos.TryGetValue(definicao.Chave, out var texto);

            var (valor, erro) = definicao.Interpretar(texto);

            if (erro is not null)
            {
                erros.Add(erro);
            }

            valores[definicao.Chave] = valor;
        }

        return (new ValoresParametros(valores), erros);
    }

    /// <summary>
    /// O valor de um parametro.
    ///
    /// Lanca quando a chave nao existe, e isso e proposital: a chave vem do
    /// codigo da propria regra, entao errar aqui e defeito de programacao, nao
    /// entrada de usuario. Devolver zero em silencio faria a regra rodar com
    /// tolerancia zero e acusar o mundo inteiro.
    /// </summary>
    public decimal Obter(string chave) => _valores.TryGetValue(chave, out var valor)
        ? valor
        : throw new KeyNotFoundException($"Parametro '{chave}' nao declarado pela regra.");

    public IReadOnlyDictionary<string, decimal> Todos => _valores;

    private static string Curto(string texto) =>
        texto.Length > 40 ? texto[..40] : texto;
}
