using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Workflow;

namespace PrismaRH.Dominio.Analises;

/// <summary>
/// Um achado gravado.
///
/// ## O que ele congela, e por quê
///
/// <see cref="CodigoRegra"/>, <see cref="VersaoRegra"/> e
/// <see cref="Severidade"/> sao **copiados** no momento da execucao, e nunca
/// lidos de volta da configuracao atual.
///
/// E o mesmo mecanismo de `LancamentoFolha` e pela mesma razao
/// (`CLAUDE.md secao 4.3`): quando a regra mudar de versao, ou a organizacao
/// baixar a severidade, o resultado de agosto precisa continuar dizendo o que
/// ele dizia em agosto. Sem congelar, baixar a severidade hoje reescreveria o
/// passado - e o `ROADMAP.md` pede exatamente **regra e versao** em cada
/// resultado.
/// </summary>
public sealed class ResultadoAnalise
{
    public const int TamanhoMaximoDescricao = 500;
    public const int TamanhoMaximoContexto = 200;

    /// <summary>
    /// Teto da justificativa.
    ///
    /// Generoso de proposito - explicar por que um salario divergiu as vezes
    /// exige contexto -, mas finito: campo de texto sem teto e vetor de abuso e
    /// de linha ilegivel na tela.
    /// </summary>
    public const int TamanhoMaximoJustificativa = 2_000;

    private readonly List<AndamentoInconsistencia> _andamentos = [];

    private ResultadoAnalise()
    {
    }

