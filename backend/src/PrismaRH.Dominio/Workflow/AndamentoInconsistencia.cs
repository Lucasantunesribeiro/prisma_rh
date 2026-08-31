namespace PrismaRH.Dominio.Workflow;

/// <summary>O que aconteceu numa linha do historico.</summary>
public enum TipoAndamento
{
    /// <summary>Alguem escreveu um comentario.</summary>
    Comentario = 1,

    /// <summary>O status mudou.</summary>
    Transicao = 2,

    /// <summary>O responsavel mudou.</summary>
    Atribuicao = 3,

    /// <summary>Alguem registrou uma evidencia do que conferiu.</summary>
    Evidencia = 4,
}

/// <summary>
/// Uma linha do historico de uma inconsistencia.
///
/// ## Por que UMA tabela para comentario, transicao, atribuicao e evidencia
///
/// A alternativa seria quatro tabelas, cada uma com sua chave e seu indice - e
/// a tela teria que juntar as quatro em memoria e reordenar por data para
/// mostrar o que a pessoa quer ver, que e **uma linha do tempo**.
///
/// Quatro tabelas produzem quatro consultas, quatro chances de esquecer o
/// filtro global e uma ordenacao montada a mao. Uma tabela com um
/// discriminador produz a linha do tempo direto do banco, ja ordenada.
///
/// Os campos que so valem para um tipo sao anulaveis, e o dominio garante a
/// coerencia nas fabricas: nao ha construtor publico que deixe criar uma
/// transicao sem status ou um comentario sem texto.
///
/// ## Somente-insercao
///
/// Nao ha metodo de alteracao nem de remocao. O `ROADMAP.md` da Fase 7 e
/// explicito - "historico nao e apagado" - e o `CLAUDE.md secao 12` diz a mesma
/// coisa: nao apagar o historico ao alterar o status.
/// </summary>
public sealed class AndamentoInconsistencia
{
    public const int TamanhoMaximoTexto = 2_000;

    private AndamentoInconsistencia()
    {
    }

    private AndamentoInconsistencia(
        Guid idOrganizacao,
        Guid idResultado,
        TipoAndamento tipo,
        Guid idAutor,
        int sequencia,
        DateTimeOffset agora)
    {
        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdResultadoAnalise = idResultado;
        Tipo = tipo;
        IdAutor = idAutor == Guid.Empty ? null : idAutor;
        Sequencia = sequencia;
        OcorridoEm = agora;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdResultadoAnalise { get; private set; }
    public TipoAndamento Tipo { get; private set; }

    /// <summary>
    /// Quem fez. Anulavel porque o usuario pode ser apagado um dia - o
    /// historico continua valendo mesmo sem ele.
    /// </summary>
    public Guid? IdAutor { get; private set; }

    public DateTimeOffset OcorridoEm { get; private set; }

    /// <summary>
    /// A ordem dentro da inconsistencia: 1, 2, 3...
    ///
    /// ⚠️ **Existe por causa de um teste que reprovou.** A ordenacao era por
    /// `OcorridoEm` e, em empate, por `Id`. Parecia suficiente - `Guid` versao 7
    /// carrega o tempo -, mas a precisao dele e de MILISSEGUNDOS: duas linhas
    /// criadas na mesma requisicao caem no mesmo instante, e ali a parte que
    /// desempata e ALEATORIA.
    ///
    /// O efeito seria a linha do tempo aparecer fora de ordem exatamente quando
    /// varias coisas acontecem juntas - que e quando ela mais precisa estar
    /// certa: "quem atribuiu antes de mudar o status?".
    ///
    /// A sequencia e do agregado, e nao do relogio. Ela nao depende de precisao
    /// nenhuma.
    /// </summary>
    public int Sequencia { get; private set; }

    /// <summary>
    /// O texto escrito pela pessoa.
    ///
    /// ⚠️ **Dado de usuario, e o mais delicado do produto.** Justificativa de
    /// divergencia salarial costuma explicar situacao pessoal. Ele e guardado
    /// como TEXTO e exibido como TEXTO - o React escapa por padrao, e
    /// `dangerouslySetInnerHTML` e proibido (`CLAUDE.md secao 24.9`).
    /// </summary>
    public string? Texto { get; private set; }

    public StatusInconsistencia? StatusAnterior { get; private set; }
    public StatusInconsistencia? StatusNovo { get; private set; }

    public Guid? ResponsavelAnterior { get; private set; }
    public Guid? ResponsavelNovo { get; private set; }

    internal static AndamentoInconsistencia Comentario(
        Guid idOrganizacao, Guid idResultado, Guid idAutor, string texto,
        int sequencia, DateTimeOffset agora) =>
        new(idOrganizacao, idResultado, TipoAndamento.Comentario, idAutor, sequencia, agora)
        {
            Texto = Cortar(texto),
        };

    internal static AndamentoInconsistencia Evidencia(
        Guid idOrganizacao, Guid idResultado, Guid idAutor, string texto,
        int sequencia, DateTimeOffset agora) =>
        new(idOrganizacao, idResultado, TipoAndamento.Evidencia, idAutor, sequencia, agora)
        {
            Texto = Cortar(texto),
        };

    internal static AndamentoInconsistencia Transicao(
        Guid idOrganizacao,
        Guid idResultado,
        Guid idAutor,
        StatusInconsistencia de,
        StatusInconsistencia para,
        string? texto,
        int sequencia,
        DateTimeOffset agora) =>
        new(idOrganizacao, idResultado, TipoAndamento.Transicao, idAutor, sequencia, agora)
        {
            StatusAnterior = de,
            StatusNovo = para,
            Texto = string.IsNullOrWhiteSpace(texto) ? null : Cortar(texto),
        };

    internal static AndamentoInconsistencia Atribuicao(
        Guid idOrganizacao,
        Guid idResultado,
        Guid idAutor,
        Guid? de,
        Guid? para,
        int sequencia,
        DateTimeOffset agora) =>
        new(idOrganizacao, idResultado, TipoAndamento.Atribuicao, idAutor, sequencia, agora)
        {
            ResponsavelAnterior = de,
            ResponsavelNovo = para,
        };

    private static string Cortar(string texto)
    {
        var limpo = texto.Trim();

        return limpo.Length > TamanhoMaximoTexto ? limpo[..TamanhoMaximoTexto] : limpo;
    }
}
