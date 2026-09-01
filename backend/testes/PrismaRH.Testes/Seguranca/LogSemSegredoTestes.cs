using System.Text.RegularExpressions;

namespace PrismaRH.Testes.Seguranca;

/// <summary>
/// Log sem segredo (Fase 12).
///
/// ## Por que um teste sobre o código-fonte, e não sobre a execução
///
/// `CLAUDE.md §24.16` lista o que o log **não** registra: senha, access token,
/// refresh token, cookie, secret, CPF completo, folha, holerite, payload
/// sensível. E dá a razão:
///
/// > *"O log costuma ter retenção diferente, acesso mais amplo e menos proteção
/// > que o banco — o que entra nele sai do regime de proteção do dado
/// > original."*
///
/// Provar isso em execução exigiria exercitar todo caminho que loga, com dado
/// sensível de verdade, e conferir a saída. Caro, e ainda assim incompleto: o
/// caminho que ninguém exercitou é justamente o que vaza.
///
/// Ler o **código** responde a pergunta certa — *existe alguma chamada de log
/// que menciona algo sensível?* — e responde para os caminhos todos, inclusive
/// os que nenhum teste percorre.
///
/// ## Isto é uma heurística, e está declarado
///
/// Um `grep` estruturado não é análise semântica: uma variável chamada
/// `resumo` que por acaso contém um CPF passa. O teste é a primeira barreira,
/// não a única — a revisão do `§24.16` continua valendo.
///
/// O que ele **garante** é que ninguém escreve `log.LogInformation("token {T}",
/// token)` sem que a suíte reclame.
/// </summary>
public sealed class LogSemSegredoTestes
{
    /// <summary>
    /// Palavras que não podem aparecer numa chamada de log.
    ///
    /// A lista sai do `CLAUDE.md §24.16` e da classificação do `§24.13`.
    /// </summary>
    private static readonly string[] Proibidas =
    [
        "senha", "password",
        "token", "refresh",
        "cookie",
        "secret", "chaveassinatura", "apikey",
        "cpf",
        "prompt",
        "stringconexao", "connectionstring",
        "corpo", "payload", "body",
        "holerite", "salario",
    ];

    /// <summary>
    /// Falsos positivos conferidos **um a um**, no formato `arquivo::trecho`.
    ///
    /// Mesma convenção do `.varredura-permitido` do repositório, e pela mesma
    /// razão: carimbar a liberação sem olhar anula a ferramenta. Cada linha
    /// aqui foi lida no código antes de entrar.
    /// </summary>
    private static readonly Dictionary<string, string> Liberados = new()
    {
        ["SemeadorDesenvolvimento.cs::Semeadura ignorada: defina"] =
            "O argumento e `VariavelSenha` - o NOME da variavel de ambiente "
            + "(PRISMARH_SEED_SENHA), nunca o valor. O log existe para dizer ao "
            + "desenvolvedor o que falta definir; sem o nome, a mensagem nao "
            + "serve para nada. Conferido em 01/09/2026.",
    };

    /// <summary>
    /// Chamadas de log, com os argumentos. Pega até o `);` que fecha, com teto
    /// de linhas para não sair engolindo o arquivo se alguém escrever torto.
    /// </summary>
    private static readonly Regex Chamada = new(
        @"Log(?:Information|Warning|Error|Debug|Trace|Critical)\s*\((?<corpo>[^;]{0,600}?)\)\s*;",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static string RaizDoRepositorio()
    {
        var diretorio = new DirectoryInfo(AppContext.BaseDirectory);

        while (diretorio is not null && !File.Exists(Path.Combine(diretorio.FullName, "PrismaRH.sln")))
        {
            diretorio = diretorio.Parent;
        }

        Assert.NotNull(diretorio);

        return diretorio!.FullName;
    }

    private static IEnumerable<string> Fontes() =>
        Directory.EnumerateFiles(
            Path.Combine(RaizDoRepositorio(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(a => !a.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !a.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// ⚠️ **A guarda.** Nenhuma chamada de log menciona algo sensível.
    /// </summary>
    [Fact]
    public void NenhumaChamadaDeLogMencionaDadoSensivel()
    {
        var achados = new List<string>();

        foreach (var arquivo in Fontes())
        {
            var texto = File.ReadAllText(arquivo);

            foreach (Match chamada in Chamada.Matches(texto))
            {
                var corpo = chamada.Groups["corpo"].Value;
                var minusculo = corpo.ToLowerInvariant();

                foreach (var proibida in Proibidas)
                {
                    if (!minusculo.Contains(proibida, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var nome = Path.GetFileName(arquivo);

                    // Liberado nominalmente? So vale se o trecho casar - uma
                    // linha nova no MESMO arquivo continua sendo pega.
                    if (Liberados.Keys.Any(k =>
                            k.StartsWith(nome, StringComparison.OrdinalIgnoreCase)
                            && corpo.Contains(
                                k[(k.IndexOf("::", StringComparison.Ordinal) + 2)..],
                                StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    achados.Add($"{nome}: '{proibida}' em {Resumir(corpo)}");
                }
            }
        }

        Assert.True(
            achados.Count == 0,
            "Chamada de log mencionando dado sensivel (CLAUDE.md 24.16): "
            + string.Join(" | ", achados));
    }

    /// <summary>
    /// A varredura precisa estar realmente olhando alguma coisa.
    ///
    /// Sem esta contagem mínima, um erro de caminho transformaria o teste acima
    /// num que passa sempre — e passar sempre é indistinguível de proteger.
    /// </summary>
    [Fact]
    public void AVarreduraEncontraAsChamadasDeLogQueExistem()
    {
        var total = Fontes().Sum(a => Chamada.Matches(File.ReadAllText(a)).Count);

        Assert.True(total >= 15, $"A varredura so encontrou {total} chamadas de log.");
    }

    /// <summary>
    /// ⚠️ O controle: uma linha de log **deliberadamente errada** é detectada.
    ///
    /// Sem isto, a lista de palavras poderia estar escrita errada e o teste
    /// acima passaria por não achar nada — em vez de por não haver nada.
    /// </summary>
    [Fact]
    public void AGuardaDetectaUmaLinhaErradaDeProposito()
    {
        const string Ruim = """log.LogInformation("Entrou {Email} com {Senha}", email, senha);""";

        var chamada = Chamada.Match(Ruim);

        Assert.True(chamada.Success);

        Assert.Contains(
            Proibidas,
            p => chamada.Groups["corpo"].Value.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Toda liberação tem motivo escrito. Uma lista de exceções sem
    /// justificativa vira o lugar onde os problemas se escondem.
    /// </summary>
    [Fact]
    public void TodaLiberacaoTemMotivoEscrito()
    {
        foreach (var (trecho, motivo) in Liberados)
        {
            Assert.True(motivo.Length > 80, $"'{trecho}' precisa de um motivo de verdade.");
        }
    }

    private static string Resumir(string corpo)
    {
        var limpo = Regex.Replace(corpo, @"\s+", " ").Trim();

        return limpo.Length > 80 ? limpo[..80] : limpo;
    }
}
