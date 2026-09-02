using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.Extensions.Configuration;

namespace PrismaRH.Infraestrutura.Producao;

/// <summary>
/// Lê os segredos de produção do **SSM Parameter Store**, e não de variável de
/// ambiente.
///
/// ## O defeito que isto corrige
///
/// `CLAUDE.md §24.19 item 9`: a AWS devolve as variáveis de ambiente de uma
/// função em **texto puro** para qualquer chamada de `lambda:ListFunctions` ou
/// `lambda:GetFunctionConfiguration`. Guardar ali a senha do banco e a chave de
/// assinatura do JWT significava que **um comando de leitura entregava as
/// duas** — e com a chave do JWT um atacante forja token para qualquer usuário
/// de qualquer organização, o que derruba de uma vez o filtro global, a matriz
/// de perfis e o isolamento inteiro.
///
/// Depois desta mudança, o que a função carrega é o **nome** do parâmetro:
///
/// ```text
/// PRISMARH_SSM_PARAMETRO_BANCO = /portfolio/prisma-rh/prod/database
/// PRISMARH_SSM_PARAMETRO_JWT   = /portfolio/prisma-rh/prod/jwt-signing-key
/// ```
///
/// Nome não é segredo. Ler o valor passa a exigir `ssm:GetParameters` **naquele
/// ARN específico** mais `kms:Decrypt` — duas permissões a mais, cada uma
/// concedida a um papel só.
///
/// ## Por que Parameter Store, e não Secrets Manager
///
/// Custo. O `CLAUDE.md §16` exclui serviço que cobra por existir, e o Secrets
/// Manager cobra por segredo por mês. Verificado na documentação vigente antes
/// de criar qualquer coisa:
///
/// - **parâmetro standard**: *"Standard parameters are available at no
///   additional charge"* — inclusive as chamadas de API no throughput padrão;
/// - **chave gerenciada pela AWS** (`aws/ssm`): *"You are not charged for (...)
///   creation and storage of AWS managed (...) KMS keys"*, com franquia de
///   **20.000 requisições/mês** de KMS.
///
/// Nenhuma *customer-managed key* é criada — ela custaria US$ 1,00/mês só por
/// existir.
///
/// ## ⚠️ Uma chamada por CONTAINER, não por requisição
///
/// A franquia de 20.000 requisições/mês do KMS só é folgada porque a busca
/// acontece **uma vez, no startup**. Buscar por requisição transformaria um
/// portfólio ocioso numa conta — e, pior, colocaria uma dependência de rede no
/// caminho de toda chamada.
///
/// O cache é o próprio ciclo de vida do container: o valor é lido durante o
/// registro dos serviços e vive enquanto o processo viver. Não há TTL porque
/// não há releitura — trocar o segredo exige publicar de novo, que é o
/// comportamento desejado para uma rotação deliberada.
///
/// ## Fallback, e por que ele não enfraquece nada
///
/// Sem o nome do parâmetro configurado, cai na variável de ambiente direta.
/// Isso mantém desenvolvimento, testes e Docker local funcionando sem AWS
/// nenhuma — e **não** é uma porta dos fundos em produção: lá a variável com o
/// segredo deixa de existir, então não há o que cair.
/// </summary>
public static class SegredosSsm
{
    /// <summary>Nome do parâmetro com a conexão do banco.</summary>
    public const string VariavelParametroBanco = "PRISMARH_SSM_PARAMETRO_BANCO";

