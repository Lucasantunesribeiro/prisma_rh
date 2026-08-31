using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Lambda.SQSEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Infraestrutura.Identidade;
using PrismaRH.Infraestrutura.Persistencia;
using PrismaRH.Worker;

namespace PrismaRH.Worker;

/// <summary>
/// Entrada do worker.
///
/// Classe explicita, e nao top-level statements: a API tambem tem um `Program`
/// gerado, e o projeto de testes referencia os dois. Com os dois gerados, o
/// compilador acusa `CS0433 - o tipo Program existe em dois assemblies`, e a
/// suite inteira para de compilar.
/// </summary>
internal static class ProgramaWorker
{
    private static async Task Main()
    {
        // ============================================================================
        // O worker de importacao do Prisma RH (Fase 9).
        //
        // Runtime `provided.al2023`, publicado self-contained: a Lambda nao tem runtime
        // gerenciado para .NET 10.
        //
        // ## O contentor e montado UMA vez, fora do laco
        //
        // A Lambda reaproveita o processo entre invocacoes. Montar o `ServiceProvider`
        // aqui, e nao dentro do manipulador, faz a segunda mensagem reusar o pool de
        // conexoes e o modelo do EF ja compilado - o que, num limite de 128 MB e 60 s,
        // e a diferenca entre caber e nao caber.
        //
        // O que NAO e compartilhado e o tenant: cada mensagem abre o proprio escopo.
        // ============================================================================

        var conexao = ConexaoNeon.DoAmbiente()
            ?? throw new InvalidOperationException(
                $"Variavel {ConexaoNeon.Variavel} ausente. A funcao nao sobe sem banco.");

        var servicos = new ServiceCollection();

        // ⚠️ O contexto do tenant e SCOPED. Se fosse singleton, a mensagem seguinte
        // herdaria a organizacao da anterior - o vazamento que esta fase inteira
        // existe para impedir.
        servicos.AddScoped<ContextoDoTrabalho>();
        servicos.AddScoped<IContextoUsuario>(p => p.GetRequiredService<ContextoDoTrabalho>());
        servicos.AddSingleton<IRelogio, RelogioSistema>();

        servicos.AddDbContext<PrismaRhDbContext>(
            opcoes => opcoes.UseNpgsql(conexao),
            // Scoped e o padrao, e esta explicito porque aqui a razao e de seguranca,
            // e nao de performance: um DbContext singleton carregaria o filtro global
            // de uma organizacao para dentro da proxima mensagem.
            ServiceLifetime.Scoped);

        await using var raiz = servicos.BuildServiceProvider();

        // Descreve a conexao SEM a senha - host, banco e modo TLS. Ver `ConexaoNeon`.
        Console.WriteLine($"worker pronto: {ConexaoNeon.Descrever(conexao)}");

        var manipulador = new ManipuladorImportacao(raiz);

        Func<SQSEvent, ILambdaContext, Task<SQSBatchResponse>> tratar = async (evento, contexto) =>
        {
            var falhas = new List<SQSBatchResponse.BatchItemFailure>();

            foreach (var registro in evento.Records)
            {
                var desfecho = await manipulador.ProcessarAsync(
                    registro.Body,
                    texto => contexto.Logger.LogLine($"[{registro.MessageId}] {texto}"),
                    CancellationToken.None);

                // So `Retentar` volta para a fila. `Descartada` sai dela de proposito:
                // mensagem invalida nunca vai virar valida, e insistir tres vezes em
                // algo impossivel so gasta invocacao e requisicao de fila. O motivo
                // fica no log e, quando ha trabalho, no campo `Erro` dele.
                if (desfecho == DesfechoMensagem.Retentar)
                {
                    falhas.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = registro.MessageId });
                }
            }

            // Falhas PARCIAIS: sem isso, uma mensagem ruim no meio de dez faria as dez
            // voltarem para a fila - e as nove boas seriam reprocessadas, gastando
            // franquia e forcando a idempotencia a trabalhar por um erro que nem era
            // dela.
            return new SQSBatchResponse { BatchItemFailures = falhas };
        };

        await LambdaBootstrapBuilder
            .Create(tratar, new DefaultLambdaJsonSerializer())
            .Build()
            .RunAsync();
    }
}
