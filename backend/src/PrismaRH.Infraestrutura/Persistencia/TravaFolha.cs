using Microsoft.EntityFrameworkCore;

namespace PrismaRH.Infraestrutura.Persistencia;

/// <summary>
/// A trava que torna a folha fechada imutavel mesmo sob operacoes simultaneas.
///
/// ## O defeito que ela fecha
///
/// <c>FolhaPagamento.GarantirAberta</c> confere o estado que a requisicao
/// LEU. Sem trava, duas requisicoes leem a mesma folha aberta; uma fecha e
/// confirma; a outra, com o retrato antigo na memoria, grava lancamento ou
/// recalculo DEPOIS do fechamento. O fato historico muda, e o total que a
/// auditoria registrou no fechamento deixa de ser o total gravado. Reproduzido
/// em <c>FolhaConcorrenciaTestes</c> antes desta correcao.
///
/// ## Como
///
/// <c>SELECT ... FOR UPDATE</c> na linha da folha, dentro da transacao da
/// operacao e ANTES de carregar o agregado. Quem chega depois fica na fila do
/// PostgreSQL; quando entra, a leitura seguinte ja ve o estado confirmado pela
/// anterior (isolamento READ COMMITTED: cada comando enxerga o que ja foi
/// confirmado) - e <c>GarantirAberta</c> recusa com a mensagem real.
///
/// Escolhida em vez de token de versao (ADR 009) porque cobre tambem a
/// alteracao que so mexe em FILHO - um lancamento informativo de valor zero
/// nao faz UPDATE na folha, e um token que so anda com esse UPDATE deixaria a
/// gravacao passar. A trava e tomada antes de qualquer leitura, entao nao
/// depende do que a operacao vai gravar.
///
/// ## Cuidados
///
/// - **So dentro de transacao.** Fora dela o PostgreSQL solta a linha ao fim do
///   proprio SELECT, e a trava viraria enfeite. Por isso o metodo recusa.
/// - **A organizacao entra no WHERE.** A linha de outro tenant nao casa e nao e
///   travada: um id alheio nao consegue nem colocar a folha do vizinho em fila.
/// - **Prazo de espera** (<c>lock_timeout</c>, so nesta transacao): uma
///   operacao presa nao segura a requisicao seguinte indefinidamente. Estourar
///   o prazo gera <c>55P03</c>, que a API devolve como 409.
/// - **Funciona atras do PgBouncer em modo transacao** (Neon): trava de linha
///   vive na transacao, nao na sessao.
///
/// SQL bruto revisado (`CLAUDE.md` secao 24.8): os valores entram por
/// interpolacao parametrizada do EF; o unico texto fixo e o prazo, constante.
/// </summary>
public static class TravaFolha
{
    /// <summary>Quanto uma operacao espera pela folha antes de desistir com 409.</summary>
    public const string PrazoDeEspera = "5s";

    // Constante de compilacao: nenhum valor de fora entra neste texto.
    private const string DefinirPrazo = "SET LOCAL lock_timeout = '" + PrazoDeEspera + "'";

    /// <summary>
    /// Trava a folha para escrita ate o fim da transacao corrente. Devolve
    /// false quando a folha nao existe NESTA organizacao.
    /// </summary>
    public static async Task<bool> TravarParaEscritaAsync(
        this PrismaRhDbContext db, Guid idFolha, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A trava da folha so vale dentro de uma transacao: fora dela a linha e solta ao fim do SELECT.");
        }

        await db.Database.ExecuteSqlRawAsync(DefinirPrazo, ct);

        var idOrganizacao = db.IdOrganizacaoAtual;

        var travadas = await db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                FROM folhas_pagamento
                WHERE id = {idFolha} AND id_organizacao = {idOrganizacao}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        return travadas.Count == 1;
    }
}
