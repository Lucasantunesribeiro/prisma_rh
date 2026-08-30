namespace PrismaRH.Dominio.Importacao;

/// <summary>
/// Os tetos de uma importacao.
///
/// Existem antes do parser de proposito: o `CLAUDE.md secao 13` e o Security
/// Gate da Fase 5 exigem limite de tamanho e de quantidade, e limite que so e
/// conferido DEPOIS de ler o arquivo inteiro nao protege de nada - o dano de um
/// arquivo de 2 GB acontece na leitura, nao na conferencia.
///
/// Por isso todo limite daqui e verificado ENQUANTO se le, e a leitura para no
/// instante em que um deles estoura.
///
/// Sao valores conservadores para uso local e sincrono, que e o que o
/// `ROADMAP.md` aprovou para esta fase. Processamento assincrono e a Fase 9, e
/// tetos maiores so fazem sentido junto com ela.
/// </summary>
public sealed record LimitesImportacao(
    int TamanhoMaximoBytes,
    int MaximoRegistros,
    int MaximoColunas,
    int TamanhoMaximoCampo)
{
    /// <summary>5 MB. Uma planilha de RH com 10 mil linhas nao chega perto disso.</summary>
    public const int TamanhoPadraoBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Dez mil registros.
    ///
    /// O numero nao e arbitrario: com processamento SINCRONO, cada registro
    /// vira validacao e depois INSERT dentro de uma transacao. Dez mil cabe em
    /// segundos; cem mil seguraria a requisicao e a conexao do banco por tempo
    /// demais, e a resposta certa para esse volume e a Fase 9, nao um teto maior.
    /// </summary>
    public const int RegistrosPadrao = 10_000;

    /// <summary>
    /// Cinquenta colunas.
    ///
    /// Protege contra a linha unica com milhoes de delimitadores - o jeito mais
    /// barato de fazer um parser alocar memoria sem que o arquivo pareca grande.
    /// </summary>
    public const int ColunasPadrao = 50;

    /// <summary>
    /// Mil caracteres por campo.
    ///
    /// Maior que qualquer campo real do dominio (nome tem 200), e pequeno o
    /// bastante para que um campo unico gigante nao consuma o arquivo inteiro.
    /// </summary>
    public const int CampoPadrao = 1_000;

    public static readonly LimitesImportacao Padrao = new(
        TamanhoPadraoBytes, RegistrosPadrao, ColunasPadrao, CampoPadrao);

    // Um teto zero ou negativo nao e "sem limite": e configuracao errada, e
    // aceita-la em silencio devolveria a aplicacao ao estado sem protecao
    // alguma - exatamente o que esta classe existe para impedir.
    //
    // A validacao mora no ACESSOR init, e nao num inicializador de
    // propriedade. A diferenca nao e de estilo: `with { X = 0 }` NAO reexecuta
    // inicializador de propriedade - ele copia o objeto e aplica o init. Com a
    // validacao no inicializador, `LimitesImportacao.Padrao with { ... = 0 }`
    // produzia um limite invalido em silencio, e um teste provou isso.
    private readonly int _tamanhoMaximoBytes =
        Positivo(TamanhoMaximoBytes, nameof(TamanhoMaximoBytes));

    private readonly int _maximoRegistros =
        Positivo(MaximoRegistros, nameof(MaximoRegistros));

    private readonly int _maximoColunas =
        Positivo(MaximoColunas, nameof(MaximoColunas));

    private readonly int _tamanhoMaximoCampo =
        Positivo(TamanhoMaximoCampo, nameof(TamanhoMaximoCampo));

    public int TamanhoMaximoBytes
    {
        get => _tamanhoMaximoBytes;
        init => _tamanhoMaximoBytes = Positivo(value, nameof(TamanhoMaximoBytes));
    }

    public int MaximoRegistros
    {
        get => _maximoRegistros;
        init => _maximoRegistros = Positivo(value, nameof(MaximoRegistros));
    }

    public int MaximoColunas
    {
        get => _maximoColunas;
        init => _maximoColunas = Positivo(value, nameof(MaximoColunas));
    }

    public int TamanhoMaximoCampo
    {
        get => _tamanhoMaximoCampo;
        init => _tamanhoMaximoCampo = Positivo(value, nameof(TamanhoMaximoCampo));
    }

    private static int Positivo(int valor, string nome) => valor > 0
        ? valor
        : throw new ArgumentOutOfRangeException(nome, valor, "Limite precisa ser maior que zero.");
}
