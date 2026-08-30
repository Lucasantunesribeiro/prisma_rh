using System.Security.Cryptography;

namespace PrismaRH.Dominio.Importacao;

/// <summary>Formato do arquivo recebido. Enum fechado, nunca texto livre.</summary>
public enum FormatoImportacao
{
    Csv = 1,
    Xlsx = 2,
}

/// <summary>
/// O que aconteceu com a importacao.
///
/// Tres estados, e o caminho entre eles e de mao unica. Nao ha "Analisada" de
/// volta depois de aplicada: uma importacao aplicada e um fato historico, e o
/// `CLAUDE.md secao 4.3` proibe reescrever o passado em silencio.
/// </summary>
public enum StatusImportacao
{
    /// <summary>Registrada e conferida. Nada foi gravado no cadastro ainda.</summary>
    Analisada = 1,

    /// <summary>Os registros foram criados.</summary>
    Aplicada = 2,

    /// <summary>Tinha erro. NADA foi gravado - e o registro existe para provar isso.</summary>
    Recusada = 3,
}

/// <summary>
/// Uma importacao de arquivo, do ponto de vista da rastreabilidade.
///
/// ## O que ela NAO guarda, e por que
///
/// **O arquivo.** Decisao aprovada pelo responsavel em 29/08/2026: guardar o
/// binario exige armazenamento isolado por organizacao, politica de retencao e
/// download autorizado - que e infraestrutura da Fase 9 e do S3, e o
/// `ROADMAP.md` proibe antecipar. O que substitui o binario e o
/// <see cref="HashSha256"/>: ele responde "foi ESTE arquivo?" com certeza
/// pratica, sem guardar uma linha do conteudo.
///
/// Quem tem o arquivo original confere o hash e sabe. Quem nao tem, nao
/// consegue reconstruir nada a partir dele - que e exatamente a propriedade
/// desejada quando o conteudo traz CPF e salario.
///
/// **Dado pessoal.** Nem nome, nem CPF, nem salario entram aqui nem em
/// <see cref="LinhaImportacao"/>. O relatorio se faz com NUMERO DE LINHA, e
/// quem corrige tem o arquivo aberto do lado. Copiar CPF para uma segunda
/// tabela so para deixar o relatorio mais bonito criaria um banco paralelo de
/// dado sensivel, com retencao diferente da do cadastro - exatamente o que o
/// `CLAUDE.md secao 24.16` diz sobre log, e que vale igual aqui.
/// </summary>
public sealed class Importacao
{
    public const int TamanhoMaximoNomeArquivo = 260;

    /// <summary>SHA-256 em hexadecimal minusculo: 64 caracteres, sempre.</summary>
    public const int TamanhoHash = 64;

    private readonly List<LinhaImportacao> _linhas = [];

    private Importacao()
    {
    }

