using PrismaRH.Aplicacao.Comum;
using PrismaRH.Dominio.Importacao;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Servicos;

/// <summary>
/// Apaga os blobs temporarios que venceram.
///
/// ## Por que uma varredura, se o worker ja apaga ao terminar
///
/// Porque a remocao no fim do processamento **nao cobre todos os caminhos**:
///
/// - o worker morreu no meio;
/// - a mensagem se perdeu antes de chegar na fila;
/// - a publicacao falhou depois do commit (aconteceu de verdade durante o
///   desenvolvimento desta fase, por credencial errada no ambiente);
/// - o trabalho foi para a DLQ e ninguem voltou nele.
///
/// Sem a varredura, cada um desses acidentes sao ate 5 MB perdidos para sempre
/// num orcamento de 50 MB. **Dez acidentes e o sistema para de aceitar
/// importacao** - e o sintoma apareceria como "sem espaco", meses depois, sem
/// ninguem ligar uma coisa a outra.
///
/// ## Por que dentro da API, e nao numa Lambda agendada
///
/// Uma regra agendada do EventBridge para varrer 50 MB seria mais um recurso
/// para existir, monitorar e destruir - e o requisito da fase e **custo
/// previsto de US$ 0,00**. A API ja esta de pe; um laco que acorda de hora em
/// hora custa nada.
///
/// ## Fora de requisicao, e por isso sem tenant
///
/// A varredura roda sem usuario autenticado. `OrcamentoBlobs.ApagarExpiradosAsync`
/// usa `IgnoreQueryFilters` de proposito: com o filtro global valendo,
/// `IdOrganizacaoAtual` seria `Guid.Empty` e a varredura nao acharia nada -
/// falharia em silencio, que e a pior forma de falhar.
/// </summary>
public sealed class VarreduraBlobs(
    IServiceProvider raiz,
    ILogger<VarreduraBlobs> log) : BackgroundService
{
    /// <summary>
    /// De hora em hora. A retencao e de 7 dias, entao a precisao nao importa -
    /// o que importa e que o espaco volte sem ninguem precisar lembrar.
    /// </summary>
    public static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken parada)
    {
        // Uma passada logo no arranque: se a aplicacao ficou fora do ar por
        // dias, o que venceu nesse periodo nao precisa esperar mais uma hora.
        while (!parada.IsCancellationRequested)
        {
            try
            {
                using var escopo = raiz.CreateScope();
                var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();
                var relogio = escopo.ServiceProvider.GetRequiredService<IRelogio>();

                var apagados = await OrcamentoBlobs.ApagarExpiradosAsync(db, relogio.Agora, parada);

                if (apagados > 0)
                {
                    var usado = await OrcamentoBlobs.UsadoAsync(db, parada);

                    // Quantidades, nunca conteudo. O log tecnico nao e lugar de
                    // dado pessoal (`CLAUDE.md secao 24.16`).
                    log.LogInformation(
                        "Varredura removeu {Apagados} blob(s) vencido(s). Ocupacao: {Usado} de {Teto} bytes.",
                        apagados, usado, OrcamentoSemCusto.ArmazenamentoGlobalMaximoBytes);
                }
            }
            catch (OperationCanceledException) when (parada.IsCancellationRequested)
            {
                break;
            }
            catch (Exception excecao)
            {
                // Falha na varredura NAO derruba a aplicacao: ela e higiene, e
                // nao caminho de requisicao. O espaco volta na proxima passada.
                log.LogWarning(excecao, "Varredura de blobs falhou. Nova tentativa em {Intervalo}.", Intervalo);
            }

            try
            {
                await Task.Delay(Intervalo, parada);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
