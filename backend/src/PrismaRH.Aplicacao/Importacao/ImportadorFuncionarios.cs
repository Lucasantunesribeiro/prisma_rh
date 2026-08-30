using PrismaRH.Dominio.Importacao;
using PrismaRH.Dominio.Pessoas;

namespace PrismaRH.Aplicacao.Importacao;

/// <summary>
/// Uma linha ja interpretada: o que dela virou funcionario, e o que deu errado.
///
/// Carrega o **CPF de verdade**, e nao o mascarado. Este record vive DENTRO do
/// processo: a confirmacao precisa do documento inteiro para criar o cadastro,
/// e mascarar aqui obrigaria a interpretar o CPF duas vezes - uma no preview e
/// outra na gravacao -, que e como duas validacoes acabam divergindo.
///
/// O mascaramento acontece na FRONTEIRA, ao montar a resposta HTTP, que e onde
/// ele protege alguma coisa. Ver `CpfMascarado`.
/// </summary>
public sealed record LinhaFuncionario(
    int NumeroNoArquivo,
    string? Nome,
    Cpf? Cpf,
    DateOnly? DataNascimento,
    IReadOnlyList<string> Erros)
{
    public bool Valida => Erros.Count == 0;

    /// <summary>O CPF como ele pode aparecer numa tela: `111.***.**7-35`.</summary>
    public string? CpfMascarado => Cpf?.Mascarado;
}

/// <summary>
/// O que a leitura de um arquivo de funcionarios produziu.
///
/// Serve ao preview E a confirmacao, e e de proposito: as duas passam pelo
/// MESMO caminho. Se a confirmacao tivesse validacao propria, ela poderia
/// divergir do que a tela mostrou - e a divergencia so apareceria em producao.
/// </summary>
public sealed record ResultadoFuncionarios(
    IReadOnlyList<LinhaFuncionario> Linhas,
    IReadOnlyList<ErroImportacao> ErrosDoArquivo)
{
    public int Total => Linhas.Count;

    public int Validas => Linhas.Count(l => l.Valida);

    public int ComErro => Linhas.Count(l => !l.Valida);

    /// <summary>
    /// So e importavel se NAO houver erro nenhum - nem de arquivo, nem de linha.
    ///
    /// Tudo ou nada, e nao "importa as boas": importar parcialmente deixaria o
    /// cadastro num estado que ninguem pediu, e obrigaria a pessoa a descobrir
    /// quais linhas entraram para montar o arquivo da segunda tentativa.
    /// </summary>
    public bool Importavel => ErrosDoArquivo.Count == 0 && Linhas.Count > 0 && ComErro == 0;
}

/// <summary>
/// Transforma um arquivo de funcionarios em cadastro - ou em relatorio de erro.
///
/// ## As colunas
///
/// `nome`, `cpf`, `data de nascimento`. Nada mais: o `CLAUDE.md secao 7` proibe
/// campo sem uso claro, e esses tres sao os que <see cref="Funcionario"/> exige.
/// A busca do cabecalho ignora maiuscula, acento e espaco - quem monta a
/// planilha escreve "CPF" ou " Cpf ", e recusar por isso seria rigor sem
/// proposito. O CONTEUDO continua validado com todo rigor.
///
/// ## Onde a seguranca mora
///
/// Esta classe **nao conhece organizacao**. Ela recebe os CPFs que ja existem e
/// devolve linhas interpretadas; quem sabe de qual organizacao se trata e o
/// endpoint, que tira isso do usuario autenticado. Um mapeador que aceitasse
/// `IdOrganizacao` seria um lugar a mais onde confiar no cliente por engano.
///
/// Funcao pura: sem banco, sem relogio, sem HTTP (`CLAUDE.md secao 10`).
/// </summary>
public static class ImportadorFuncionarios
{
    public const string ColunaNome = "nome";
    public const string ColunaCpf = "cpf";
    public const string ColunaDataNascimento = "data de nascimento";

    public static readonly string[] ColunasObrigatorias =
        [ColunaNome, ColunaCpf, ColunaDataNascimento];

