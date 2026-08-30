using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Dominio.Importacao;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Isolamento multiempresa das tabelas de importacao (Fase 5, etapa 2), contra
/// PostgreSQL real.
///
/// **Este teste nao passa pela API de proposito.** A etapa 2 nao tem rota - ela
/// entra na etapa 3 -, e o `CLAUDE.md secao 24.5` e explicito: o filtro global
/// protege consultas, e "toda funcionalidade nova que manipule dado de tenant
/// entra acompanhada de teste de isolamento contra PostgreSQL real". Esperar a
/// rota existir para so entao testar o filtro deixaria a tabela sem prova
/// justamente na etapa em que ela nasce.
///
/// Contra Testcontainers, e nao EF InMemory: o filtro global vira `WHERE` no
/// SQL, e banco falso nao gera SQL - um teste ali provaria nada.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class ImportacaoIsolamentoTestes(BancoPostgresFixture banco)
{
    /// <summary>
    /// Um contexto que enxerga o banco como a organizacao informada.
    ///
    /// E o mesmo mecanismo da aplicacao: o `IdOrganizacao` vem do
    /// IContextoUsuario, e o filtro global do DbContext o usa. Trocar de
    /// organizacao aqui e trocar de usuario logado la.
    /// </summary>
    private sealed class ContextoFalso(Guid idOrganizacao, Guid idUsuario) : IContextoUsuario
    {
        public bool EstaAutenticado => true;

        public Guid IdUsuario => idUsuario;

        public Guid IdOrganizacao => idOrganizacao;

        public Perfil Perfil => Perfil.AdministradorEmpresa;
    }

    private ServiceProvider Provedor(Guid idOrganizacao, Guid idUsuario)
    {
        var servicos = new ServiceCollection();

        servicos.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        servicos.AddSingleton<IContextoUsuario>(new ContextoFalso(idOrganizacao, idUsuario));
        servicos.AddDbContext<PrismaRhDbContext>(o => o.UseNpgsql(banco.StringConexao));

        return servicos.BuildServiceProvider();
    }

    private async Task<Guid> UsuarioDeAsync(Guid idOrganizacao)
    {
        await using var provedor = Provedor(idOrganizacao, Guid.Empty);
        using var escopo = provedor.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        return await db.Usuarios.Select(u => u.Id).FirstAsync();
    }

    private async Task<Guid> ImportacaoDeAsync(Guid idOrganizacao, string nome)
    {
        var idUsuario = await UsuarioDeAsync(idOrganizacao);

        await using var provedor = Provedor(idOrganizacao, idUsuario);
        using var escopo = provedor.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        var importacao = new Importacao(
            idOrganizacao, idUsuario, nome, FormatoImportacao.Csv,
            2048, Importacao.CalcularHash(System.Text.Encoding.UTF8.GetBytes(nome)),
            DateTimeOffset.UtcNow);

        importacao.Registrar(2, []);
        importacao.Registrar(3, ["CPF invalido: '123'"]);

        db.Importacoes.Add(importacao);
        await db.SaveChangesAsync();

        return importacao.Id;
    }

    // ------------------------------------------------------------ isolamento

    [Fact]
    public async Task ImportacaoDaVizinha_NAOAparece()
    {
        var idA = await ImportacaoDeAsync(banco.IdOrganizacaoC, "folha-da-c.csv");

        var idUsuarioD = await UsuarioDeAsync(banco.IdOrganizacaoD);

        await using var provedor = Provedor(banco.IdOrganizacaoD, idUsuarioD);
        using var escopo = provedor.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        Assert.Null(await db.Importacoes.SingleOrDefaultAsync(i => i.Id == idA));
        Assert.DoesNotContain(await db.Importacoes.ToListAsync(), i => i.Id == idA);
    }

    [Fact]
    public async Task AsLINHASDaVizinha_TambemNAOAparecem()
    {
        var idA = await ImportacaoDeAsync(banco.IdOrganizacaoC, "linhas-da-c.csv");

        var idUsuarioD = await UsuarioDeAsync(banco.IdOrganizacaoD);

        await using var provedor = Provedor(banco.IdOrganizacaoD, idUsuarioD);
        using var escopo = provedor.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        // O ponto deste teste: consultar LinhasImportacao DIRETO, sem passar
        // pela Importacao. Se so a raiz tivesse filtro, este caminho alcancaria
        // o relatorio de erros da organizacao vizinha - e relatorio de erro de
        // outra empresa e dado de outra empresa.
        Assert.Empty(await db.LinhasImportacao.Where(l => l.IdImportacao == idA).ToListAsync());
    }

    [Fact]
    public async Task SemUsuarioAutenticado_NaoSeVeNADA()
    {
        await ImportacaoDeAsync(banco.IdOrganizacaoC, "fail-closed.csv");

        // Fail closed (`CLAUDE.md secao 24.5`): sem usuario o IdOrganizacao e
        // Guid.Empty, que nao casa com nenhuma linha. O padrao e nao ver, e nao
        // ver tudo.
        await using var provedor = Provedor(Guid.Empty, Guid.Empty);
        using var escopo = provedor.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        Assert.Empty(await db.Importacoes.ToListAsync());
        Assert.Empty(await db.LinhasImportacao.ToListAsync());
    }

    [Fact]
    public async Task CadaOrganizacaoVEAPROPRIA()
    {
        var idC = await ImportacaoDeAsync(banco.IdOrganizacaoC, "propria-c.csv");
        var idD = await ImportacaoDeAsync(banco.IdOrganizacaoD, "propria-d.csv");

        // O outro lado da moeda: um filtro que escondesse tudo tambem passaria
        // nos testes acima. Este prova que ele esconde o do vizinho e mostra o
        // seu.
        await using var provedorC = Provedor(
            banco.IdOrganizacaoC, await UsuarioDeAsync(banco.IdOrganizacaoC));

        using var escopoC = provedorC.CreateScope();
        var dbC = escopoC.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        Assert.NotNull(await dbC.Importacoes.SingleOrDefaultAsync(i => i.Id == idC));
        Assert.Null(await dbC.Importacoes.SingleOrDefaultAsync(i => i.Id == idD));
    }

    // ------------------------------------------------------- persistencia

    [Fact]
    public async Task OQueFOIGravado_VoltaIgual()
    {
        var idUsuario = await UsuarioDeAsync(banco.IdOrganizacaoC);
        var id = await ImportacaoDeAsync(banco.IdOrganizacaoC, "ida-e-volta.csv");

        await using var provedor = Provedor(banco.IdOrganizacaoC, idUsuario);
        using var escopo = provedor.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        var lida = await db.Importacoes
            .Include(i => i.Linhas)
            .SingleAsync(i => i.Id == id);

        Assert.Equal("ida-e-volta.csv", lida.NomeOriginalArquivo);
        Assert.Equal(FormatoImportacao.Csv, lida.Formato);
        Assert.Equal(2048, lida.TamanhoBytes);
        Assert.Equal(Importacao.TamanhoHash, lida.HashSha256.Length);
        Assert.Equal(StatusImportacao.Analisada, lida.Status);

        Assert.Equal(2, lida.TotalLinhas);
        Assert.Equal(1, lida.LinhasValidas);
        Assert.Equal(1, lida.LinhasComErro);

        // O array de texto do PostgreSQL volta com o erro inteiro.
        var comErro = lida.Linhas.Single(l => l.Situacao == SituacaoLinha.ComErro);
        Assert.Equal("CPF invalido: '123'", comErro.Erros.Single());

        // E a linha valida volta SEM erro nenhum, e nao com uma string vazia.
        Assert.Empty(lida.Linhas.Single(l => l.Situacao == SituacaoLinha.Valida).Erros);
    }

    [Fact]
    public async Task ApagarAImportacao_LEVAAsLinhasJunto()
    {
        var idUsuario = await UsuarioDeAsync(banco.IdOrganizacaoC);
        var id = await ImportacaoDeAsync(banco.IdOrganizacaoC, "cascata.csv");

        await using var provedor = Provedor(banco.IdOrganizacaoC, idUsuario);
        using var escopo = provedor.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        db.Importacoes.Remove(await db.Importacoes.SingleAsync(i => i.Id == id));
        await db.SaveChangesAsync();

        // Cascata da importacao para as linhas: linha orfa nao significa nada
        // sozinha, porque ela nao guarda valor algum - so numero e erro.
        Assert.Empty(await db.LinhasImportacao.Where(l => l.IdImportacao == id).ToListAsync());
    }

    [Fact]
    public async Task ContadorContraditorio_ERECUSADOPeloBANCO()
    {
        var idUsuario = await UsuarioDeAsync(banco.IdOrganizacaoC);

        await using var provedor = Provedor(banco.IdOrganizacaoC, idUsuario);
        using var escopo = provedor.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();

        var id = await ImportacaoDeAsync(banco.IdOrganizacaoC, "contadores.csv");

        // A entidade ja garante isso em memoria. A check constraint garante
        // contra qualquer caminho que NAO passe pelo dominio - um script de
        // correcao, por exemplo. A garantia final nao e o C#.
        var erro = await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Database.ExecuteSqlRawAsync(
                "update importacoes set linhas_validas = 99 where id = {0}", id));

        Assert.Contains("ck_importacoes_contadores", erro.ToString());
    }
}
