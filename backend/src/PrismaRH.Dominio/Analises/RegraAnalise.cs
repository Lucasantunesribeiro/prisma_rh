namespace PrismaRH.Dominio.Analises;

/// <summary>
/// Um parametro configurado, guardado como texto.
///
/// **Texto, e nao `numeric`.** Os tipos aceitos sao decimal, percentual e
/// inteiro, e uma coluna por tipo seria tres colunas com duas sempre nulas. O
/// valor e convertido e validado contra a
/// <see cref="DefinicaoParametro"/> da regra **antes** de chegar aqui, e de
/// novo ao ser lido - o texto no banco nunca vira comportamento sem passar pela
/// faixa declarada.
/// </summary>
public sealed class ParametroRegraAnalise
{
    private ParametroRegraAnalise()
    {
    }

    internal ParametroRegraAnalise(Guid idOrganizacao, Guid idRegra, string chave, string valor)
    {
        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdRegraAnalise = idRegra;
        Chave = chave;
        Valor = valor;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdRegraAnalise { get; private set; }
    public string Chave { get; private set; } = string.Empty;
    public string Valor { get; private set; } = string.Empty;

    internal void Alterar(string valor) => Valor = valor;
}

/// <summary>
/// A configuracao de uma regra oficial, **por organizacao**.
///
/// ## O que esta linha e, e o que ela nao e
///
/// Ela **nao** guarda a regra. A regra e codigo, vive no
/// <see cref="CatalogoRegras"/> e nao muda por organizacao. O que esta linha
/// guarda e a decisao da organizacao sobre ela: roda ou nao, com qual
/// severidade, com quais numeros.
///
/// Essa separacao e o que faz o `ROADMAP.md` da Fase 6 poder prometer
/// "parametrizacao por empresa" sem prometer "usuario escreve regra".
///
/// ## Ausencia e configuracao valida
///
/// Regra sem linha aqui roda **ativa, no padrao**. A alternativa - so rodar o
/// que foi configurado - faria uma organizacao nova nao ter conferencia
/// nenhuma, que e o oposto de `secure by default` (`CLAUDE.md secao 24.2`).
///
/// ## Quem alterou
///
/// <see cref="AlteradoPor"/> e <see cref="AlteradoEm"/> guardam a ULTIMA
/// alteracao, e nao o historico. O `CLAUDE.md secao 24.17` manda auditar
/// alteracao de parametro de regra, e a trilha completa - com valor anterior e
/// valor novo - e entrega da **Fase 7**, junto com as demais. Ver a pendencia
/// registrada no bloco da Fase 6 do `ROADMAP.md`.
/// </summary>
public sealed class RegraAnalise
{
    private readonly List<ParametroRegraAnalise> _parametros = [];

    private RegraAnalise()
    {
    }

    public RegraAnalise(Guid idOrganizacao, CodigoRegra codigo, DateTimeOffset agora)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Organizacao e obrigatoria.", nameof(idOrganizacao));
        }

        if (!CatalogoRegras.Conhece(codigo))
        {
            // Codigo fora do catalogo nao vira linha. A recusa acontece aqui,
            // e nao so no endpoint: quem chamar o dominio direto tambem esbarra.
            throw new ArgumentException($"Regra desconhecida: {codigo}.", nameof(codigo));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        Codigo = codigo;
        Ativa = true;
        Severidade = CatalogoRegras.De(codigo)!.SeveridadePadrao;
        AlteradoEm = agora;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public CodigoRegra Codigo { get; private set; }
    public bool Ativa { get; private set; }
    public Severidade Severidade { get; private set; }

    public DateTimeOffset AlteradoEm { get; private set; }
    public Guid? AlteradoPor { get; private set; }

    public IReadOnlyList<ParametroRegraAnalise> Parametros =>
        [.. _parametros.OrderBy(p => p.Chave, StringComparer.Ordinal)];

    /// <summary>
    /// Aplica uma configuracao ja validada.
    ///
    /// Recebe <see cref="ValoresParametros"/>, e nao um dicionario cru: o tipo
    /// so existe depois de passar pela faixa declarada pela regra, entao nao ha
    /// caminho para gravar valor fora da faixa - nem por engano de quem chama.
    /// </summary>
    public void Configurar(
        bool ativa, Severidade severidade, ValoresParametros valores, Guid autor, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(valores);

        Ativa = ativa;
        Severidade = severidade;
        AlteradoPor = autor == Guid.Empty ? null : autor;
        AlteradoEm = agora;

        foreach (var (chave, valor) in valores.Todos)
        {
            var texto = DefinicaoParametro.Formatar(valor);
            var existente = _parametros.SingleOrDefault(
                p => string.Equals(p.Chave, chave, StringComparison.OrdinalIgnoreCase));

            if (existente is null)
            {
                _parametros.Add(new ParametroRegraAnalise(IdOrganizacao, Id, chave, texto));
                continue;
            }

            // Alterar em vez de remover e recriar: remontar a lista faz o EF
            // emitir DELETE+INSERT a cada gravacao. Mesma licao ja registrada
            // em FolhaFuncionario.
            existente.Alterar(texto);
        }

        // Parametro que a regra nao declara mais - versao antiga do sistema -
        // sai. Deixa-lo confundiria quem le a configuracao.
        _parametros.RemoveAll(p => !valores.Todos.ContainsKey(p.Chave));
    }

    /// <summary>Os valores gravados, como o dicionario que a interpretacao espera.</summary>
    public IReadOnlyDictionary<string, string?> ValoresGravados() =>
        _parametros.ToDictionary(p => p.Chave, p => (string?)p.Valor, StringComparer.OrdinalIgnoreCase);
}
