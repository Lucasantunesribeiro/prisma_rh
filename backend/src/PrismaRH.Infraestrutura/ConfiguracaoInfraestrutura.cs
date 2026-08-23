using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Infraestrutura.Identidade;
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

    public static IServiceCollection AdicionarInfraestrutura(
        this IServiceCollection servicos,
        IConfiguration configuracao)
    {
        var stringConexao = configuracao.GetConnectionString(NomeConexao);

        if (string.IsNullOrWhiteSpace(stringConexao))
        {
            throw new InvalidOperationException(
                $"String de conexao '{NomeConexao}' nao configurada. Defina 'ConnectionStrings:{NomeConexao}' " +
                $"no appsettings do ambiente ou a variavel de ambiente 'ConnectionStrings__{NomeConexao}'.");
        }

        servicos.AddDbContext<PrismaRhDbContext>(opcoes => opcoes.UseNpgsql(stringConexao));

        servicos
            .AddHealthChecks()
            .AddDbContextCheck<PrismaRhDbContext>(NomeVerificacaoBanco);

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
