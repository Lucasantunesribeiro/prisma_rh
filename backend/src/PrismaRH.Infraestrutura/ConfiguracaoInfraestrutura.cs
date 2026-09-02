using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Infraestrutura.Identidade;
using Amazon.SQS;
using Microsoft.Extensions.Logging;
using PrismaRH.Infraestrutura.Fila;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Infraestrutura;

/// <summary>
/// Registro dos servicos de infraestrutura no container de injecao de dependencias.
/// </summary>
public static class ConfiguracaoInfraestrutura
{
    /// <summary>Nome da string de conexao lida da configuracao do ambiente.</summary>
    public const string NomeConexao = "PrismaRh";

    /// <summary>Nome da verificacao de saude do banco de dados exposta em /health.</summary>
    public const string NomeVerificacaoBanco = "banco-de-dados";

    /// <summary>
    /// Nome da verificacao que compara o ESQUEMA do banco com as migrations do
    /// codigo. Separada da anterior de proposito: "responde" e "esta compativel"
    /// sao dois sinais, e confundi-los foi o que escondeu um incidente.
    /// </summary>
    public const string NomeVerificacaoMigrations = "migrations";

    public static IServiceCollection AdicionarInfraestrutura(
        this IServiceCollection servicos,
        IConfiguration configuracao)
    {
        // ------------------------------------------------- Neon (Fase 9)
        //
        // Quando `PRISMARH_NEON_CONNECTION` existe, ela tem precedencia: e o
        // banco que a Lambda alcanca da AWS, e o local do Docker nao e.
        //
        // A variavel vem no formato URI do Neon e e convertida aqui - colar o
        // URI direto no `UseNpgsql` falharia so na primeira conexao, e nao no
        // build. Ver `ConexaoNeon`.
        //
        // ⚠️ O valor NUNCA aparece em log nem em excecao. A mensagem de erro
        // abaixo fala de nomes de variavel, jamais de conteudo.
        var stringConexao = ConexaoNeon.DoAmbiente() ?? configuracao.GetConnectionString(NomeConexao);

        if (string.IsNullOrWhiteSpace(stringConexao))
        {
            throw new InvalidOperationException(
                $"String de conexao '{NomeConexao}' nao configurada. Defina 'ConnectionStrings:{NomeConexao}' " +
                $"no appsettings do ambiente ou a variavel de ambiente 'ConnectionStrings__{NomeConexao}'.");
        }

        servicos.AddDbContext<PrismaRhDbContext>(opcoes => opcoes.UseNpgsql(stringConexao));

        servicos
            .AddHealthChecks()
            .AddDbContextCheck<PrismaRhDbContext>(NomeVerificacaoBanco)

            // ⚠️ Acrescentado em 02/09/2026, depois de um incidente REAL.
            //
            // O codigo do bloqueio progressivo foi publicado sem a migration ser
            // aplicada no Neon. O login passou a responder 500 com
            // `42703: column u.bloqueado_ate does not exist` - e o `/health`
            // continuou dizendo `saudavel` o tempo todo.
            //
            // A causa: `AddDbContextCheck` prova que o banco RESPONDE, e nada
            // mais. Banco alcancavel e banco compativel com o codigo sao duas
            // perguntas diferentes, e so a segunda diz se a aplicacao consegue
            // servir.
            //
            // Migrations nao rodam no startup de proposito (`CLAUDE.md secao
            // 24.21`: aplicar migration e credencial diferente de executar). O
            // preco disso e justamente este: da para publicar codigo a frente do
            // esquema. Esta verificacao transforma o defeito silencioso num
            // sinal visivel.
            .AddDbContextCheck<PrismaRhDbContext>(
                NomeVerificacaoMigrations,
                customTestQuery: async (db, ct) =>
                    !(await db.Database.GetPendingMigrationsAsync(ct)).Any());

        // ------------------------------------------------------- fila (Fase 9)
        //
        // O cliente da SQS so e registrado quando ha fila configurada. Sem a
        // variavel, `PublicadorFila` recebe `null` e vira no-op com aviso -
        // o ambiente local e a suite de testes rodam sem AWS, e a API precisa
        // subir do mesmo jeito (`CLAUDE.md secao 1`).
        //
        // As credenciais vem da cadeia padrao do SDK (perfil, variaveis de
        // ambiente, papel da instancia). NUNCA do appsettings, e nunca do
        // repositorio.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PublicadorFila.VariavelUrl)))
        {
            servicos.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient());
        }

        servicos.AddSingleton<PublicadorFila>(p => new PublicadorFila(
            p.GetService<IAmazonSQS>(),
            p.GetRequiredService<ILogger<PublicadorFila>>()));

        servicos.AddOptions<OpcoesJwt>()
            .Bind(configuracao.GetSection(OpcoesJwt.Secao))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ChaveAssinatura) && o.ChaveAssinatura.Length >= 32,
                "Jwt:ChaveAssinatura e obrigatoria e precisa ter ao menos 32 caracteres. " +
                "Fora de Development, defina a variavel de ambiente Jwt__ChaveAssinatura.")
            .Validate(o => o.MinutosAccessToken is > 0 and <= 60,
                "Jwt:MinutosAccessToken deve ficar entre 1 e 60.")
            .ValidateOnStart();

        // Padrao seguro: sem requisicao HTTP nao ha organizacao, e o filtro
        // global devolve vazio. A Api registra a versao que le os claims
        // depois desta, e a ultima registrada vence na resolucao.
        servicos.TryAddScoped<IContextoUsuario, ContextoSemUsuario>();

        servicos.AddSingleton<IRelogio, RelogioSistema>();
        servicos.AddSingleton<IHasheadorSenha, HasheadorSenha>();
        servicos.AddScoped<IGeradorTokens, GeradorJwt>();
        servicos.AddScoped<IArmazenamentoIdentidade, ArmazenamentoIdentidade>();
        servicos.AddScoped<AutenticacaoServico>();

        return servicos;
    }
}
