using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Interceptor de teste que controla o instante da gravacao.
///
/// Corrida de verdade nao se prova rodando "muitas vezes e torcendo": aqui a
/// ordem e escolhida pelo teste. Duas armas, ambas de disparo unico:
///
/// - **Pausa**: a proxima gravacao avisa que chegou e espera ser liberada. Na
///   API, isso acontece DEPOIS de a requisicao carregar a folha e aplicar o
///   dominio - e o momento exato em que uma segunda requisicao poderia passar
///   na frente.
/// - **Falha depois de gravar**: a proxima gravacao executa o SQL e so entao
///   lanca. Sem transacao explicita, o EF ja confirmou o que gravou; com ela, o
///   rollback desfaz. E a diferenca que o teste de atomicidade mede.
///
/// Toda espera tem prazo: um defeito vira falha com mensagem, e nao uma suite
/// presa.
/// </summary>
public sealed class PortaoDoSalvar : SaveChangesInterceptor
{
    public static readonly TimeSpan Prazo = TimeSpan.FromSeconds(30);

    private int _pausaArmada;
    private int _falhaArmada;
    private TaskCompletionSource _chegou = Novo();
    private TaskCompletionSource _liberar = Novo();

    private static TaskCompletionSource Novo() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Arma a pausa. A tarefa devolvida termina quando uma gravacao chegou ao
    /// portao e esta parada esperando <see cref="Liberar"/>.
    /// </summary>
    public Task ArmarPausa()
    {
        _chegou = Novo();
        _liberar = Novo();
        Volatile.Write(ref _pausaArmada, 1);
        return _chegou.Task.WaitAsync(Prazo);
    }

    public void Liberar() => _liberar.TrySetResult();

    public void ArmarFalhaDepoisDeGravar() => Volatile.Write(ref _falhaArmada, 1);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _pausaArmada, 0) == 1)
        {
            _chegou.TrySetResult();
            await _liberar.Task.WaitAsync(Prazo, cancellationToken);
        }

        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _falhaArmada, 0) == 1)
        {
            throw new InvalidOperationException("Falha simulada depois de gravar.");
        }

        return ValueTask.FromResult(result);
    }
}
