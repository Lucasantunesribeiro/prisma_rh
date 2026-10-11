using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// A folha fechada continua imutavel quando operacoes chegam AO MESMO TEMPO.
///
/// O teste sequencial ("fecha, depois recalcula, leva 409") ja existia e nao
/// prova nada sobre a corrida: o perigo e a requisicao que LEU a folha aberta
/// antes do fechamento e grava depois dele. Aqui a ordem nao e sorteada - e
/// escolhida pelo teste:
///
/// - <see cref="PortaoDoSalvar"/> estaciona uma requisicao no instante entre
///   "carreguei e apliquei o dominio" e "gravei";
/// - uma transacao do proprio teste segura a linha da folha com
///   <c>FOR UPDATE</c>, como uma requisicao concorrente faria;
/// - a espera e por CONDICAO observada no PostgreSQL (alguem na fila da trava,
///   em <c>pg_stat_activity</c>), com prazo maximo - nunca um sleep torcendo
///   para a ordem sair certa.
///
/// Toda assercao confere o que ficou GRAVADO - situacao, versao, totais,
/// lancamentos e a trilha de auditoria - e nao so o codigo HTTP.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class FolhaConcorrenciaTestes : IDisposable
{
    private readonly BancoPostgresFixture _banco;
    private readonly PortaoDoSalvar _portao = new();
    private readonly FabricaApiIsolada _fabrica;

    public FolhaConcorrenciaTestes(BancoPostgresFixture banco)
    {
        _banco = banco;
        _fabrica = new FabricaApiIsolada(
            banco.StringConexao,
            servicosExtras: s => s.ConfigureDbContext<PrismaRhDbContext>(o => o.AddInterceptors(_portao)));
    }

    public void Dispose()
    {
        _portao.Liberar();
        _fabrica.Dispose();
    }

    private static readonly TimeSpan Prazo = PortaoDoSalvar.Prazo;

    private sealed record Identificado(Guid Id);

    private sealed record RubricaItem(Guid Id, string Codigo, bool Ativa);

    private sealed record HoleriteResumo(Guid Id, string Matricula, decimal Liquido);

    private sealed record FolhaResumo(Guid Id, string Situacao, int VersaoCalculo, decimal TotalLiquido);

    private sealed record FolhaDetalhe(FolhaResumo Folha, List<HoleriteResumo> Funcionarios);

    /// <summary>O que esta GRAVADO da folha - lido por um contexto proprio, fora da API.</summary>
    private sealed record Retrato(
        SituacaoFolha Situacao,
        int Versao,
        decimal Proventos,
        decimal Descontos,
        decimal Liquido,
        IReadOnlyList<Guid> Lancamentos,
        IReadOnlyList<string?> Fechamentos);

    /// <summary>Uma folha calculada, com um lancamento manual de comissao.</summary>
    private sealed record Cenario(
        HttpClient Admin, Guid IdFolha, Guid IdHolerite, Guid IdComissao,
        Guid IdRubricaComissao, Guid IdRubricaInformativa);

    private static int _sequencia;

    // ------------------------------------------------------------------ apoio

    private PrismaRhDbContext Contexto(Guid idOrganizacao)
    {
        var opcoes = new DbContextOptionsBuilder<PrismaRhDbContext>()
            .UseNpgsql(_banco.StringConexao)
            .Options;

        return new PrismaRhDbContext(opcoes, new ContextoFixo(idOrganizacao));
    }

    private sealed class ContextoFixo(Guid idOrganizacao) : IContextoUsuario
    {
        public bool EstaAutenticado => true;
        public Guid IdUsuario => Guid.Empty;
        public Guid IdOrganizacao => idOrganizacao;
        public Perfil Perfil => Perfil.AdministradorEmpresa;
    }

    private async Task<Retrato> RetratoAsync(Guid idFolha)
    {
        await using var db = Contexto(_banco.IdOrganizacaoA);

        var folha = await db.Folhas.AsNoTracking().SingleAsync(f => f.Id == idFolha);

        var lancamentos = await db.LancamentosFolha
            .AsNoTracking()
            .Join(db.FolhasFuncionario, l => l.IdFolhaFuncionario, f => f.Id, (l, f) => new { l.Id, f.IdFolha })
            .Where(x => x.IdFolha == idFolha)
            .Select(x => x.Id)
            .OrderBy(x => x)
            .ToListAsync();

        var fechamentos = await db.EventosAuditoria
            .AsNoTracking()
            .Where(e => e.IdEntidade == idFolha && e.Acao == AcaoAuditada.FolhaFechada)
            .Select(e => e.Contexto)
            .ToListAsync();

        return new Retrato(
            folha.Situacao, folha.VersaoCalculo,
            folha.TotalProventos, folha.TotalDescontos, folha.TotalLiquido,
            lancamentos, fechamentos);
    }

    /// <summary>
    /// Termina quando ALGUMA sessao do banco esta parada esperando uma trava.
    /// Os testes desta colecao rodam em sequencia, entao a unica espera
    /// possivel e a provocada pelo proprio teste.
    /// </summary>
    private async Task EsperarAlguemNaFilaDaTravaAsync()
    {
        var limite = DateTime.UtcNow + Prazo;

        await using var conexao = new NpgsqlConnection(_banco.StringConexao);
        await conexao.OpenAsync();

        while (true)
        {
            await using var comando = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity " +
                "WHERE wait_event_type = 'Lock' AND datname = current_database()", conexao);

            if ((long)(await comando.ExecuteScalarAsync())! > 0)
            {
                return;
            }

            if (DateTime.UtcNow > limite)
            {
                throw new TimeoutException("Nenhuma sessao ficou esperando a trava da folha.");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>
    /// Exige que <paramref name="operacao"/> fique PARADA na trava, e nao
    /// termine antes. Se ela terminar primeiro, nao havia nada protegendo a
    /// folha - e e isso que a falha diz.
    /// </summary>
    private async Task ExigirQueEspereATravaAsync(Task<HttpResponseMessage> operacao, string quem)
    {
        var esperando = EsperarAlguemNaFilaDaTravaAsync();
        var primeiro = await Task.WhenAny(operacao, esperando);

        if (primeiro == operacao)
        {
            var resposta = await operacao;
            Assert.Fail(
                $"{quem} terminou ({(int)resposta.StatusCode}) sem esperar a trava: " +
                "nada impediu que ela agisse sobre a folha ao mesmo tempo que a outra operacao.");
        }

        await esperando;
    }

    private static async Task<Guid> RubricaAsync(HttpClient admin, string codigo, string nome, string tipo, string estrategia)
    {
        using var criacao = await admin.PostAsJsonAsync("/api/rubricas", new { codigo, nome, tipo, estrategia });

        if (criacao.StatusCode == HttpStatusCode.Created)
        {
            return (await criacao.Content.ReadFromJsonAsync<Identificado>())!.Id;
        }

        Assert.Equal(HttpStatusCode.Conflict, criacao.StatusCode);

        var existentes = await admin.PaginaDe<RubricaItem>("/api/rubricas");
        return existentes.First(r => r.Codigo == codigo && r.Ativa).Id;
    }

    /// <summary>
    /// Folha mensal da empresa A, numa competencia so deste teste, calculada e
    /// com um lancamento manual de comissao de 450.
    /// </summary>
    private async Task<Cenario> CenarioAsync()
    {
        var n = Interlocked.Increment(ref _sequencia);
        var sufixo = n.ToString("D3", CultureInfo.InvariantCulture);
        var competencia = $"{(n - 1) % 12 + 1:D2}/{2061 + (n - 1) / 12}";

        var admin = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        await RubricaAsync(admin, "SAL", "Salario base", "Provento", "SalarioBaseProporcional");
        var idComissao = await RubricaAsync(admin, "COM", "Comissao", "Provento", "ValorInformado");
        var idInformativa = await RubricaAsync(admin, "CINF", "Informativo de conferencia", "Informativo", "ValorInformado");

        using var cargo = await admin.PostAsJsonAsync("/api/cargos", new { codigo = $"CC{sufixo}", nome = $"Cargo concorrencia {sufixo}" });
        cargo.EnsureSuccessStatusCode();
        var idCargo = (await cargo.Content.ReadFromJsonAsync<Identificado>())!.Id;

        using var pessoa = await admin.PostAsJsonAsync("/api/funcionarios", new
        {
            nome = $"Concorrencia Pessoa {sufixo}",
            cpf = BancoPostgresFixture.CpfDeTeste(130_000 + n),
            dataNascimento = "1990-05-20",
        });
        pessoa.EnsureSuccessStatusCode();
        var idFuncionario = (await pessoa.Content.ReadFromJsonAsync<Identificado>())!.Id;

        using var contrato = await admin.PostAsJsonAsync($"/api/funcionarios/{idFuncionario}/contratos", new
        {
            idEmpresa = _banco.IdEmpresaA,
            matricula = $"CC{sufixo}",
            dataAdmissao = "2025-03-01",
            salarioInicial = 3000m,
            idCargo,
            idEstabelecimento = _banco.IdEstabelecimentoA,
            jornadaMensalHoras = 220,
        });
        contrato.EnsureSuccessStatusCode();

        using var abertura = await admin.PostAsJsonAsync("/api/folhas", new { idEmpresa = _banco.IdEmpresaA, competencia });
        abertura.EnsureSuccessStatusCode();
        var idFolha = (await abertura.Content.ReadFromJsonAsync<Identificado>())!.Id;

        using var calculo = await admin.PostAsync($"/api/folhas/{idFolha}/calcular", null);
        calculo.EnsureSuccessStatusCode();
        var detalhe = (await calculo.Content.ReadFromJsonAsync<FolhaDetalhe>())!;
        var idHolerite = detalhe.Funcionarios.Single(f => f.Matricula == $"CC{sufixo}").Id;

        using var lancamento = await admin.PostAsJsonAsync(
            $"/api/folhas/{idFolha}/funcionarios/{idHolerite}/lancamentos",
            new { idRubrica = idComissao, valor = 450m, referencia = (string?)null });
        lancamento.EnsureSuccessStatusCode();

        await using var db = Contexto(_banco.IdOrganizacaoA);
        var idLancamentoComissao = await db.LancamentosFolha
            .Where(l => l.IdFolhaFuncionario == idHolerite && l.CodigoRubrica == "COM")
            .Select(l => l.Id)
            .SingleAsync();

        return new Cenario(admin, idFolha, idHolerite, idLancamentoComissao, idComissao, idInformativa);
    }

    /// <summary>As operacoes que alteram uma folha aberta.</summary>
    private static Task<HttpResponseMessage> Operar(Cenario c, string operacao) => operacao switch
    {
        "lancar" => c.Admin.PostAsJsonAsync(
            $"/api/folhas/{c.IdFolha}/funcionarios/{c.IdHolerite}/lancamentos",
            new { idRubrica = c.IdRubricaComissao, valor = 125m, referencia = (string?)null }),

        // Lancamento que NAO muda total nenhum: informativo de valor zero. Um
        // token de versao que so andasse com o UPDATE da folha deixaria este
        // passar - a trava e tomada antes de qualquer leitura, e por isso nao.
        "lancar-sem-mudar-total" => c.Admin.PostAsJsonAsync(
            $"/api/folhas/{c.IdFolha}/funcionarios/{c.IdHolerite}/lancamentos",
            new { idRubrica = c.IdRubricaInformativa, valor = 0m, referencia = (string?)null }),

        "remover" => c.Admin.DeleteAsync(
            $"/api/folhas/{c.IdFolha}/funcionarios/{c.IdHolerite}/lancamentos/{c.IdComissao}"),

        "calcular" => c.Admin.PostAsync($"/api/folhas/{c.IdFolha}/calcular", null),

        "fechar" => c.Admin.PostAsync($"/api/folhas/{c.IdFolha}/fechar", null),

        _ => throw new ArgumentOutOfRangeException(nameof(operacao), operacao, null),
    };

    private static string ContextoEsperadoDoFechamento(Retrato r) =>
        FormattableString.Invariant($"liquido={r.Liquido:0.00};versao={r.Versao}");

    // ------------------------------------------------- chegou depois do fechar

    [Theory]
    [InlineData("lancar")]
    [InlineData("lancar-sem-mudar-total")]
    [InlineData("remover")]
    [InlineData("calcular")]
    public async Task OperacaoQueEsperavaAFolha_EncontraElaFechada_ERecusada_ENadaMuda(string operacao)
    {
        var c = await CenarioAsync();

        // Outra "requisicao" - aqui, a transacao do teste - pega a folha
        // primeiro e a fecha, sem confirmar ainda.
        await using var outra = Contexto(_banco.IdOrganizacaoA);
        await using var transacao = await outra.Database.BeginTransactionAsync();
        await outra.Database.ExecuteSqlAsync($"SELECT 1 FROM folhas_pagamento WHERE id = {c.IdFolha} FOR UPDATE");
        var folha = await outra.Folhas.Include(f => f.Funcionarios).SingleAsync(f => f.Id == c.IdFolha);
        folha.Fechar(DateTimeOffset.UtcNow);
        await outra.SaveChangesAsync();

        var antes = await RetratoAsync(c.IdFolha);

        var tentativa = Operar(c, operacao);

        try
        {
            await ExigirQueEspereATravaAsync(tentativa, operacao);
        }
        finally
        {
            await transacao.CommitAsync();
        }

        using var resposta = await tentativa.WaitAsync(Prazo);
        var depois = await RetratoAsync(c.IdFolha);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Contains("fechada", await resposta.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        Assert.Equal(SituacaoFolha.Fechada, depois.Situacao);
        Assert.Equal(antes.Versao, depois.Versao);
        Assert.Equal(antes.Proventos, depois.Proventos);
        Assert.Equal(antes.Descontos, depois.Descontos);
        Assert.Equal(antes.Liquido, depois.Liquido);
        Assert.Equal(antes.Lancamentos, depois.Lancamentos);
    }

    // ---------------------------------------- fechar chegou durante a alteracao

    [Theory]
    [InlineData("lancar")]
    [InlineData("lancar-sem-mudar-total")]
    [InlineData("remover")]
    [InlineData("calcular")]
    public async Task FechamentoDuranteUmaAlteracao_Espera_EFechaOQueFoiGravado(string operacao)
    {
        var c = await CenarioAsync();
        var antes = await RetratoAsync(c.IdFolha);

        var estacionada = _portao.ArmarPausa();
        var alteracao = Operar(c, operacao);
        await estacionada;

        var fechamento = Operar(c, "fechar");

        try
        {
            await ExigirQueEspereATravaAsync(fechamento, "O fechamento");
        }
        finally
        {
            _portao.Liberar();
        }

        using var respostaAlteracao = await alteracao.WaitAsync(Prazo);
        using var respostaFechamento = await fechamento.WaitAsync(Prazo);

        Assert.True(respostaAlteracao.IsSuccessStatusCode, $"{operacao}: {(int)respostaAlteracao.StatusCode}");
        Assert.True(respostaFechamento.IsSuccessStatusCode, $"fechar: {(int)respostaFechamento.StatusCode}");

        var depois = await RetratoAsync(c.IdFolha);

        Assert.Equal(SituacaoFolha.Fechada, depois.Situacao);

        // A alteracao veio ANTES do fechamento, e o fechamento registrou
        // exatamente o que ficou gravado: a auditoria e o banco contam a mesma
        // historia.
        var fechamentoAuditado = Assert.Single(depois.Fechamentos);
        Assert.StartsWith(ContextoEsperadoDoFechamento(depois), fechamentoAuditado);

        switch (operacao)
        {
            case "lancar":
                Assert.Equal(antes.Liquido + 125m, depois.Liquido);
                break;
            case "lancar-sem-mudar-total":
                Assert.Equal(antes.Lancamentos.Count + 1, depois.Lancamentos.Count);
                break;
            case "remover":
                Assert.Equal(antes.Liquido - 450m, depois.Liquido);
                Assert.DoesNotContain(c.IdComissao, depois.Lancamentos);
                break;
            case "calcular":
                Assert.Equal(antes.Versao + 1, depois.Versao);
                break;
        }
    }

    // --------------------------------------------------- duas operacoes iguais

    [Fact]
    public async Task DoisFechamentosSimultaneos_UmVence_EAAuditoriaTemUmSo()
    {
        var c = await CenarioAsync();

        var estacionado = _portao.ArmarPausa();
        var primeiro = Operar(c, "fechar");
        await estacionado;

        var segundo = Operar(c, "fechar");

        try
        {
            await ExigirQueEspereATravaAsync(segundo, "O segundo fechamento");
        }
        finally
        {
            _portao.Liberar();
        }

        using var r1 = await primeiro.WaitAsync(Prazo);
        using var r2 = await segundo.WaitAsync(Prazo);

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);

        var depois = await RetratoAsync(c.IdFolha);
        Assert.Equal(SituacaoFolha.Fechada, depois.Situacao);
        Assert.StartsWith(ContextoEsperadoDoFechamento(depois), Assert.Single(depois.Fechamentos));
    }

    [Fact]
    public async Task DuasEdicoesSimultaneas_AmbasFicam_SemPerdaSilenciosa()
    {
        var c = await CenarioAsync();
        var antes = await RetratoAsync(c.IdFolha);

        var estacionada = _portao.ArmarPausa();
        var primeira = Operar(c, "lancar");
        await estacionada;

        var segunda = Operar(c, "lancar");

        try
        {
            await ExigirQueEspereATravaAsync(segunda, "A segunda edicao");
        }
        finally
        {
            _portao.Liberar();
        }

        using var r1 = await primeira.WaitAsync(Prazo);
        using var r2 = await segunda.WaitAsync(Prazo);

        Assert.True(r1.IsSuccessStatusCode, $"primeira: {(int)r1.StatusCode}");
        Assert.True(r2.IsSuccessStatusCode, $"segunda: {(int)r2.StatusCode}");

        var depois = await RetratoAsync(c.IdFolha);

        // Duas comissoes de 125: os totais refletem as DUAS, e nao a ultima
        // que gravou por cima da outra.
        Assert.Equal(antes.Lancamentos.Count + 2, depois.Lancamentos.Count);
        Assert.Equal(antes.Liquido + 250m, depois.Liquido);
        Assert.Equal(antes.Proventos + 250m, depois.Proventos);
    }

    // ---------------------------------------------------------- atomicidade

    [Theory]
    [InlineData("fechar")]
    [InlineData("lancar")]
    public async Task FalhaDepoisDeGravar_DesfazTudoJunto_ELiberaAFolha(string operacao)
    {
        var c = await CenarioAsync();
        var antes = await RetratoAsync(c.IdFolha);

        // O SQL da operacao chega ao banco; so depois a gravacao falha. O que
        // tiver sido gravado tem de sair junto: lancamento, totais e auditoria.
        _portao.ArmarFalhaDepoisDeGravar();

        using var resposta = await Operar(c, operacao).WaitAsync(Prazo);

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);

        var depois = await RetratoAsync(c.IdFolha);

        Assert.Equal(SituacaoFolha.Calculada, depois.Situacao);
        Assert.Equal(antes.Versao, depois.Versao);
        Assert.Equal(antes.Liquido, depois.Liquido);
        Assert.Equal(antes.Lancamentos, depois.Lancamentos);
        Assert.Empty(depois.Fechamentos);

        // E a folha nao ficou presa: a proxima operacao passa direto.
        using var seguinte = await Operar(c, "fechar").WaitAsync(Prazo);
        Assert.Equal(HttpStatusCode.OK, seguinte.StatusCode);
    }

    // --------------------------------------------------------- trava presa

    [Fact]
    public async Task TravaPresaAlemDoPrazo_Devolve409Controlado_ENadaMuda()
    {
        var c = await CenarioAsync();
        var antes = await RetratoAsync(c.IdFolha);

        await using var outra = Contexto(_banco.IdOrganizacaoA);
        await using var transacao = await outra.Database.BeginTransactionAsync();
        await outra.Database.ExecuteSqlAsync($"SELECT 1 FROM folhas_pagamento WHERE id = {c.IdFolha} FOR UPDATE");

        using var resposta = await Operar(c, "lancar").WaitAsync(Prazo);

        await transacao.RollbackAsync();

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Contains("recarregue", await resposta.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var depois = await RetratoAsync(c.IdFolha);
        Assert.Equal(antes.Liquido, depois.Liquido);
        Assert.Equal(antes.Lancamentos, depois.Lancamentos);
    }

    // ------------------------------------------------------- outra organizacao

    [Fact]
    public async Task OutraOrganizacao_Recebe404_SemSequerEsperarATrava()
    {
        var c = await CenarioAsync();
        var vizinha = await _fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminB);

        // A folha esta travada por outra transacao. Se a requisicao da vizinha
        // tentasse travar a linha alheia, ficaria na fila e sairia com o 409
        // do prazo - o 404 imediato prova que ela nem chegou la.
        await using var outra = Contexto(_banco.IdOrganizacaoA);
        await using var transacao = await outra.Database.BeginTransactionAsync();
        await outra.Database.ExecuteSqlAsync($"SELECT 1 FROM folhas_pagamento WHERE id = {c.IdFolha} FOR UPDATE");

        using var resposta = await vizinha.PostAsync($"/api/folhas/{c.IdFolha}/fechar", null).WaitAsync(Prazo);

        await transacao.RollbackAsync();

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Equal(SituacaoFolha.Calculada, (await RetratoAsync(c.IdFolha)).Situacao);
    }
}
