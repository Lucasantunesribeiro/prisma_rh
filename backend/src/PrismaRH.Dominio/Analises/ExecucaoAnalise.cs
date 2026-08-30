using PrismaRH.Dominio.Folha;

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
