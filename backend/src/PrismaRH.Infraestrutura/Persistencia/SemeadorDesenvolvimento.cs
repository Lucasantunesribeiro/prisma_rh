using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Dominio.Parametros;
using PrismaRH.Dominio.Pessoas;

namespace PrismaRH.Infraestrutura.Persistencia;

/// <summary>
/// Dados ficticios para desenvolvimento e demonstracao. NUNCA roda fora de
/// Development.
///
/// Cria DUAS organizacoes de proposito: com uma so, um furo no isolamento
/// multiempresa passaria despercebido, porque nao haveria vizinho para invadir.
///
/// A senha vem de PRISMARH_SEED_SENHA. Nao ha senha no codigo.
/// </summary>
public static class SemeadorDesenvolvimento
{
    public const string VariavelSenha = "PRISMARH_SEED_SENHA";

    public static async Task SemearAsync(IServiceProvider servicos, CancellationToken ct = default)
    {
        using var escopo = servicos.CreateScope();

        var contexto = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();
        var hasheador = escopo.ServiceProvider.GetRequiredService<IHasheadorSenha>();
        var relogio = escopo.ServiceProvider.GetRequiredService<IRelogio>();
        var log = escopo.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(SemeadorDesenvolvimento));

        // O banco pode estar fora. A aplicacao PRECISA subir mesmo assim: e o
        // /health que reporta o estado do banco, e ele so consegue reportar se
        // a API estiver de pe.
        if (!await contexto.Database.CanConnectAsync(ct))
        {
            log.LogWarning("Semeadura ignorada: banco indisponivel. Verifique em /health.");
            return;
        }

        var senha = Environment.GetEnvironmentVariable(VariavelSenha);

        if (string.IsNullOrWhiteSpace(senha))
        {
            log.LogWarning(
                "Semeadura ignorada: defina {Variavel} para criar os usuarios de demonstracao.",
                VariavelSenha);
            return;
        }

        var agora = relogio.Agora;