    internal ResultadoAnalise(
        Guid idOrganizacao,
        Guid idExecucao,
        Guid idFolha,
        IRegraAnalise regra,
        Severidade severidade,
        Achado achado)
    {
        ArgumentNullException.ThrowIfNull(regra);
        ArgumentNullException.ThrowIfNull(achado);

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdExecucaoAnalise = idExecucao;
        IdFolha = idFolha;

        Codigo = regra.Codigo;
        VersaoRegra = regra.Versao;
        Categoria = regra.Categoria;
        Severidade = severidade;

        IdFolhaFuncionario = achado.IdFolhaFuncionario;
        IdFuncionario = achado.IdFuncionario;
        Matricula = Cortar(achado.Matricula, 30);
        NomeFuncionario = Cortar(achado.NomeFuncionario, 200);

        Descricao = Cortar(achado.Descricao, TamanhoMaximoDescricao) ?? string.Empty;
        ValorEsperado = achado.ValorEsperado;
        ValorEncontrado = achado.ValorEncontrado;
        Diferenca = achado.Diferenca;
        Contexto = Cortar(achado.Contexto, TamanhoMaximoContexto);
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdExecucaoAnalise { get; private set; }
    public Guid IdFolha { get; private set; }

    public CodigoRegra Codigo { get; private set; }
    public int VersaoRegra { get; private set; }
    public CategoriaRegra Categoria { get; private set; }
    public Severidade Severidade { get; private set; }

    public Guid? IdFolhaFuncionario { get; private set; }
    public Guid? IdFuncionario { get; private set; }

    /// <summary>
    /// Matricula e nome sao copiados para o relatorio poder ser lido sem
    /// juntar cinco tabelas. Nao ha CPF nem salario aqui alem do que a
    /// descricao ja explica - o `CLAUDE.md secao 24.13` manda minimizar, e o
    /// relatorio precisa dizer QUEM, nao repetir o cadastro.
    /// </summary>
    public string? Matricula { get; private set; }

    public string? NomeFuncionario { get; private set; }

    public string Descricao { get; private set; } = string.Empty;
    public decimal? ValorEsperado { get; private set; }
    public decimal? ValorEncontrado { get; private set; }
    public decimal? Diferenca { get; private set; }

    /// <summary>Contexto tecnico curto, em `chave=valor`. Nunca dado pessoal.</summary>
    public string? Contexto { get; private set; }

    // ------------------------------------------------------------- workflow

    /// <summary>
    /// Em que ponto do tratamento esta.
    ///
    /// Nasce <see cref="StatusInconsistencia.Detectada"/>: o motor achou, e
    /// ninguem olhou ainda. So muda por <see cref="Transitar"/>, que consulta a
    /// maquina de estados.
    /// </summary>
    public StatusInconsistencia Status { get; private set; } = StatusInconsistencia.Detectada;

    /// <summary>Quem esta cuidando. Nulo enquanto ninguem assumiu.</summary>
    public Guid? IdResponsavel { get; private set; }

    /// <summary>
    /// Por que o valor estava certo, quando estava.
    ///
    /// Exigida para chegar a <see cref="StatusInconsistencia.Justificada"/> -
    /// justificar sem escrever o motivo e so fechar a pendencia com outro nome.
    /// </summary>
    public string? Justificativa { get; private set; }

    /// <summary>Quando foi para Resolvida. Volta a ser nulo numa reabertura.</summary>
    public DateTimeOffset? ConcluidaEm { get; private set; }

    /// <summary>
    /// A linha do tempo, da mais antiga para a mais nova.
    ///
    /// Ordenada pela SEQUENCIA, e nao pelo instante: duas linhas da mesma
    /// requisicao compartilham o instante, e ali o desempate por `Id` seria
    /// aleatorio. Ver `AndamentoInconsistencia.Sequencia`.
    /// </summary>
    public IReadOnlyList<AndamentoInconsistencia> Andamentos =>
        [.. _andamentos.OrderBy(a => a.Sequencia)];

    private int ProximaSequencia() => _andamentos.Count == 0
        ? 1
        : _andamentos.Max(a => a.Sequencia) + 1;

    /// <summary>Ainda pede trabalho de alguem?</summary>
    public bool Pendente => TransicoesInconsistencia.Pendente(Status);

    /// <summary>
    /// Muda o status, validando a transicao.
    ///
    /// Devolve o motivo da recusa, ou nulo quando passou. Recusa em vez de
    /// lancar porque transicao invalida e **entrada de usuario**, e nao defeito
    /// de programacao: a tela pode estar desatualizada, ou duas pessoas podem
    /// ter mexido na mesma inconsistencia.
    /// </summary>
    public string? Transitar(
        StatusInconsistencia destino,
        Guid idAutor,
        string? texto,
        DateTimeOffset agora)
    {
        if (!Enum.IsDefined(destino))
        {
            return "Status desconhecido.";
        }

        if (destino == Status)
        {
            return $"A inconsistencia ja esta em '{Status}'.";
        }

        if (!TransicoesInconsistencia.Permitida(Status, destino))
        {
            var possiveis = TransicoesInconsistencia.A_partir_de(Status);

            return possiveis.Count == 0
                ? $"De '{Status}' nao ha para onde ir."
                : $"De '{Status}' so e possivel ir para {string.Join(" ou ", possiveis)}.";
        }

        // Justificar sem escrever o motivo e so fechar a pendencia com outro
        // nome - e o relatorio de conformidade passaria a mentir.
        if (destino == StatusInconsistencia.Justificada && string.IsNullOrWhiteSpace(texto))
        {
            return "Justificar exige escrever o motivo.";
        }

        var anterior = Status;

        Status = destino;

        if (destino == StatusInconsistencia.Justificada)
        {
            Justificativa = Cortar(texto, TamanhoMaximoJustificativa);
        }

        // Reabrir limpa a data de conclusao, e NAO limpa a justificativa: ela e
        // parte do historico, e apaga-la esconderia o que se concluiu antes.
        ConcluidaEm = destino == StatusInconsistencia.Resolvida ? agora : null;

        _andamentos.Add(AndamentoInconsistencia.Transicao(
            IdOrganizacao, Id, idAutor, anterior, destino, texto, ProximaSequencia(), agora));

        return null;
    }

    /// <summary>
    /// Define o responsavel.
    ///
    /// **Nao valida se o usuario pertence a organizacao** - isso e conferido
    /// por quem chama, que tem o banco. O dominio nao consulta banco
    /// (`CLAUDE.md secao 10`), e uma validacao que finge conferir e pior que
    /// nenhuma. Ver o teste de integracao que prova a recusa.
    /// </summary>
    public void Atribuir(Guid? idResponsavel, Guid idAutor, DateTimeOffset agora)
    {
        var novo = idResponsavel == Guid.Empty ? null : idResponsavel;

        if (novo == IdResponsavel)
        {
            return;
        }

        var anterior = IdResponsavel;

        IdResponsavel = novo;

        _andamentos.Add(AndamentoInconsistencia.Atribuicao(
            IdOrganizacao, Id, idAutor, anterior, novo, ProximaSequencia(), agora));
    }

    public string? Comentar(Guid idAutor, string texto, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return "O comentario nao pode ser vazio.";
        }

        _andamentos.Add(AndamentoInconsistencia.Comentario(
            IdOrganizacao, Id, idAutor, texto, ProximaSequencia(), agora));

        return null;
    }

