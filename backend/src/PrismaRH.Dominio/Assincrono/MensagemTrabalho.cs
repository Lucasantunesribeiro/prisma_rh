using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrismaRH.Dominio.Assincrono;

/// <summary>Por que a mensagem foi recusada. Vocabulario fechado.</summary>
public enum RecusaMensagem
{
    Aceita = 0,
    NaoEJson = 1,
    VersaoDesconhecida = 2,
    SemTrabalho = 3,
    SemOrganizacao = 4,
    TipoDesconhecido = 5,
    GrandeDemais = 6,

    /// <summary>
    /// ⚠️ A grave: o tenant da mensagem nao bate com o do trabalho gravado.
    /// Ou houve adulteracao, ou um defeito montou a mensagem errada. Nos dois
    /// casos, processar seria vazamento entre organizacoes.
    /// </summary>
    TenantDivergente = 7,
}

/// <summary>
/// O que trafega na fila.
///
/// ## Identificadores, e nada mais
///
/// Nao ha planilha aqui, nao ha CPF, nao ha salario. A mensagem carrega o
/// **id do trabalho, a organizacao e o tipo** - e o worker busca o resto no
/// banco.
///
/// Nao e economia de bytes. Uma fila tem retencao propria, controle de acesso
/// proprio e uma DLQ onde a mensagem pode ficar quatorze dias; pôr dado pessoal
/// ali cria uma **segunda copia** do dado mais sensivel do produto, num lugar
/// com regras diferentes das do banco (`CLAUDE.md secao 24.13`). O teto de 8 KB
/// do <c>OrcamentoSemCusto</c> existe para tornar a tentacao impossivel.
///
/// ## A versao existe desde o primeiro dia
///
/// Fila tem mensagem em voo. No dia em que o formato mudar, havera mensagem
/// antiga na fila e worker novo lendo - e sem o campo de versao o worker novo
/// interpretaria o formato velho em silencio. Um numero agora custa um `int`;
/// depois custa um incidente.
/// </summary>
public sealed record MensagemTrabalho(
    [property: JsonPropertyName("versao")] int Versao,
    [property: JsonPropertyName("idTrabalho")] Guid IdTrabalho,
    [property: JsonPropertyName("idOrganizacao")] Guid IdOrganizacao,
    [property: JsonPropertyName("tipo")] TipoTrabalho Tipo)
{
    public const int VersaoAtual = 1;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        MaxDepth = 8,
        Converters = { new JsonStringEnumConverter() },
    };

    public static MensagemTrabalho De(TrabalhoAssincrono trabalho)
    {
        ArgumentNullException.ThrowIfNull(trabalho);

        return new MensagemTrabalho(VersaoAtual, trabalho.Id, trabalho.IdOrganizacao, trabalho.Tipo);
    }

    public string Serializar() => JsonSerializer.Serialize(this, Opcoes);

    /// <summary>
    /// Le e valida por esquema.
    ///
    /// Toda recusa acontece **antes** de qualquer consulta ao banco: mensagem
    /// malformada nao deve nem chegar a custar um `SELECT`.
    /// </summary>
    public static RecusaMensagem Ler(string? corpo, int tamanhoMaximoBytes, out MensagemTrabalho? mensagem)
    {
        mensagem = null;

        if (string.IsNullOrWhiteSpace(corpo))
        {
            return RecusaMensagem.NaoEJson;
        }

        // Conferido em BYTES, e nao em caracteres: um corpo cheio de acentos
        // ocupa mais que o `Length` sugere, e o teto da fila e em bytes.
        if (System.Text.Encoding.UTF8.GetByteCount(corpo) > tamanhoMaximoBytes)
        {
            return RecusaMensagem.GrandeDemais;
        }

        MensagemTrabalho? lida;

        try
        {
            lida = JsonSerializer.Deserialize<MensagemTrabalho>(corpo, Opcoes);
        }
        catch (JsonException)
        {
            return RecusaMensagem.NaoEJson;
        }

        if (lida is null)
        {
            return RecusaMensagem.NaoEJson;
        }

        if (lida.Versao != VersaoAtual)
        {
            return RecusaMensagem.VersaoDesconhecida;
        }

        if (lida.IdTrabalho == Guid.Empty)
        {
            return RecusaMensagem.SemTrabalho;
        }

        // Mensagem sem tenant nao e "mensagem do sistema": e mensagem invalida.
        // Aceita-la faria o worker rodar sem dono, que e exatamente o cenario
        // que o Security Gate manda impedir.
        if (lida.IdOrganizacao == Guid.Empty)
        {
            return RecusaMensagem.SemOrganizacao;
        }

        // Enum fora do vocabulario: `(TipoTrabalho)999` desserializa sem erro e
        // so quebraria la na frente, dentro do `switch`.
        if (!Enum.IsDefined(lida.Tipo))
        {
            return RecusaMensagem.TipoDesconhecido;
        }

        mensagem = lida;

        return RecusaMensagem.Aceita;
    }

    /// <summary>
    /// ⚠️ A segunda barreira: a mensagem passou no esquema, mas ela **combina
    /// com o trabalho de verdade**?
    ///
    /// A primeira valida a forma; esta valida o fato. Trocar um `Guid` na
    /// mensagem produz um JSON perfeitamente valido - e e aqui que ele para.
    /// </summary>
    public RecusaMensagem Conferir(TrabalhoAssincrono trabalho)
    {
        ArgumentNullException.ThrowIfNull(trabalho);

        if (trabalho.Id != IdTrabalho)
        {
            return RecusaMensagem.SemTrabalho;
        }

        if (!trabalho.PertenceA(IdOrganizacao))
        {
            return RecusaMensagem.TenantDivergente;
        }

        if (trabalho.Tipo != Tipo)
        {
            return RecusaMensagem.TipoDesconhecido;
        }

        return RecusaMensagem.Aceita;
    }
}
