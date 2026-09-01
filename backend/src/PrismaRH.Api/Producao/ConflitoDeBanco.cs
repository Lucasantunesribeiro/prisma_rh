using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PrismaRH.Api.Producao;

/// <summary>
/// Reconhece falhas de banco que significam **"nada foi gravado, tente de
/// novo"** — e as separa das que significam defeito.
///
/// ## O problema que isto resolve
///
/// O padrão do projeto é `catch (DbUpdateException)` seguido de rollback e
/// **409**. Isso cobre violação de índice único, que é o caso comum: duas
/// requisições simultâneas com o mesmo CPF.
///
/// Só que **nem todo conflito chega como `DbUpdateException`**. Sob carga, o
/// PostgreSQL aborta uma das transações concorrentes com:
///
/// - `40P01` — *deadlock detected*, quando duas transações travam recursos em
///   ordens diferentes;
/// - `40001` — *serialization failure*, quando o isolamento não consegue manter
///   a ilusão de execução sequencial.
///
/// Dependendo de **onde** isso acontece — no comando, no `COMMIT`, ou ao
/// devolver a conexão —, a exceção sobe como `NpgsqlException` crua, sem o
/// embrulho do EF. O `catch` estreito não pega, e o cliente recebe **500**.
///
/// Um teste de concorrência pegou exatamente isso: passava isolado e falhava
/// sob a carga da suíte inteira, com `InternalServerError` no lugar do 409.
///
/// ## Por que não capturar tudo
///
/// Porque `catch (Exception)` transformaria bug em 409, e 409 diz "tente de
/// novo" — o cliente tentaria para sempre contra um defeito que nunca vai
/// melhorar. A lista de códigos é curta e fechada de propósito: só entra o que
/// é **transitório por definição do PostgreSQL**.
/// </summary>
public static class ConflitoDeBanco
{
    /// <summary>Deadlock. Uma das transações foi escolhida como vítima e abortada.</summary>
    public const string Deadlock = "40P01";

    /// <summary>Falha de serialização sob isolamento alto.</summary>
    public const string FalhaSerializacao = "40001";

    /// <summary>Violação de restrição única — o caso comum, já coberto pelo EF.</summary>
    public const string ViolacaoUnica = "23505";

    /// <summary>
    /// A exceção significa "conflito, e nada foi gravado"?
    ///
    /// Percorre as causas internas porque o EF embrulha, e o Npgsql às vezes
    /// embrulha de novo — o `SqlState` costuma estar duas camadas abaixo.
    /// </summary>
    public static bool EhConflito(Exception? excecao)
    {
        for (var atual = excecao; atual is not null; atual = atual.InnerException)
        {
            if (atual is PostgresException postgres
                && postgres.SqlState is Deadlock or FalhaSerializacao or ViolacaoUnica)
            {
                return true;
            }

            // `DbUpdateException` sem Postgres por baixo ainda é conflito de
            // escrita: concorrência otimista do próprio EF entra aqui.
            if (atual is DbUpdateConcurrencyException)
            {
                return true;
            }
        }

        return false;
    }
}