    /// <summary>
    /// Interpreta o resultado da leitura.
    /// </summary>
    /// <param name="cpfsJaCadastrados">
    /// Os CPFs que a organizacao ja tem. Serve para transformar duplicata em
    /// **erro legivel** em vez de violacao de indice unico: um 500 vindo do
    /// banco nao diz a ninguem qual linha do arquivo repetiu o documento.
    /// </param>
    public static ResultadoFuncionarios Interpretar(
        ResultadoLeitura leitura,
        IReadOnlySet<string> cpfsJaCadastrados,
        DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(leitura);
        ArgumentNullException.ThrowIfNull(cpfsJaCadastrados);

        if (!leitura.Valido)
        {
            return new ResultadoFuncionarios([], leitura.Erros);
        }

        var faltando = ColunasObrigatorias.Where(c => leitura.Coluna(c) is null).ToList();

        if (faltando.Count > 0)
        {
            return new ResultadoFuncionarios([], [
                new ErroImportacao(
                    1, null, "Faltam colunas obrigatorias: " + string.Join(", ", faltando) + "."),
            ]);
        }

        var iNome = leitura.Coluna(ColunaNome)!.Value;
        var iCpf = leitura.Coluna(ColunaCpf)!.Value;
        var iNascimento = leitura.Coluna(ColunaDataNascimento)!.Value;

        var linhas = new List<LinhaFuncionario>(leitura.Linhas.Count);

        // Duplicata DENTRO do arquivo tambem e duplicata. Sem isto, um arquivo
        // com o mesmo CPF duas vezes passaria na validacao e so quebraria no
        // INSERT - com a transacao ja aberta e a metade do trabalho feita.
        var cpfsDoArquivo = new HashSet<string>(StringComparer.Ordinal);

        foreach (var linha in leitura.Linhas)
        {
            var erros = new List<string>();

            var nome = linha.Campos[iNome].Trim();
            var cpfBruto = linha.Campos[iCpf].Trim();
            var nascimentoBruto = linha.Campos[iNascimento].Trim();

            Cpf? cpf = null;

            try
            {
                cpf = Cpf.Criar(cpfBruto);
            }
            catch (ArgumentException)
            {
                // A mensagem NAO repete o CPF invalido. O numero de linha basta
                // para achar a celula, e ecoar documento em mensagem de erro o
                // levaria para log e para tela sem necessidade.
                erros.Add("CPF invalido.");
            }

            if (cpf is { } valido)
            {
                if (cpfsJaCadastrados.Contains(valido.Valor))
                {
                    erros.Add("Ja existe um funcionario com este CPF nesta organizacao.");
                }
                else if (!cpfsDoArquivo.Add(valido.Valor))
                {
                    erros.Add("Este CPF aparece mais de uma vez no arquivo.");
                }
            }

            if (string.IsNullOrWhiteSpace(nome))
            {
                erros.Add("Nome e obrigatorio.");
            }
            else if (nome.Length > Funcionario.TamanhoMaximoNome)
            {
                erros.Add($"Nome passa de {Funcionario.TamanhoMaximoNome} caracteres.");
            }

            DateOnly? nascimento = null;

            if (InterpretarData(nascimentoBruto) is { } data)
            {
                if (data >= hoje)
                {
                    erros.Add("Data de nascimento precisa ser no passado.");
                }
                else
                {
                    nascimento = data;
                }
            }
            else
            {
                erros.Add("Data de nascimento invalida. Use dd/mm/aaaa ou aaaa-mm-dd.");
            }

            linhas.Add(new LinhaFuncionario(
                linha.Numero,
                string.IsNullOrWhiteSpace(nome) ? null : nome,
                cpf,
                nascimento,
                erros));
        }

        return new ResultadoFuncionarios(linhas, []);
    }

    /// <summary>
    /// Duas formas, e so duas: `dd/mm/aaaa` e `aaaa-mm-dd`.
    ///
    /// Vocabulario fechado de proposito. Aceitar o que a cultura da maquina
    /// entender faria `03/04/2026` virar marco num servidor e abril noutro -
    /// e ninguem perceberia, porque as duas datas existem.
    /// </summary>
    public static DateOnly? InterpretarData(string? valor)
    {
        var limpo = valor?.Trim();

        if (string.IsNullOrEmpty(limpo))
        {
            return null;
        }

        string[] formatos = ["dd/MM/yyyy", "yyyy-MM-dd"];

        return DateOnly.TryParseExact(
            limpo, formatos, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var data)
            ? data
            : null;
    }
}
