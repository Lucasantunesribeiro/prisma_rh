using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Assincrono;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Dominio.Importacao;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// O orcamento global de blobs, contra PostgreSQL real.
///
/// ## Por que estes testes nao podem usar banco falso
///
/// A defesa aqui e `pg_advisory_xact_lock`, uma primitiva **do PostgreSQL**.
/// O EF InMemory nao gera SQL, entao nao tem lock, nao tem transacao de
/// verdade, e um teste contra ele passaria sem provar coisa alguma - exatamente
/// a armadilha que o `CLAUDE.md secao 24.5` descreve para o filtro global.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class OrcamentoBlobsTestes(BancoPostgresFixture banco)
{
    private const int CincoMb = 5 * 1024 * 1024;

    /// <summary>
    /// Contexto ligado ao banco do container, com a organizacao informada.
    ///
    /// Cada um abre a **propria conexao**, e e isso que permite testar
    /// concorrencia de verdade: duas transacoes simultaneas em conexoes
    /// distintas, como duas requisicoes seriam.
    /// </summary>
    private PrismaRhDbContext Contexto(Guid idOrganizacao)
    {
        var opcoes = new DbContextOptionsBuilder<PrismaRhDbContext>()
            .UseNpgsql(banco.StringConexao)
            .Options;

        return new PrismaRhDbContext(opcoes, new ContextoFixo(idOrganizacao));
    }

    private sealed class ContextoFixo(Guid idOrganizacao) : IContextoUsuario
    {
        public bool EstaAutenticado => idOrganizacao != Guid.Empty;
        public Guid IdUsuario => Guid.Empty;
        public Guid IdOrganizacao => idOrganizacao;
        public Perfil Perfil => Perfil.AdministradorEmpresa;
    }

    private static async Task<TrabalhoAssincrono> CriarTrabalhoAsync(
        PrismaRhDbContext db, Guid org, Guid usuario, string sufixo)
    {
        var t = new TrabalhoAssincrono(
            org, usuario, TipoTrabalho.ImportacaoFuncionarios,
            $"teste:{org:N}:{sufixo}:{Guid.NewGuid():N}", DateTimeOffset.UtcNow);

        db.TrabalhosAssincronos.Add(t);
        await db.SaveChangesAsync(CancellationToken.None);

        return t;
    }

    private static BlobTemporario Blob(Guid org, Guid idTrabalho, int bytes, TimeSpan? retencao = null) =>
        new(org, idTrabalho, new byte[bytes], DateTimeOffset.UtcNow,
            retencao ?? OrcamentoSemCusto.RetencaoBlob);

    /// <summary>Deixa o orcamento vazio antes de cada cenario.</summary>
    private async Task LimparAsync()
    {
        await using var db = Contexto(Guid.Empty);
        await db.BlobsTemporarios.IgnoreQueryFilters().ExecuteDeleteAsync(CancellationToken.None);
    }

    private async Task<Guid> UsuarioDaAsync(Guid org)
    {
        await using var db = Contexto(org);
        return await db.Usuarios.Select(u => u.Id).FirstAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------- contabilidade

    [Fact]
    public async Task OTotalSomaOsBlobsDeTodasAsOrganizacoes()
    {
        await LimparAsync();

        var usuarioA = await UsuarioDaAsync(banco.IdOrganizacaoA);
        var usuarioB = await UsuarioDaAsync(banco.IdOrganizacaoB);

        await using (var db = Contexto(banco.IdOrganizacaoA))
        {
            var t = await CriarTrabalhoAsync(db, banco.IdOrganizacaoA, usuarioA, "soma-a");
            db.BlobsTemporarios.Add(Blob(banco.IdOrganizacaoA, t.Id, 1024));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        await using (var db = Contexto(banco.IdOrganizacaoB))
        {
            var t = await CriarTrabalhoAsync(db, banco.IdOrganizacaoB, usuarioB, "soma-b");
            db.BlobsTemporarios.Add(Blob(banco.IdOrganizacaoB, t.Id, 2048));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        // ⚠️ O orcamento e GLOBAL: contar so o que a organizacao atual enxerga
        // daria um numero sempre menor que o real, e o teto nunca chegaria.
        await using var leitura = Contexto(banco.IdOrganizacaoA);
        Assert.Equal(3072, await OrcamentoBlobs.UsadoAsync(leitura, CancellationToken.None));
    }

    /// <summary>
    /// O orcamento e compartilhado; o **dado** nao. Esta e a distincao que a
    /// correcao de arquitetura de 31/08/2026 introduziu, e ela precisa valer
    /// nas duas direcoes ao mesmo tempo.
    /// </summary>
    [Fact]
    public async Task OTetoESharedMasOsBytesContinuamIsolados()
    {
        await LimparAsync();

        var usuarioB = await UsuarioDaAsync(banco.IdOrganizacaoB);

        await using (var db = Contexto(banco.IdOrganizacaoB))
        {
            var t = await CriarTrabalhoAsync(db, banco.IdOrganizacaoB, usuarioB, "isolado");
            db.BlobsTemporarios.Add(Blob(banco.IdOrganizacaoB, t.Id, 4096));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        await using var daA = Contexto(banco.IdOrganizacaoA);

        // A organizacao A ENXERGA o espaco ocupado...
        Assert.Equal(4096, await OrcamentoBlobs.UsadoAsync(daA, CancellationToken.None));

        // ...e NAO enxerga um unico blob da vizinha.
        Assert.Empty(await daA.BlobsTemporarios.ToListAsync(CancellationToken.None));
    }

    // ------------------------------------------------------------- concorrencia

    /// <summary>
    /// ⚠️ **O teste que justifica o lock consultivo.**
    ///
    /// O orcamento esta com espaco para **exatamente mais um** arquivo de 5 MB.
    /// Duas requisicoes tentam ao mesmo tempo.
    ///
    /// Sem o `pg_advisory_xact_lock`, as duas leem o mesmo total, as duas
    /// concluem que cabe, e as duas gravam - o banco termina com 55 MB num teto
    /// de 50 MB. Com o lock, a segunda espera e ve o total ja atualizado.
    ///
    /// Este teste **reprovaria** se alguem trocasse o lock por um `if` em C#.
    /// </summary>
    [Fact]
    public async Task DuasRequisicoesSimultaneasNaoEstouramOTeto()
    {
        await LimparAsync();

        var usuarioA = await UsuarioDaAsync(banco.IdOrganizacaoA);
        var usuarioB = await UsuarioDaAsync(banco.IdOrganizacaoB);

        // Enche o orcamento deixando espaco para exatamente mais um arquivo.
        await using (var db = Contexto(banco.IdOrganizacaoA))
        {
            for (var i = 0; i < OrcamentoSemCusto.ArquivosNoTeto - 1; i++)
            {
                var t = await CriarTrabalhoAsync(db, banco.IdOrganizacaoA, usuarioA, $"enche-{i}");
                db.BlobsTemporarios.Add(Blob(banco.IdOrganizacaoA, t.Id, CincoMb));
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }

        var largada = new TaskCompletionSource();

        async Task<bool> Tentar(Guid org, Guid usuario, string sufixo)
        {
            await using var db = Contexto(org);
            var t = await CriarTrabalhoAsync(db, org, usuario, sufixo);

            await largada.Task; // as duas partem juntas

            await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);

            if (!await OrcamentoBlobs.TentarReservarAsync(db, CincoMb, CancellationToken.None))
            {
                await tx.RollbackAsync(CancellationToken.None);
                return false;
            }

            db.BlobsTemporarios.Add(Blob(org, t.Id, CincoMb));
            await db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);

            return true;
        }

        var uma = Tentar(banco.IdOrganizacaoA, usuarioA, "corrida-a");
        var outra = Tentar(banco.IdOrganizacaoB, usuarioB, "corrida-b");

        largada.SetResult();

        var resultados = await Task.WhenAll(uma, outra);

        // Exatamente uma passa. Qual delas nao importa - a corrida e legitima.
        Assert.Equal(1, resultados.Count(ok => ok));

        await using var conferencia = Contexto(banco.IdOrganizacaoA);
        var total = await OrcamentoBlobs.UsadoAsync(conferencia, CancellationToken.None);

        Assert.True(
            total <= OrcamentoSemCusto.ArmazenamentoGlobalMaximoBytes,
            $"orcamento estourado: {total} bytes contra teto de {OrcamentoSemCusto.ArmazenamentoGlobalMaximoBytes}");
    }

    [Fact]
    public async Task ComOrcamentoCheioAReservaERecusada()
    {
        await LimparAsync();

        var usuarioA = await UsuarioDaAsync(banco.IdOrganizacaoA);

        await using var db = Contexto(banco.IdOrganizacaoA);

        for (var i = 0; i < OrcamentoSemCusto.ArquivosNoTeto; i++)
        {
            var t = await CriarTrabalhoAsync(db, banco.IdOrganizacaoA, usuarioA, $"cheio-{i}");
            db.BlobsTemporarios.Add(Blob(banco.IdOrganizacaoA, t.Id, CincoMb));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);

        Assert.False(await OrcamentoBlobs.TentarReservarAsync(db, 1024, CancellationToken.None));

        await tx.RollbackAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ArquivoMaiorQueOTetoIndividualERecusadoComBancoVazio()
    {
        await LimparAsync();

        await using var db = Contexto(banco.IdOrganizacaoA);
        await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);

        Assert.False(await OrcamentoBlobs.TentarReservarAsync(
            db, OrcamentoSemCusto.TamanhoMaximoArquivoBytes + 1, CancellationToken.None));

        await tx.RollbackAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------------ limpeza

    /// <summary>
    /// Os bytes somem; o registro do trabalho fica. Apagar o blob nao apaga a
    /// historia de que a importacao aconteceu.
    /// </summary>
    [Fact]
    public async Task ApagarDoTrabalhoRemoveOsBytesEMantemOTrabalho()
    {
        await LimparAsync();

        var usuarioA = await UsuarioDaAsync(banco.IdOrganizacaoA);

        await using var db = Contexto(banco.IdOrganizacaoA);
        var t = await CriarTrabalhoAsync(db, banco.IdOrganizacaoA, usuarioA, "apagar");

        db.BlobsTemporarios.Add(Blob(banco.IdOrganizacaoA, t.Id, CincoMb));
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(1, await OrcamentoBlobs.ApagarDoTrabalhoAsync(db, t.Id, CancellationToken.None));

        Assert.Equal(0, await OrcamentoBlobs.UsadoAsync(db, CancellationToken.None));
        Assert.NotNull(await db.TrabalhosAssincronos.FindAsync([t.Id], CancellationToken.None));
    }

    /// <summary>
    /// ⚠️ A varredura dos orfaos.
    ///
    /// A remocao no fim do processamento nao cobre todos os caminhos: worker
    /// morto no meio, mensagem perdida, trabalho que nunca foi enfileirado. Sem
    /// isto, cada acidente sao 5 MB perdidos num orcamento de 50 MB - dez
    /// acidentes e o sistema para de aceitar importacao.
    /// </summary>
    [Fact]
    public async Task ExpiradosSaoVarridosEOsDeDentroDoPrazoFicam()
    {
        await LimparAsync();

        var usuarioA = await UsuarioDaAsync(banco.IdOrganizacaoA);
        var agora = DateTimeOffset.UtcNow;

        await using var db = Contexto(banco.IdOrganizacaoA);

        var vencido = await CriarTrabalhoAsync(db, banco.IdOrganizacaoA, usuarioA, "vencido");
        var vigente = await CriarTrabalhoAsync(db, banco.IdOrganizacaoA, usuarioA, "vigente");

        // Um nasceu com retencao ja vencida; o outro, com a normal.
        db.BlobsTemporarios.Add(Blob(banco.IdOrganizacaoA, vencido.Id, 1024, TimeSpan.FromDays(-1)));
        db.BlobsTemporarios.Add(Blob(banco.IdOrganizacaoA, vigente.Id, 2048));
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(1, await OrcamentoBlobs.ApagarExpiradosAsync(db, agora, CancellationToken.None));

        // So o vigente sobrou, e o espaco do vencido voltou para o orcamento.
        Assert.Equal(2048, await OrcamentoBlobs.UsadoAsync(db, CancellationToken.None));
    }

    /// <summary>
    /// A varredura roda **fora de requisicao**, sem usuario - e precisa
    /// alcançar blob de qualquer organizacao. Com o filtro global valendo,
    /// `IdOrganizacaoAtual` seria `Guid.Empty` e ela nao acharia nada:
    /// falharia em silencio, que e a pior forma de falhar.
    /// </summary>
    [Fact]
    public async Task AVarreduraAlcancaBlobDeQualquerOrganizacaoMesmoSemUsuario()
    {
        await LimparAsync();

        var usuarioB = await UsuarioDaAsync(banco.IdOrganizacaoB);

        await using (var db = Contexto(banco.IdOrganizacaoB))
        {
            var t = await CriarTrabalhoAsync(db, banco.IdOrganizacaoB, usuarioB, "orfao");
            db.BlobsTemporarios.Add(Blob(banco.IdOrganizacaoB, t.Id, 1024, TimeSpan.FromDays(-1)));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        // Contexto SEM usuario, como a limpeza roda de verdade.
        await using var semUsuario = Contexto(Guid.Empty);

        Assert.Equal(1, await OrcamentoBlobs.ApagarExpiradosAsync(
            semUsuario, DateTimeOffset.UtcNow, CancellationToken.None));
    }
}
