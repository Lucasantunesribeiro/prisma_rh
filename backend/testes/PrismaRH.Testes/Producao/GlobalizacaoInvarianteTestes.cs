using System.Text.RegularExpressions;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Testes.Producao;

/// <summary>
/// ⚠️ **O teste que faltava quando o motor de cálculo quebrou em produção.**
///
/// ## O que aconteceu
///
/// Em 02/09/2026, usando a produção como usuário, `POST
/// /api/folhas/{id}/calcular` devolveu **500**:
///
/// ```text
/// CultureNotFoundException: Only the invariant culture is supported in
/// globalization-invariant mode.
/// ```
///
/// Nove classes tinham `static readonly CultureInfo Brasil =
/// CultureInfo.GetCultureInfo("pt-BR")`. A Lambda roda com
/// `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` — o runtime `provided.al2023` não
/// traz ICU —, e nesse modo pedir cultura por nome **lança**.
///
/// Por ser inicializador **estático**, a falha não vinha da linha que formata
/// texto: vinha do **primeiro toque na classe**, derrubando o cálculo antes da
/// primeira conta.
///
/// ## Por que 1258 testes verdes não pegaram
///
/// **Porque a máquina de desenvolvimento e o runner do CI têm ICU instalado.**
/// O mesmo código passa nos dois e falha só no lugar que importa. É a mesma
/// família do modelo de IA aposentado: *ambiente verde não prova ambiente de
/// produção.*
///
/// A defesa tem duas camadas, e as duas são necessárias:
///
/// | Camada | O que ela pega |
/// |---|---|
/// | **Este teste** | a linha proibida, no momento em que alguém a escreve, com mensagem dizendo o porquê |
/// | **A suíte rodada com `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`** | qualquer caminho novo que dependa de ICU, inclusive os que este teste não sabe procurar |
///
/// Sozinho, este teste seria uma lista que envelhece. Sozinha, a variável de
/// ambiente diria "quebrou" sem dizer onde.
/// </summary>
public class GlobalizacaoInvarianteTestes
{
    /// <summary>Sobe até achar a raiz da solução — o teste roda de `bin/`.</summary>
    private static DirectoryInfo RaizDoCodigo()
    {
        var atual = new DirectoryInfo(AppContext.BaseDirectory);

        while (atual is not null && !File.Exists(Path.Combine(atual.FullName, "PrismaRH.sln")))
        {
            atual = atual.Parent;
        }

        Assert.NotNull(atual);
        return new DirectoryInfo(Path.Combine(atual!.FullName, "src"));
    }

    [Fact]
    public void NenhumCodigoPedeCulturaPeloNome()
    {
        var proibido = new Regex(
            @"(new\s+CultureInfo\s*\(\s*""|CultureInfo\.GetCultureInfo\s*\(\s*"")",
            RegexOptions.Compiled);

        var achados = new List<string>();

        foreach (var arquivo in RaizDoCodigo()
                     .EnumerateFiles("*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                 && !f.FullName.Contains("Migrations")))
        {
            var linhas = File.ReadAllLines(arquivo.FullName);

            for (var i = 0; i < linhas.Length; i++)
            {
                // Comentario nao executa - e a documentacao do defeito cita a
                // linha proibida de proposito.
                var texto = linhas[i].TrimStart();

                if (texto.StartsWith("//", StringComparison.Ordinal)
                    || texto.StartsWith("///", StringComparison.Ordinal)
                    || texto.StartsWith('*'))
                {
                    continue;
                }

                if (proibido.IsMatch(linhas[i]))
                {
                    achados.Add($"{arquivo.Name}:{i + 1}");
                }
            }
        }

        Assert.True(
            achados.Count == 0,
            "⚠️ Cultura pedida pelo NOME. Em modo globalization-invariant (a Lambda "
            + "de producao) isto LANCA, e num `static readonly` derruba a classe "
            + "inteira. Use `FormatoBrasileiro.Numero`.\n  "
            + string.Join("\n  ", achados));
    }

    [Theory]
    [InlineData(1234.56, "1.234,56")]
    [InlineData(0.5, "0,50")]
    [InlineData(-89.9, "-89,90")]
    [InlineData(1000000, "1.000.000,00")]
    public void OFormatoBrasileiroNaoDependeDeIcu(decimal valor, string esperado) =>
        Assert.Equal(esperado, valor.ToString("N2", FormatoBrasileiro.Numero));

    /// <summary>
    /// Tocar cada calculadora força o inicializador estático a rodar. Sob
    /// `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` este é o teste que reproduz a
    /// produção — e sem a variável ele apenas confirma que as classes carregam.
    /// </summary>
    [Fact]
    public void OsInicializadoresEstaticosDoDominioCarregam()
    {
        var tipos = typeof(MotorCalculoFolha).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
            .Where(t => t.Name.StartsWith("Calculadora", StringComparison.Ordinal)
                        || t.Name.EndsWith("Regra", StringComparison.Ordinal)
                        || t.Name is "MotorCalculoFolha" or "ApuracaoRescisao")
            .ToList();

        Assert.NotEmpty(tipos);

        foreach (var tipo in tipos)
        {
            var falha = Record.Exception(() =>
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(tipo.TypeHandle));

            Assert.True(falha is null, $"{tipo.Name} nao carrega: {falha?.Message}");
        }
    }
}