    public Importacao(
        Guid idOrganizacao,
        Guid idUsuario,
        string nomeOriginalArquivo,
        FormatoImportacao formato,
        long tamanhoBytes,
        string hashSha256,
        DateTimeOffset enviadaEm)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException(
                "Importacao precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        if (idUsuario == Guid.Empty)
        {
            // Sem autor nao ha rastreabilidade, e rastreabilidade e a unica
            // razao desta entidade existir.
            throw new ArgumentException(
                "Importacao precisa ter o usuario que a enviou.", nameof(idUsuario));
        }

        if (!Enum.IsDefined(formato))
        {
            throw new ArgumentException("Formato de importacao desconhecido.", nameof(formato));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tamanhoBytes);

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdUsuario = idUsuario;
        NomeOriginalArquivo = ValidarNomeArquivo(nomeOriginalArquivo);
        Formato = formato;
        TamanhoBytes = tamanhoBytes;
        HashSha256 = ValidarHash(hashSha256);
        EnviadaEm = enviadaEm;
        Status = StatusImportacao.Analisada;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdUsuario { get; private set; }

    /// <summary>
    /// O nome que o arquivo tinha na maquina de quem enviou.
    ///
    /// **Guardado para leitura humana, e NUNCA usado como caminho.** O
    /// `CLAUDE.md secao 24.8` e explicito: nome de arquivo enviado pelo usuario
    /// nao vira caminho. Aqui ele e so um rotulo do relatorio - e como o
    /// binario nao e salvo, nao existe caminho nenhum para ele virar.
    /// </summary>
    public string NomeOriginalArquivo { get; private set; } = string.Empty;

    public FormatoImportacao Formato { get; private set; }
    public long TamanhoBytes { get; private set; }

    /// <summary>
    /// A impressao digital do arquivo, em hexadecimal minusculo.
    ///
    /// Substitui o binario para a pergunta que importa: "a importacao 42 veio
    /// deste arquivo aqui?". Quem tem o original calcula e compara.
    /// </summary>
    public string HashSha256 { get; private set; } = string.Empty;

    public DateTimeOffset EnviadaEm { get; private set; }
    public StatusImportacao Status { get; private set; }

    public int TotalLinhas { get; private set; }
    public int LinhasValidas { get; private set; }
    public int LinhasComErro { get; private set; }

    public IReadOnlyList<LinhaImportacao> Linhas => _linhas;

    /// <summary>
    /// Acrescenta uma linha ao relatorio e atualiza os contadores.
    ///
    /// Os contadores sao mantidos aqui, e nao calculados por consulta, porque
    /// a listagem de importacoes precisa deles sem carregar as linhas - e
    /// dez mil linhas por importacao tornariam a listagem impraticavel.
    /// </summary>
    public LinhaImportacao Registrar(int numeroNoArquivo, IReadOnlyList<string> erros)
    {
        ArgumentNullException.ThrowIfNull(erros);
        GarantirEmAnalise();

        // O MESMO numero de linha pode chegar duas vezes: dois problemas de
        // cabecalho sao ambos da linha 1. Criar uma linha nova a cada chamada
        // violava o indice unico do banco e transformava "sua planilha tem dois
        // erros" em 409 de conflito - defeito corrigido em 30/08/2026, ver
        // LinhaImportacao.Acrescentar.
        var existente = _linhas.SingleOrDefault(l => l.NumeroNoArquivo == numeroNoArquivo);

        if (existente is not null)
        {
            var eraValida = existente.Situacao == SituacaoLinha.Valida;

            existente.Acrescentar(erros);

            if (eraValida && existente.Situacao == SituacaoLinha.ComErro)
            {
                LinhasValidas--;
                LinhasComErro++;
            }

            return existente;
        }

        var linha = new LinhaImportacao(IdOrganizacao, Id, numeroNoArquivo, erros);

        _linhas.Add(linha);

        TotalLinhas++;

        if (linha.Situacao == SituacaoLinha.Valida)
        {
            LinhasValidas++;
        }
        else
        {
            LinhasComErro++;
        }

        return linha;
    }

    /// <summary>
    /// Marca a importacao como aplicada.
    ///
    /// **Recusa se houver uma linha com erro.** E a invariante que sustenta a
    /// regra do `ROADMAP.md` - "importacao invalida nao pode deixar dados
    /// parcialmente gravados" - no lugar onde ela nao pode ser esquecida. A
    /// transacao do banco e a segunda camada; esta e a primeira, e ela vale
    /// mesmo para quem chamar o dominio sem transacao.
    /// </summary>
    public void Aplicar()
    {
        GarantirEmAnalise();

        if (LinhasComErro > 0)
        {
            throw new InvalidOperationException(
                $"Importacao tem {LinhasComErro} linha(s) com erro e nao pode ser aplicada.");
        }

        if (TotalLinhas == 0)
        {
            throw new InvalidOperationException(
                "Importacao sem linha alguma nao pode ser aplicada.");
        }

        Status = StatusImportacao.Aplicada;
    }

    /// <summary>
    /// Marca a importacao como recusada. Nada foi gravado.
    ///
    /// O registro da recusa e proposital: uma tentativa que falhou tambem e
    /// rastreabilidade. Apagar o vestigio deixaria a pergunta "por que o
    /// cadastro nao mudou?" sem resposta.
    /// </summary>
    public void Recusar()
    {
        GarantirEmAnalise();

        Status = StatusImportacao.Recusada;
    }

    private void GarantirEmAnalise()
    {
        if (Status != StatusImportacao.Analisada)
        {
            throw new InvalidOperationException(
                $"Importacao ja esta {Status} e nao pode mais ser alterada.");
        }
    }

    /// <summary>
    /// Calcula o SHA-256 do conteudo, no formato em que ele e guardado.
    ///
    /// Fica aqui, e nao na infraestrutura, porque o formato do hash e parte da
    /// invariante da entidade: duas formas diferentes de escrever o mesmo hash
    /// fariam a comparacao falhar sem nada parecer errado.
    /// </summary>
    public static string CalcularHash(ReadOnlySpan<byte> conteudo) =>
        Convert.ToHexStringLower(SHA256.HashData(conteudo));

    private static string ValidarNomeArquivo(string? nome)
    {
        var limpo = nome?.Trim();

        if (string.IsNullOrEmpty(limpo))
        {
            throw new ArgumentException("Nome do arquivo e obrigatorio.", nameof(nome));
        }

        if (limpo.Length > TamanhoMaximoNomeArquivo)
        {
            throw new ArgumentException(
                $"Nome do arquivo passa de {TamanhoMaximoNomeArquivo} caracteres.", nameof(nome));
        }

        return limpo;
    }

    private static string ValidarHash(string? hash)
    {
        var limpo = hash?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(limpo)
            || limpo.Length != TamanhoHash
            || !limpo.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new ArgumentException(
                "Hash precisa ser um SHA-256 em hexadecimal de 64 caracteres.", nameof(hash));
        }

        return limpo;
    }
}
