using Microsoft.EntityFrameworkCore;
using PrismaRH.Dominio.Assincrono;
using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Infraestrutura.Persistencia;

/// <summary>
/// O orcamento **global** de bytes temporarios, contado com seguranca contra
/// concorrencia.
///
/// ## O erro que esta classe existe para impedir
///
/// A conta ingenua e:
///
/// <code>
/// var usado = await db.BlobsTemporarios.SumAsync(b => b.TamanhoBytes);
/// if (usado + novo &lt;= teto) { db.Add(blob); await db.SaveChangesAsync(); }
/// </code>
///
/// Com duas requisicoes ao mesmo tempo, **as duas leem o mesmo total**, as duas
/// concluem que cabe, e as duas gravam. Com o teto em 50 MB e arquivos de 5 MB,
/// bastam duas requisicoes simultaneas na hora errada para o banco passar do
/// limite - e o limite existe justamente porque o espaco e o recurso mais
/// escasso do projeto.
///
/// Isto e uma **corrida de leitura-e-escrita** (`read-modify-write`), e nenhum
/// `if` em C# a resolve: o intervalo entre ler e gravar e onde a outra
/// requisicao passa.
///
/// ## A solucao: lock consultivo por transacao
///
/// `pg_advisory_xact_lock` pede ao PostgreSQL um cadeado nomeado que vale ate o
/// fim da transacao. Quem chega depois **espera**, em vez de ler um total velho.
///
/// Duas propriedades importam:
///
/// - **`_xact_`, e nao o de sessao.** O lock de sessao sobrevive ao commit e
///   precisa ser liberado a mao - se uma excecao escapasse antes do
///   `unlock`, o cadeado ficaria preso e travaria toda importacao seguinte. O
///   de transacao e devolvido pelo proprio commit ou rollback, sempre.
/// - **Funciona atras do PgBouncer.** O Neon gratuito usa pooler em modo
///   transacao, e nele uma transacao inteira vai para a mesma conexao de
///   backend. Lock de sessao, nesse modo, seria pego numa conexao e procurado
///   noutra.
///
/// ## Por que somar, e nao manter um contador
///
/// Um contador seria mais rapido e pode **divergir da realidade** - basta uma
/// remocao que esqueca de decrementar. `SUM(tamanho_bytes)` le uma coluna
/// pequena por indice, nunca toca nos `bytea`, e nao tem como mentir: o numero
/// e derivado das linhas que existem.
/// </summary>
public static class OrcamentoBlobs
{
    /// <summary>
    /// A chave do cadeado. Numero arbitrario e fixo - o que importa e que seja
    /// o **mesmo** em toda reserva, e diferente de qualquer outro lock
    /// consultivo que o sistema venha a usar.
    /// </summary>
    private const long ChaveDoLock = 918_273_645;

    /// <summary>
    /// Quanto o banco inteiro guarda de blobs agora.
    ///
    /// `IgnoreQueryFilters` de proposito, e este e um dos poucos lugares do
    /// sistema onde isso e correto: o orcamento e **global**. Contar so o que a
    /// organizacao atual enxerga daria um numero sempre menor que o real, e o
    /// teto nunca seria alcancado.
    ///
    /// Ler o tamanho de todos **nao** e ler o dado de ninguem: a soma nao
    /// revela de quem sao os arquivos, quantos cada um tem, nem o conteudo.
    /// </summary>
    public static Task<long> UsadoAsync(PrismaRhDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        return db.BlobsTemporarios
            .IgnoreQueryFilters()
            .SumAsync(b => (long)b.TamanhoBytes, ct);
    }

    /// <summary>
    /// Tenta reservar espaco para um arquivo.
    ///
    /// **Precisa ser chamado dentro de uma transacao** - e o `pg_advisory_xact_lock`
    /// so vale ate o fim dela. Devolve `false` quando nao cabe; quem chama
    /// desfaz a transacao e responde ao usuario que o espaco acabou.
    ///
    /// A ordem importa: cadeado primeiro, soma depois. Somar antes de travar
    /// seria ler o total velho com passos extras.
    /// </summary>
    public static async Task<bool> TentarReservarAsync(
        PrismaRhDbContext db,
        long tamanhoBytes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tamanhoBytes);

        if (tamanhoBytes > OrcamentoSemCusto.TamanhoMaximoArquivoBytes)
        {
            return false;
        }

        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({ChaveDoLock})", ct);

        var usado = await UsadoAsync(db, ct);

        return OrcamentoSemCusto.CabeNoOrcamentoGlobal(usado, tamanhoBytes);
    }

    /// <summary>
    /// Apaga os bytes de um trabalho, mantendo tudo o mais.
    ///
    /// Chamada quando o worker termina - **concluido ou falho**. Um arquivo
    /// processado nao precisa continuar existindo, e a `Importacao` guarda o
    /// historico: quem, quando, qual arquivo por hash, quantas linhas e o que
    /// deu errado.
    ///
    /// `ExecuteDeleteAsync` emite um unico `DELETE` e **nao carrega os bytes
    /// para a memoria** - carregar 5 MB para depois joga-los fora seria gastar
    /// o recurso que se esta tentando liberar.
    /// </summary>
    public static Task<int> ApagarDoTrabalhoAsync(
        PrismaRhDbContext db,
        Guid idTrabalho,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        return db.BlobsTemporarios
            .IgnoreQueryFilters()
            .Where(b => b.IdTrabalho == idTrabalho)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// A varredura dos orfaos.
    ///
    /// Existe porque a remocao no fim do processamento **nao cobre todos os
    /// caminhos**: worker morto no meio, mensagem perdida, trabalho que nunca
    /// foi enfileirado. Sem a varredura, cada um desses casos seria 5 MB
    /// perdidos para sempre num orcamento de 50 MB - dez acidentes e o sistema
    /// para de aceitar importacao.
    ///
    /// `IgnoreQueryFilters` porque a limpeza roda **fora de requisicao**, sem
    /// usuario: com o filtro valendo, `IdOrganizacaoAtual` seria `Guid.Empty` e
    /// a varredura nao encontraria nada - falharia silenciosamente, que e a
    /// pior forma de falhar.
    /// </summary>
    public static Task<int> ApagarExpiradosAsync(
        PrismaRhDbContext db,
        DateTimeOffset agora,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        return db.BlobsTemporarios
            .IgnoreQueryFilters()
            .Where(b => b.ExpiraEm <= agora)
            .ExecuteDeleteAsync(ct);
    }
}