    /// <summary>Nome do parâmetro com a chave de assinatura do JWT.</summary>
    public const string VariavelParametroJwt = "PRISMARH_SSM_PARAMETRO_JWT";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Carregados =
        new(Buscar, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Há parâmetros do SSM configurados neste ambiente?
    ///
    /// Em Development e nos testes isto é falso, e nada da AWS é tocado.
    /// </summary>
    public static bool Configurado =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VariavelParametroBanco))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VariavelParametroJwt));

    /// <summary>
    /// O valor do parâmetro apontado por <paramref name="variavelComONome"/>,
    /// ou `null` quando não há parâmetro configurado.
    ///
    /// ⚠️ Devolve `null` também quando o parâmetro está configurado mas a busca
    /// falhou. Quem chama decide o que fazer — e no caso da conexão e da chave
    /// JWT o resultado é a aplicação **não subir**, que é o correto: subir sem
    /// chave de assinatura válida seria pior que não subir.
    /// </summary>
    public static string? Ler(string variavelComONome)
    {
        var nome = Environment.GetEnvironmentVariable(variavelComONome);

        if (string.IsNullOrWhiteSpace(nome))
        {
            return null;
        }

        return Carregados.Value.TryGetValue(nome, out var valor) ? valor : null;
    }

    /// <summary>
    /// Injeta os segredos do SSM **na configuração**, sobrepondo o que veio de
    /// `appsettings` e de variável de ambiente.
    ///
    /// ## ⚠️ Por que na configuração, e não no `IOptions`
    ///
    /// Este método existe por causa de um defeito real, cometido e corrigido em
    /// 02/09/2026. A primeira versão injetava a chave via
    /// `AddOptions&lt;OpcoesJwt&gt;().Configure(...)` — e a API **caiu em produção**
    /// com `IDX10703: key length is zero`.
    ///
    /// A causa: existem **dois caminhos independentes** lendo o mesmo segredo.
    /// O `GeradorJwt` **emite** token pelo `IOptions`; o `AddJwtBearer` do
    /// `Program.cs` **valida** token lendo `builder.Configuration` direto. A
    /// correção cobria só o primeiro, e a validação subia com chave vazia.
    ///
    /// Alimentar a **configuração** cobre os dois de uma vez, porque os dois
    /// nascem dela. A lição é geral: quando um valor tem duas portas de
    /// entrada, corrigir uma é corrigir metade.
    ///
    /// Precisa ser chamado **antes** de qualquer leitura — logo no início do
    /// `Program.cs`.
    /// </summary>
    public static void AdicionarNaConfiguracao(IConfigurationBuilder configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        var chaveJwt = Ler(VariavelParametroJwt);

        if (string.IsNullOrWhiteSpace(chaveJwt))
        {
            return;
        }

        // Fonte em memoria adicionada por ULTIMO: na configuracao do .NET, a
        // ultima fonte vence. E o que faz o cofre sobrepor a variavel de
        // ambiente sem precisar remover a variavel primeiro.
        configuracao.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Jwt:ChaveAssinatura"] = chaveJwt });
    }

    /// <summary>
    /// Busca **todos** os parâmetros configurados de uma vez.
    ///
    /// `GetParameters` no plural de propósito: dois segredos numa chamada, e
    /// não duas. Metade das requisições de KMS pelo mesmo resultado.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Buscar()
    {
        var nomes = new[] { VariavelParametroBanco, VariavelParametroJwt }
            .Select(Environment.GetEnvironmentVariable)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct()
            .ToList();

        if (nomes.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        using var cliente = new AmazonSimpleSystemsManagementClient();

        // `WithDecryption` e o que faz o SSM pedir o Decrypt ao KMS. Sem ele o
        // valor volta cifrado e a aplicacao subiria com lixo por senha.
        var resposta = cliente.GetParametersAsync(new GetParametersRequest
        {
            Names = nomes,
            WithDecryption = true,
        }).GetAwaiter().GetResult();

        // ⚠️ `InvalidParameters` sao os nomes que NAO existem. Ignora-los aqui e
        // deliberado: quem chama recebe `null` e decide. Lancar daqui com o
        // nome dentro da mensagem colocaria o caminho do parametro em log de
        // excecao - e caminho nao e segredo, mas tambem nao precisa circular.
        return resposta.Parameters.ToDictionary(p => p.Name, p => p.Value);
    }
}
