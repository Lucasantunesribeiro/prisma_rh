namespace PrismaRH.Dominio.Importacao;

/// <summary>Se a linha passou na validacao ou nao.</summary>
public enum SituacaoLinha
{
    Valida = 1,
    ComErro = 2,
}

/// <summary>
/// Uma linha do arquivo, do ponto de vista do relatorio.
///
/// ## O que ela deliberadamente NAO guarda
///
/// **Os valores da linha.** Nem o texto bruto, nem os campos separados. Nao e
/// esquecimento: e a regra do `CLAUDE.md secao 24.13` sobre minimizacao, e a
/// instrucao explicita do responsavel de nao duplicar CPF, salario ou nome sem
/// necessidade.
///
/// A necessidade nao existe. Quem corrige um arquivo tem o arquivo aberto do
/// lado, e a chave que liga o relatorio a ele e o <see cref="NumeroNoArquivo"/> -
/// o mesmo numero que o editor de planilha mostra na lateral. "Linha 7: CPF
/// invalido" resolve. "Linha 7 (Ana Silva, 111.222.333-44): CPF invalido"
/// resolveria igual, e ainda criaria uma **segunda copia de dado pessoal**,
/// numa tabela com retencao propria e finalidade diferente da do cadastro.
///
/// O **preview** - que mostra os valores antes de gravar - acontece em memoria,
/// na resposta da requisicao, e nao passa por aqui. O `ROADMAP.md` manda o
/// preview vir antes da persistencia, e e literalmente o que acontece: o que
/// aparece na tela para conferencia nunca chega ao banco.
///
/// ## Por que a linha VALIDA tambem e gravada
///
/// Ela e a ancora da origem. O `IdLinhaImportacao` do funcionario aponta para
/// aqui, e e assim que se responde "de onde veio este cadastro?". Gravar so as
/// linhas com erro deixaria os registros criados sem origem - que era metade do
/// pedido.
/// </summary>
public sealed class LinhaImportacao
{
    /// <summary>
    /// Quantos erros de uma mesma linha sao guardados.
    ///
    /// Uma linha com trinta colunas erradas produz trinta mensagens, e quem le
    /// nao precisa das trinta para entender que a linha esta ruim. O teto
    /// impede que um arquivo desenhado para isso multiplique dez mil linhas por
    /// cinquenta erros e encha a tabela.
    /// </summary>
    public const int MaximoErrosPorLinha = 10;

    public const int TamanhoMaximoErro = 300;

    private readonly List<string> _erros = [];

    private LinhaImportacao()
    {
    }

    internal LinhaImportacao(
        Guid idOrganizacao, Guid idImportacao, int numeroNoArquivo, IReadOnlyList<string> erros)
    {
        ArgumentNullException.ThrowIfNull(erros);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numeroNoArquivo);

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdImportacao = idImportacao;
        NumeroNoArquivo = numeroNoArquivo;

        Acrescentar(erros);
    }

    /// <summary>
    /// Acrescenta erros a uma linha que ja existe.
    ///
    /// ⚠️ **Existe por causa de um defeito real, corrigido em 30/08/2026.**
    ///
    /// Ate a etapa 4, `Importacao.Registrar` criava uma linha NOVA a cada
    /// chamada. Quando o mesmo numero de linha aparecia duas vezes - dois erros
    /// de cabecalho, por exemplo, que sao ambos da linha 1 - nasciam duas
    /// LinhaImportacao com o mesmo `NumeroNoArquivo`, e o indice unico do banco
    /// recusava a gravacao.
    ///
    /// O efeito visivel era o pior possivel: um arquivo com DOIS problemas de
    /// cabecalho devolvia **409**, com a mensagem de conflito de importacao
    /// simultanea, em vez de `Recusada` com os dois erros explicados. A pessoa
    /// via "alguem importou ao mesmo tempo" quando o problema era a planilha
    /// dela.
    /// </summary>
    internal void Acrescentar(IReadOnlyList<string> erros)
    {
        ArgumentNullException.ThrowIfNull(erros);

        foreach (var erro in erros)
        {
            if (_erros.Count >= MaximoErrosPorLinha)
            {
                break;
            }

            var limpo = (erro ?? string.Empty).Trim();

            if (limpo.Length == 0)
            {
                continue;
            }

            _erros.Add(limpo.Length > TamanhoMaximoErro
                ? limpo[..TamanhoMaximoErro]
                : limpo);
        }

        // A situacao e DERIVADA dos erros, e nao um parametro. Um chamador que
        // pudesse dizer "valida" com erros na lista criaria uma linha que se
        // contradiz - e ela passaria pela invariante de Importacao.Aplicar.
        Situacao = _erros.Count == 0 ? SituacaoLinha.Valida : SituacaoLinha.ComErro;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdImportacao { get; private set; }

    /// <summary>
    /// O numero da linha NO ARQUIVO, contando o cabecalho como linha 1.
    ///
    /// E o numero que o editor de planilha mostra na lateral. Devolver o indice
    /// do registro obrigaria quem corrige a fazer a conta de cabeca.
    /// </summary>
    public int NumeroNoArquivo { get; private set; }

    public SituacaoLinha Situacao { get; private set; }

    public IReadOnlyList<string> Erros => _erros;
}