        // Idempotente POR SECAO, e nao tudo-ou-nada.
        //
        // Um "se ja existe organizacao, pule tudo" faria um banco criado numa
        // fase anterior nunca receber os dados das fases seguintes: a demo
        // ficaria incompleta e ninguem entenderia por que.
        var prisma = await contexto.Organizacoes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Nome.StartsWith("Prisma"), ct);

        if (prisma is null)
        {
            prisma = await SemearIdentidadeAsync(contexto, hasheador, senha, agora, ct);
            log.LogInformation("Semeadura: identidade e empresas criadas.");
        }

        if (!await contexto.Funcionarios.IgnoreQueryFilters().AnyAsync(ct))
        {
            var quantidade = await SemearCadastroFuncionalAsync(contexto, prisma, agora, ct);
            log.LogInformation("Semeadura: {Quantidade} funcionarios com contrato criados.", quantidade);
        }

        // Parametro legal e independente das organizacoes e das fases: entra
        // antes da folha, e roda mesmo em banco que ja tinha rubricas.
        if (!await contexto.TabelasInss.AnyAsync(ct))
        {
            await SemearTabelaInssAsync(contexto, agora, ct);
            log.LogInformation("Semeadura: tabela de INSS vigente desde 01/01/2026 cadastrada.");
        }

        if (!await contexto.TabelasFgts.AnyAsync(ct))
        {
            // Aliquota de 8% sobre a remuneracao, vigente desde a lei que
            // criou o regime atual (CLAUDE.md secao 29).
            contexto.TabelasFgts.Add(new TabelaFgts(
                new DateOnly(1990, 5, 11),
                0.08m,
                "Lei n. 8.036, de 11/05/1990, art. 15 - deposito mensal de 8% da remuneracao",
                agora));

            await contexto.SaveChangesAsync(ct);
            log.LogInformation("Semeadura: aliquota de FGTS cadastrada.");
        }

        if (await contexto.Rubricas.IgnoreQueryFilters().AnyAsync(ct))
        {
            // O catalogo ja existe, mas pode ter nascido numa fase anterior.
            // As rubricas que as fases 4B e 4C introduziram sao acrescentadas
            // aqui, uma a uma - sem isso, um banco criado na Fase 3 nunca
            // passaria a calcular INSS nem FGTS, e ninguem entenderia por que
            // a demo esta incompleta.
            var acrescentadas = await CompletarCatalogoAsync(contexto, prisma, agora, ct);

            log.LogInformation(
                acrescentadas.Length == 0
                    ? "Semeadura: folha ja existia, nada a fazer."
                    : "Semeadura: rubricas acrescentadas ao catalogo existente: {Rubricas}.",
                string.Join(", ", acrescentadas));

            return;
        }

        var competencias = await SemearFolhaAsync(contexto, prisma, agora, ct);
        log.LogInformation("Semeadura: folhas de {Competencias} criadas.", competencias);
    }

    // -----------------------------------------------------------------------
    // Fase 4B: parametro legal federal
    // -----------------------------------------------------------------------

    /// <summary>
    /// A tabela de INSS vigente a partir de 01/01/2026.
    ///
    /// Os numeros entram como DADO VERSIONADO, nunca na formula: quando sair a
    /// tabela de 2027, basta cadastrar outra vigencia e o algoritmo
    /// progressivo permanece intacto (CLAUDE.md secao 4.1).
    ///
    /// Aliquotas em FRACAO: 7,5% e 0.075.
    /// </summary>
    private static async Task SemearTabelaInssAsync(
        PrismaRhDbContext contexto,
        DateTimeOffset agora,
        CancellationToken ct)
    {
        var tabela = new TabelaInss(
            new DateOnly(2026, 1, 1),
            "Portaria Interministerial MPS/MF n. 13, de 09/01/2026, Anexo II - "
            + "tabela de contribuicao dos segurados empregado, empregado domestico e trabalhador avulso",
            [
                (1621.00m, 0.075m),
                (2902.84m, 0.09m),
                (4354.27m, 0.12m),
                (8475.55m, 0.14m),
            ],
            agora);

        contexto.TabelasInss.Add(tabela);
        await contexto.SaveChangesAsync(ct);
    }

    // -----------------------------------------------------------------------
    // Fase 1: organizacoes, usuarios, empresas e estabelecimentos
    // -----------------------------------------------------------------------
    private static async Task<Organizacao> SemearIdentidadeAsync(
        PrismaRhDbContext contexto,
        IHasheadorSenha hasheador,
        string senha,
        DateTimeOffset agora,
        CancellationToken ct)
    {
        var hash = hasheador.Gerar(senha);

        var prisma = new Organizacao("Prisma Servicos de RH Ltda.", agora);
        var horizonte = new Organizacao("Contabilidade Horizonte Ltda.", agora);
        contexto.Organizacoes.AddRange(prisma, horizonte);

        // Um usuario por perfil na organizacao principal, para dar para testar
        // autorizacao entrando com cada um.
        contexto.Usuarios.AddRange(
            new Usuario(prisma.Id, "Ana Plataforma", "plataforma@prisma.exemplo", hash, Perfil.AdministradorPlataforma, agora),
            new Usuario(prisma.Id, "Bruno Admin", "admin@prisma.exemplo", hash, Perfil.AdministradorEmpresa, agora),
            new Usuario(prisma.Id, "Carla Analista", "analista@prisma.exemplo", hash, Perfil.AnalistaRh, agora),
            new Usuario(prisma.Id, "Diego Auditor", "auditor@prisma.exemplo", hash, Perfil.Auditor, agora),
            new Usuario(prisma.Id, "Elisa Visualizadora", "visualizador@prisma.exemplo", hash, Perfil.Visualizador, agora),

            // O vizinho: existe para provar que ele NAO enxerga a Prisma.
            new Usuario(horizonte.Id, "Fabio Horizonte", "admin@horizonte.exemplo", hash, Perfil.AdministradorEmpresa, agora));

        var empresaPrisma = new Empresa(prisma.Id, "Industria Modelo S.A.", Cnpj.Criar("11222333000181"), agora, "Modelo");
        var empresaHorizonte = new Empresa(horizonte.Id, "Comercio Vizinho Ltda.", Cnpj.Criar("11444777000161"), agora, "Vizinho");
        contexto.Empresas.AddRange(empresaPrisma, empresaHorizonte);

        contexto.Estabelecimentos.AddRange(
            new Estabelecimento(prisma.Id, empresaPrisma.Id, "001", "Matriz", agora),
            new Estabelecimento(prisma.Id, empresaPrisma.Id, "002", "Filial Sul", agora),
            new Estabelecimento(horizonte.Id, empresaHorizonte.Id, "001", "Matriz Vizinha", agora));

        await contexto.SaveChangesAsync(ct);

        return prisma;
    }

    // -----------------------------------------------------------------------
    // Fase 2: cargos, funcionarios, contratos e historico
    // -----------------------------------------------------------------------
    private static async Task<int> SemearCadastroFuncionalAsync(
        PrismaRhDbContext contexto,
        Organizacao prisma,
        DateTimeOffset agora,
        CancellationToken ct)
    {
        var empresa = await contexto.Empresas.IgnoreQueryFilters()
            .FirstAsync(e => e.IdOrganizacao == prisma.Id, ct);

        var estabelecimentos = await contexto.Estabelecimentos.IgnoreQueryFilters()
            .Where(e => e.IdEmpresa == empresa.Id)
            .OrderBy(e => e.Codigo)
            .ToListAsync(ct);

        var matriz = estabelecimentos[0];
        var filial = estabelecimentos.Count > 1 ? estabelecimentos[1] : matriz;

        var cargos = new[]
        {
            new Cargo(prisma.Id, "AUX", "Auxiliar Administrativo", agora),
            new Cargo(prisma.Id, "ANA", "Analista", agora),
            new Cargo(prisma.Id, "COO", "Coordenador", agora),
        };
        contexto.Cargos.AddRange(cargos);

        var pessoas = new (string Nome, DateOnly Nascimento, int Cargo, decimal Salario, int Jornada)[]
        {
            ("Ana Beatriz Moraes", new DateOnly(1988, 3, 12), 0, 2600m, 220),
            ("Bruno Carvalho Lima", new DateOnly(1992, 7, 4), 1, 3400m, 220),
            ("Camila Ferreira Souza", new DateOnly(1995, 11, 23), 1, 3600m, 220),
            ("Diego Nogueira Alves", new DateOnly(1985, 1, 30), 2, 7200m, 220),
            ("Eduarda Pires Ramos", new DateOnly(1998, 5, 17), 0, 2450m, 180),
            ("Felipe Andrade Costa", new DateOnly(1990, 9, 8), 1, 3900m, 220),
            ("Gabriela Tavares Rocha", new DateOnly(1993, 2, 26), 1, 3750m, 220),
            ("Henrique Barros Melo", new DateOnly(1987, 12, 5), 2, 8100m, 220),
        };

        var contratos = new List<ContratoTrabalho>();

        for (var i = 0; i < pessoas.Length; i++)
        {
            var pessoa = pessoas[i];

            var funcionario = new Funcionario(
                prisma.Id, pessoa.Nome, Cpf.Criar(CpfFicticio(i + 1)), pessoa.Nascimento, agora);
            contexto.Funcionarios.Add(funcionario);

            contratos.Add(new ContratoTrabalho(
                prisma.Id,
                funcionario.Id,
                empresa.Id,
                matricula: (1000 + i).ToString(),
                dataAdmissao: new DateOnly(2025, 2 + i % 6, 1 + i % 20),
                salarioInicial: pessoa.Salario,
                cargos[pessoa.Cargo].Id,
                (i % 3 == 0 ? filial : matriz).Id,
                pessoa.Jornada,
                agora));
        }

        // Duas pessoas com historico, para a linha do tempo ter o que mostrar e
        // para a Fase 3 encontrar competencia com salario diferente do atual.
        contratos[1].RegistrarAlteracao(
            new DateOnly(2026, 3, 1), 3900m, cargos[1].Id, matriz.Id, 220,
            MotivoVigencia.AlteracaoSalarial, agora);

        contratos[3].RegistrarAlteracao(
            new DateOnly(2025, 9, 1), 7600m, cargos[2].Id, filial.Id, 220,
            MotivoVigencia.Transferencia, agora);
        contratos[3].RegistrarAlteracao(
            new DateOnly(2026, 4, 1), 8400m, cargos[2].Id, filial.Id, 220,
            MotivoVigencia.AlteracaoSalarial, agora);

        // Um desligado, para o cadastro nao parecer que so existe gente ativa.
        contratos[4].Desligar(new DateOnly(2026, 6, 30));

        contexto.ContratosTrabalho.AddRange(contratos);

        await contexto.SaveChangesAsync(ct);

        return pessoas.Length;
    }

    /// <summary>
    /// Acrescenta ao catalogo existente as rubricas calculadas que uma fase
    /// posterior introduziu.
    ///
    /// So cria o que falta, e so quando NAO ha uma ativa com a mesma
    /// estrategia - o indice unico parcial permite uma por organizacao, e
    /// tentar inserir a segunda quebraria a subida da API.
    ///
    /// Nao mexe nas folhas ja calculadas: elas continuam como foram fechadas
    /// (CLAUDE.md secao 4.3). A rubrica nova entra no proximo calculo.
    /// </summary>
    private static async Task<string[]> CompletarCatalogoAsync(
        PrismaRhDbContext contexto,
        Organizacao prisma,
        DateTimeOffset agora,
        CancellationToken ct)
    {
        var acrescentadas = new List<string>();

        // Fase 4B: desconto calculado, sem incidencia - desconto nao compoe base.
        if (!await contexto.Rubricas.IgnoreQueryFilters().AnyAsync(
                r => r.IdOrganizacao == prisma.Id && r.Ativa
                     && r.Estrategia == EstrategiaRubrica.InssProgressivo, ct))
        {
            contexto.Rubricas.Add(new Rubrica(
                prisma.Id, "INSS", "INSS sobre a folha",
                TipoRubrica.Desconto, EstrategiaRubrica.InssProgressivo, BaseCalculo.Nenhuma, agora));

            acrescentadas.Add("INSS");
        }

        // Fase 4C: informativa, porque FGTS e deposito do empregador.
        if (!await contexto.Rubricas.IgnoreQueryFilters().AnyAsync(
                r => r.IdOrganizacao == prisma.Id && r.Ativa
                     && r.Estrategia == EstrategiaRubrica.FgtsMensal, ct))
        {
            contexto.Rubricas.Add(new Rubrica(
                prisma.Id, "FGTS", "FGTS sobre a folha",
                TipoRubrica.Informativo, EstrategiaRubrica.FgtsMensal, BaseCalculo.Nenhuma, agora));

            acrescentadas.Add("FGTS");
        }

        if (acrescentadas.Count > 0)
        {
            await contexto.SaveChangesAsync(ct);
        }

        return [.. acrescentadas];
    }

    // -----------------------------------------------------------------------
    // Fase 3: rubricas e as duas primeiras folhas
    // -----------------------------------------------------------------------
    private static async Task<string> SemearFolhaAsync(
        PrismaRhDbContext contexto,
        Organizacao prisma,
        DateTimeOffset agora,
        CancellationToken ct)
    {
        var empresa = await contexto.Empresas.IgnoreQueryFilters()
            .FirstAsync(e => e.IdOrganizacao == prisma.Id, ct);

        // Incidencias com fonte, conforme CLAUDE.md secao 29.
        //
        // Salario e comissao integram o salario-de-contribuicao (Lei 8.212/91,
        // art. 28, I: "a remuneracao auferida (...), inclusive comissoes"), e
        // por consequencia compoem tambem a base de FGTS e a de IRRF.
        //
        // Vale-transporte e adiantamento sao DESCONTOS: nao compoem base
        // alguma, e a invariante em Rubrica recusaria se alguem tentasse. O
        // beneficio vale-transporte tambem nao integraria salario (Lei
        // 7.418/85, art. 2o), mas isso e outra rubrica, que nao existe aqui.
        const BaseCalculo integraTudo = BaseCalculo.Inss | BaseCalculo.Fgts | BaseCalculo.Irrf;

        var salario = new Rubrica(
            prisma.Id, "SAL", "Salario base",
            TipoRubrica.Provento, EstrategiaRubrica.SalarioBaseProporcional, integraTudo, agora);

        var comissao = new Rubrica(
            prisma.Id, "COM", "Comissao",
            TipoRubrica.Provento, EstrategiaRubrica.ValorInformado, integraTudo, agora);

        var valeTransporte = new Rubrica(
            prisma.Id, "VT", "Vale-transporte",
            TipoRubrica.Desconto, EstrategiaRubrica.ValorInformado, BaseCalculo.Nenhuma, agora);

        var adiantamento = new Rubrica(
            prisma.Id, "ADT", "Adiantamento salarial",
            TipoRubrica.Desconto, EstrategiaRubrica.ValorInformado, BaseCalculo.Nenhuma, agora);

        // Fase 4B: a rubrica que recebe o desconto calculado. Nao declara
        // incidencia - e desconto, e desconto nao compoe base.
        var inss = new Rubrica(
            prisma.Id, "INSS", "INSS sobre a folha",
            TipoRubrica.Desconto, EstrategiaRubrica.InssProgressivo, BaseCalculo.Nenhuma, agora);

        // Fase 4C: informativa, porque FGTS e deposito do empregador e nao sai
        // do salario de ninguem. Nao compoe base alguma - ela incide sobre a
        // base, nao a forma.
        var fgts = new Rubrica(
            prisma.Id, "FGTS", "FGTS sobre a folha",
            TipoRubrica.Informativo, EstrategiaRubrica.FgtsMensal, BaseCalculo.Nenhuma, agora);

        Rubrica[] catalogo = [salario, comissao, valeTransporte, adiantamento, inss, fgts];

        contexto.Rubricas.AddRange(catalogo);
        await contexto.SaveChangesAsync(ct);

        var tabelasInss = await contexto.TabelasInss.Include(x => x.Faixas).ToListAsync(ct);
        var tabelasFgts = await contexto.TabelasFgts.ToListAsync(ct);

        var contratos = await contexto.ContratosTrabalho.IgnoreQueryFilters()
            .Include(c => c.Vigencias)
            .Where(c => c.IdEmpresa == empresa.Id)
            .ToListAsync(ct);

        // As competencias saem do relogio, e nao de datas fixas: a demo
        // precisa continuar mostrando "o mes passado" e "este mes" daqui a um
        // ano, sem ninguem reeditar o semeador.
        var atual = Competencia.De(DateOnly.FromDateTime(agora.Date));
        var anterior = atual.Anterior();

        // Uma por competencia: cada folha usa a tabela que valia NA SUA data,
        // e nao a mais recente cadastrada.
        var inssAnterior = ParametrosInss.Montar(inss, tabelasInss, anterior);
        var inssAtual = ParametrosInss.Montar(inss, tabelasInss, atual);
        var fgtsAnterior = ParametrosFgts.Montar(fgts, tabelasFgts, anterior);
        var fgtsAtual = ParametrosFgts.Montar(fgts, tabelasFgts, atual);

        // A folha do mes passado nasce FECHADA, para a demo ter um fato
        // historico: alterar contrato depois disso nao muda mais nada nela.
        var fechada = new FolhaPagamento(prisma.Id, empresa.Id, anterior, agora);
        fechada.Calcular(contratos, salario, catalogo, inssAnterior, fgtsAnterior, agora);

        foreach (var holerite in fechada.Funcionarios.Take(2))
        {
            fechada.AdicionarLancamentoManual(holerite.Id, valeTransporte, 180m, "22 dias", inssAnterior, fgtsAnterior);
        }

        if (fechada.Funcionarios.Count > 0)
        {
            fechada.AdicionarLancamentoManual(fechada.Funcionarios[0].Id, comissao, 450m, null, inssAnterior, fgtsAnterior);
        }

        // Recalcula ANTES de fechar, de proposito: e o cenario que prova que
        // reprocessar preserva o que foi lancado a mao.
        fechada.Calcular(contratos, salario, catalogo, inssAnterior, fgtsAnterior, agora);
        fechada.Fechar(agora);

        // A do mes corrente fica calculada e aberta, para dar o que operar.
        var aberta = new FolhaPagamento(prisma.Id, empresa.Id, atual, agora);
        aberta.Calcular(contratos, salario, catalogo, inssAtual, fgtsAtual, agora);

        if (aberta.Funcionarios.Count > 0)
        {
            aberta.AdicionarLancamentoManual(aberta.Funcionarios[0].Id, adiantamento, 600m, null, inssAtual, fgtsAtual);
        }

        contexto.Folhas.AddRange(fechada, aberta);
        await contexto.SaveChangesAsync(ct);

        return $"{anterior} e {atual}";
    }

    /// <summary>
    /// CPF ficticio com digitos verificadores validos.
    ///
    /// Gerado, e nao copiado: a demo e publica e o CLAUDE.md secao 24 proibe
    /// CPF real. Um numero valido permite exercitar a validacao sem usar o
    /// documento de ninguem.
    /// </summary>
    private static string CpfFicticio(int semente)
    {
        var noveDigitos = (100_000_000 + semente * 7_919 % 800_000_000).ToString("D9");
        var comPrimeiro = noveDigitos + Digito(noveDigitos, 9);
        return comPrimeiro + Digito(comPrimeiro, 10);

        static char Digito(string digitos, int quantidade)
        {
            var soma = 0;
            var peso = quantidade + 1;

            for (var i = 0; i < quantidade; i++)
            {
                soma += (digitos[i] - '0') * peso--;
            }

            var resto = soma * 10 % 11;
            return (char)('0' + (resto == 10 ? 0 : resto));
        }
    }
}