    /// <summary>
    /// Registra uma evidencia do que foi conferido.
    ///
    /// ⚠️ **Texto, e nao arquivo.** Anexo binario exige armazenamento isolado,
    /// retencao e download autorizado - a mesma infraestrutura que a Fase 5
    /// decidiu nao construir antes da Fase 9. Ver a pendencia registrada no
    /// bloco da Fase 7 do `ROADMAP.md`.
    /// </summary>
    public string? RegistrarEvidencia(Guid idAutor, string texto, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return "A evidencia precisa dizer o que foi conferido.";
        }

        _andamentos.Add(AndamentoInconsistencia.Evidencia(
            IdOrganizacao, Id, idAutor, texto, ProximaSequencia(), agora));

        return null;
    }


    private static string? Cortar(string? texto, int maximo)
    {
        var limpo = texto?.Trim();

        if (string.IsNullOrEmpty(limpo))
        {
            return null;
        }

        return limpo.Length > maximo ? limpo[..maximo] : limpo;
    }
}

/// <summary>
/// Uma passada das regras sobre uma folha.
///
/// ## Por que a execucao e uma entidade, e nao so uma lista de resultados
///
/// Ela responde duas perguntas que os resultados sozinhos nao respondem:
///
/// - **"quando isto foi conferido, e por quem?"** — sem ela, uma folha sem
///   achado nenhum seria indistinguivel de uma folha que ninguem analisou. As
///   duas mostrariam zero inconsistencias, e sao situacoes opostas;
/// - **"isto ainda vale?"** — <see cref="VersaoCalculoDaFolha"/> guarda a
///   versao de calculo que a folha tinha no momento. Se a folha for
///   recalculada, o numero muda e a analise fica visivelmente velha.
///
/// O `ROADMAP.md` pede **historico de execucao**, e e este.
/// </summary>
public sealed class ExecucaoAnalise
{
    private readonly List<ResultadoAnalise> _resultados = [];

    private ExecucaoAnalise()
    {
    }

    public ExecucaoAnalise(
        Guid idOrganizacao,
        Guid idFolha,
        Competencia competencia,
        int versaoCalculoDaFolha,
        Guid idUsuario,
        DateTimeOffset agora)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Organizacao e obrigatoria.", nameof(idOrganizacao));
        }

        if (idFolha == Guid.Empty)
        {
            throw new ArgumentException("Folha e obrigatoria.", nameof(idFolha));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdFolha = idFolha;
        Competencia = competencia;
        VersaoCalculoDaFolha = versaoCalculoDaFolha;
        IdUsuario = idUsuario == Guid.Empty ? null : idUsuario;
        ExecutadaEm = agora;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdFolha { get; private set; }
    public Competencia Competencia { get; private set; }

    /// <summary>A versao de calculo que a folha tinha quando foi analisada.</summary>
    public int VersaoCalculoDaFolha { get; private set; }

    public Guid? IdUsuario { get; private set; }
    public DateTimeOffset ExecutadaEm { get; private set; }

    /// <summary>Quantas regras rodaram - ativas, conhecidas e aplicaveis ao tipo da folha.</summary>
    public int RegrasExecutadas { get; private set; }

    public int TotalResultados { get; private set; }
    public int ResultadosAltos { get; private set; }
    public int ResultadosMedios { get; private set; }
    public int ResultadosBaixos { get; private set; }

    public IReadOnlyList<ResultadoAnalise> Resultados => _resultados;

    /// <summary>Registra que uma regra rodou, tendo ela achado algo ou nao.</summary>
    public void RegistrarExecucaoDe(IRegraAnalise regra)
    {
        ArgumentNullException.ThrowIfNull(regra);

        RegrasExecutadas++;
    }

    /// <summary>Grava um achado, com a severidade configurada pela organizacao.</summary>
    public ResultadoAnalise Registrar(IRegraAnalise regra, Severidade severidade, Achado achado)
    {
        var resultado = new ResultadoAnalise(
            IdOrganizacao, Id, IdFolha, regra, severidade, achado);

        _resultados.Add(resultado);

        TotalResultados++;

        switch (severidade)
        {
            case Severidade.Alta:
                ResultadosAltos++;
                break;
            case Severidade.Media:
                ResultadosMedios++;
                break;
            default:
                ResultadosBaixos++;
                break;
        }

        return resultado;
    }
}
