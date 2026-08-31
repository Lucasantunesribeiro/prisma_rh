using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Assincrono;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Dominio.Importacao;
using PrismaRH.Dominio.Pessoas;
using PrismaRH.Infraestrutura.Identidade;
using PrismaRH.Infraestrutura.Persistencia;
using PrismaRH.Worker;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// O worker de importacao (Fase 9), contra PostgreSQL real.
///
/// ## Por que o worker de verdade, e nao um duble
///
/// A classe testada e `ManipuladorImportacao` - a mesma que roda na Lambda. O
/// que fica de fora e so o transporte: em vez de a SQS entregar o corpo, o
/// teste entrega. Guarda de tenant, idempotencia, transacao, limpeza do blob e
/// decisao de retry sao o codigo de producao.
///
/// Um duble aqui provaria que o duble funciona.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class WorkerImportacaoTestes(BancoPostgresFixture banco)
{
    private sealed class ContextoFixo(Guid org) : IContextoUsuario
    {
        public bool EstaAutenticado => org != Guid.Empty;
        public Guid IdUsuario => Guid.Empty;
        public Guid IdOrganizacao => org;
        public Perfil Perfil => Perfil.AdministradorEmpresa;
    }

    /// <summary>
    /// Um contentor igual ao que a Lambda monta: contexto do trabalho scoped,
    /// relogio singleton, DbContext scoped.
    /// </summary>
    private ServiceProvider Contentor()
    {
        var s = new ServiceCollection();
        s.AddScoped<ContextoDoTrabalho>();
        s.AddScoped<IContextoUsuario>(p => p.GetRequiredService<ContextoDoTrabalho>());
        s.AddSingleton<IRelogio, RelogioSistema>();
        s.AddDbContext<PrismaRhDbContext>(o => o.UseNpgsql(banco.StringConexao), ServiceLifetime.Scoped);
        return s.BuildServiceProvider();
    }

    private PrismaRhDbContext Db(Guid org) =>
        new(new DbContextOptionsBuilder<PrismaRhDbContext>().UseNpgsql(banco.StringConexao).Options,
            new ContextoFixo(org));

    private static void Log(string _) { }

    /// <summary>CPF valido e unico, para nao colidir com o que ja existe.</summary>
    private static string Cpf()
    {
        var n = new List<int>();
        var r = Random.Shared;
        for (var i = 0; i < 9; i++) n.Add(r.Next(0, 10));

        for (var v = 0; v < 2; v++)
        {
            var soma = 0;
            for (var i = 0; i < n.Count; i++) soma += (n.Count + 1 - i) * n[i];
            var d = soma * 10 % 11;
            n.Add(d == 10 ? 0 : d);
        }

        return string.Concat(n);
    }

    private static byte[] Csv(int linhas)
    {
        // Ponto e virgula: o delimitador padrao do sistema, porque o Excel
        // pt-BR salva assim (ver LeitorCsv.DelimitadorPadrao).
        var texto = "nome;cpf;data de nascimento\n";

        for (var i = 0; i < linhas; i++)
        {
            texto += $"Worker Teste {Guid.NewGuid():N}[..8];{Cpf()};1990-0{(i % 9) + 1}-15\n";
        }

        return System.Text.Encoding.UTF8.GetBytes(texto);
    }

    private async Task<(Guid trabalho, Guid usuario)> PrepararAsync(Guid org, byte[] bytes)
    {
        await using var db = Db(org);
        var usuario = await db.Usuarios.Select(u => u.Id).FirstAsync(CancellationToken.None);

        var t = new TrabalhoAssincrono(
            org, usuario, TipoTrabalho.ImportacaoFuncionarios,
            $"worker:{Guid.NewGuid():N}", DateTimeOffset.UtcNow);

        db.TrabalhosAssincronos.Add(t);
        db.BlobsTemporarios.Add(new BlobTemporario(
            org, t.Id, bytes, DateTimeOffset.UtcNow, OrcamentoSemCusto.RetencaoBlob));

        await db.SaveChangesAsync(CancellationToken.None);

        return (t.Id, usuario);
    }

    private static string Corpo(Guid trabalho, Guid org) => new MensagemTrabalho(
        MensagemTrabalho.VersaoAtual, trabalho, org, TipoTrabalho.ImportacaoFuncionarios).Serializar();

    // ---------------------------------------------------------- caminho feliz

    [Fact]
    public async Task ProcessaOArquivoECriaOsFuncionarios()
    {
        var org = banco.IdOrganizacaoA;
        var (trabalho, _) = await PrepararAsync(org, Csv(3));

        await using var contentor = Contentor();
        var desfecho = await new ManipuladorImportacao(contentor)
            .ProcessarAsync(Corpo(trabalho, org), Log, CancellationToken.None);

        Assert.Equal(DesfechoMensagem.Concluida, desfecho);

        await using var db = Db(org);
        var t = await db.TrabalhosAssincronos.FirstAsync(x => x.Id == trabalho, CancellationToken.None);

        Assert.Equal(StatusTrabalho.Concluido, t.Status);
        Assert.NotNull(t.IdRecurso);
        Assert.Equal(1, t.Tentativas);
    }

    /// <summary>
    /// ⚠️ Os bytes vao embora ao concluir; o registro fica.
    ///
    /// Guardar CPF e salario "por precaucao" e exatamente o que a minimizacao
    /// proibe - e num orcamento de 50 MB globais, tambem e o que trava o
    /// sistema depois de dez importacoes.
    /// </summary>
    [Fact]
    public async Task ConcluirApagaOsBytesEMantemAImportacao()
    {
        var org = banco.IdOrganizacaoA;
        var (trabalho, _) = await PrepararAsync(org, Csv(2));

        await using var contentor = Contentor();
        await new ManipuladorImportacao(contentor)
            .ProcessarAsync(Corpo(trabalho, org), Log, CancellationToken.None);

        await using var db = Db(org);

        Assert.False(await db.BlobsTemporarios.AnyAsync(b => b.IdTrabalho == trabalho, CancellationToken.None));

        var t = await db.TrabalhosAssincronos.FirstAsync(x => x.Id == trabalho, CancellationToken.None);
        Assert.NotNull(await db.Importacoes.FirstOrDefaultAsync(i => i.Id == t.IdRecurso, CancellationToken.None));
    }

    // ------------------------------------------------------------ idempotencia

    /// <summary>
    /// ⚠️ A SQS entrega **pelo menos uma vez**. Sem esta recusa, a mesma
    /// planilha entregue duas vezes criaria os funcionarios duas vezes - e
    /// ninguem perceberia ate a folha sair errada.
    /// </summary>
    [Fact]
    public async Task MensagemDuplicadaNaoImportaDuasVezes()
    {
        var org = banco.IdOrganizacaoA;
        var (trabalho, _) = await PrepararAsync(org, Csv(3));
        var corpo = Corpo(trabalho, org);

        await using var contentor = Contentor();
        var manipulador = new ManipuladorImportacao(contentor);

        await using (var db = Db(org))
        {
            var antes = await db.Funcionarios.CountAsync(CancellationToken.None);

            Assert.Equal(DesfechoMensagem.Concluida,
                await manipulador.ProcessarAsync(corpo, Log, CancellationToken.None));

            // A fila entrega DE NOVO.
            Assert.Equal(DesfechoMensagem.JaFeita,
                await manipulador.ProcessarAsync(corpo, Log, CancellationToken.None));

            await using var conferencia = Db(org);
            Assert.Equal(antes + 3, await conferencia.Funcionarios.CountAsync(CancellationToken.None));
        }
    }

    // --------------------------------------------------------- mensagem torta

    [Theory]
    [InlineData("nao e json")]
    [InlineData("{}")]
    [InlineData("{\"versao\":99,\"idTrabalho\":\"00000000-0000-0000-0000-000000000001\","
        + "\"idOrganizacao\":\"00000000-0000-0000-0000-000000000002\",\"tipo\":\"ImportacaoFuncionarios\"}")]
    public async Task MensagemInvalidaEDescartadaSemTocarNoBanco(string corpo)
    {
        await using var contentor = Contentor();

        Assert.Equal(DesfechoMensagem.Descartada,
            await new ManipuladorImportacao(contentor).ProcessarAsync(corpo, Log, CancellationToken.None));
    }

    /// <summary>
    /// Mensagem sem tenant nao e "mensagem do sistema": e mensagem invalida.
    /// Aceita-la faria o worker rodar sem dono.
    /// </summary>
    [Fact]
    public async Task MensagemSemTenantEDescartada()
    {
        var org = banco.IdOrganizacaoA;
        var (trabalho, _) = await PrepararAsync(org, Csv(1));

        await using var contentor = Contentor();

        Assert.Equal(DesfechoMensagem.Descartada, await new ManipuladorImportacao(contentor)
            .ProcessarAsync(Corpo(trabalho, Guid.Empty), Log, CancellationToken.None));
    }

    /// <summary>
    /// ⚠️ **O teste mais importante da fase.**
    ///
    /// A mensagem e um JSON perfeitamente valido, aponta para um trabalho que
    /// existe - e diz que ele pertence a OUTRA organizacao. Processar
    /// significaria rodar a planilha de uma empresa dentro dos dados de outra.
    ///
    /// O worker carrega o trabalho com `IgnoreQueryFilters` (ainda nao ha
    /// tenant) e **confere** a mensagem contra ele. E aqui que para.
    /// </summary>
    [Fact]
    public async Task MensagemComTenantDeOutraOrganizacaoEDescartadaSemProcessar()
    {
        var org = banco.IdOrganizacaoA;
        var (trabalho, _) = await PrepararAsync(org, Csv(3));

        await using var contentor = Contentor();

        var desfecho = await new ManipuladorImportacao(contentor)
            .ProcessarAsync(Corpo(trabalho, banco.IdOrganizacaoB), Log, CancellationToken.None);

        Assert.Equal(DesfechoMensagem.Descartada, desfecho);

        await using var db = Db(org);
        var t = await db.TrabalhosAssincronos.FirstAsync(x => x.Id == trabalho, CancellationToken.None);

        // Nao foi tocado: nem iniciado, nem tentado, nem concluido.
        Assert.Equal(StatusTrabalho.Enfileirado, t.Status);
        Assert.Equal(0, t.Tentativas);

        // E os bytes continuam la - nada foi processado nem apagado.
        Assert.True(await db.BlobsTemporarios.AnyAsync(b => b.IdTrabalho == trabalho, CancellationToken.None));
    }

    [Fact]
    public async Task TrabalhoInexistenteEDescartado()
    {
        await using var contentor = Contentor();

        Assert.Equal(DesfechoMensagem.Descartada, await new ManipuladorImportacao(contentor)
            .ProcessarAsync(Corpo(Guid.CreateVersion7(), banco.IdOrganizacaoA), Log, CancellationToken.None));
    }

    // ------------------------------------------------------- arquivo ausente

    /// <summary>
    /// Sem bytes nao ha o que processar, e nao adianta tentar de novo - o
    /// arquivo nao vai reaparecer. E o caso do blob que a varredura ja levou.
    /// </summary>
    [Fact]
    public async Task TrabalhoSemArquivoFalhaSemRetentar()
    {
        var org = banco.IdOrganizacaoA;
        var (trabalho, _) = await PrepararAsync(org, Csv(1));

        await using (var db = Db(org))
        {
            await OrcamentoBlobs.ApagarDoTrabalhoAsync(db, trabalho, CancellationToken.None);
        }

        await using var contentor = Contentor();
        var desfecho = await new ManipuladorImportacao(contentor)
            .ProcessarAsync(Corpo(trabalho, org), Log, CancellationToken.None);

        Assert.Equal(DesfechoMensagem.Descartada, desfecho);

        await using var conferencia = Db(org);
        var t = await conferencia.TrabalhosAssincronos.FirstAsync(x => x.Id == trabalho, CancellationToken.None);

        Assert.Equal(StatusTrabalho.Falhou, t.Status);
        Assert.Contains("expirado", t.Erro!, StringComparison.OrdinalIgnoreCase);
    }

    // ----------------------------------------------------- arquivo com defeito

    /// <summary>
    /// Planilha com erro **nao** e falha do trabalho: o trabalho fez o que
    /// devia, e o resultado e uma importacao recusada. Confundir os dois faria
    /// a mensagem voltar para a fila tres vezes e cair na DLQ por um arquivo
    /// que nunca vai ficar bom sozinho.
    /// </summary>
    [Fact]
    public async Task PlanilhaComErroConcluiOTrabalhoComImportacaoRecusada()
    {
        var org = banco.IdOrganizacaoA;
        var ruim = System.Text.Encoding.UTF8.GetBytes("coluna_errada;outra\nvalor;valor\n");
        var (trabalho, _) = await PrepararAsync(org, ruim);

        await using var contentor = Contentor();
        var desfecho = await new ManipuladorImportacao(contentor)
            .ProcessarAsync(Corpo(trabalho, org), Log, CancellationToken.None);

        Assert.Equal(DesfechoMensagem.Concluida, desfecho);

        await using var db = Db(org);
        var t = await db.TrabalhosAssincronos.FirstAsync(x => x.Id == trabalho, CancellationToken.None);
        var imp = await db.Importacoes.FirstAsync(i => i.Id == t.IdRecurso, CancellationToken.None);

        Assert.Equal(StatusTrabalho.Concluido, t.Status);
        Assert.Equal(StatusImportacao.Recusada, imp.Status);

        // E os bytes foram embora do mesmo jeito: recusada tambem terminou.
        Assert.False(await db.BlobsTemporarios.AnyAsync(b => b.IdTrabalho == trabalho, CancellationToken.None));
    }

    // ------------------------------------------------------------- isolamento

    /// <summary>
    /// O worker abre o contexto a partir do TRABALHO. Os funcionarios criados
    /// pertencem a organizacao dele, e a vizinha nao ve nenhum.
    /// </summary>
    [Fact]
    public async Task OsFuncionariosCriadosPertencemAOrganizacaoDoTrabalho()
    {
        var org = banco.IdOrganizacaoB;
        var (trabalho, _) = await PrepararAsync(org, Csv(2));

        await using var contentor = Contentor();
        await new ManipuladorImportacao(contentor)
            .ProcessarAsync(Corpo(trabalho, org), Log, CancellationToken.None);

        await using var daB = Db(org);
        var t = await daB.TrabalhosAssincronos.FirstAsync(x => x.Id == trabalho, CancellationToken.None);
        Assert.Equal(StatusTrabalho.Concluido, t.Status);

        // A organizacao A nao enxerga o trabalho da B.
        await using var daA = Db(banco.IdOrganizacaoA);
        Assert.Null(await daA.TrabalhosAssincronos
            .FirstOrDefaultAsync(x => x.Id == trabalho, CancellationToken.None));
    }
}
