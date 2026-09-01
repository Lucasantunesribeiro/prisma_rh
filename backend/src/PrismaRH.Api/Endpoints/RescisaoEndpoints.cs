using PrismaRH.Api.Producao;
﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Ferias;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Rescisao;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Infraestrutura.Auditoria;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record VerbaResposta(
    string Codigo, string Nome, decimal Valor, string Referencia,
    IReadOnlyList<LinhaMemoriaResposta> Memoria);

public sealed record AvisoResposta(
    DevedorDoAviso Devedor, int AnosCompletos, int DiasBase,
    int DiasAcrescidos, int Dias, bool Reduzido);

public sealed record MesProporcionalResposta(
    DateOnly Inicio, DateOnly Fim, int Dias, bool Conta, string Motivo);

public sealed record FeriasProporcionaisResposta(
    DateOnly InicioPeriodo, DateOnly FimPeriodo, int Avos, string Fracao,
    IReadOnlyList<MesProporcionalResposta> Meses);

/// <summary>
/// Entrada do valor base. Vai no CORPO de um POST, e nao na query string.
///
/// Corrigido em 29/08/2026 por decisao registrada no Security Gate: e valor
/// que multiplica dinheiro, precisa ficar gravado e auditavel, e nao trafegar
/// numa URL que vai para log de servidor e historico de navegador.
/// </summary>
public sealed record InformarValorBaseRequisicao(decimal Valor, string? Observacao);

public sealed record ValorBaseFgtsResposta(
    decimal Informado, decimal ConhecidoPeloSistema, bool AbaixoDoConhecido,
    string? Observacao, DateTimeOffset? InformadoEm);

public sealed record RescisaoResposta(
    Guid IdContrato,
    string Matricula,
    MotivoDesligamento Motivo,
    DateOnly DataDesligamento,
    DateOnly DataProjetada,
    decimal SalarioReferencia,
    bool Suportado,
    string? MotivoDoBloqueio,
    string Fonte,
    AvisoResposta? Aviso,
    FeriasProporcionaisResposta? FeriasProporcionais,
    int DiasFeriasVencidas,
    int Avos13,
    string? Fracao13,
    int AvosDoAviso,
    /// <summary>
    /// Nulo quando o valor base ainda NAO foi informado. Zero informado e
    /// coisa diferente de nao informado, e a tela precisa distinguir os dois.
    /// </summary>
    ValorBaseFgtsResposta? ValorBaseFgts,
    /// <summary>
    /// O FGTS que o sistema apurou, sempre presente - inclusive antes de
    /// alguem informar o valor base. E o numero de comparacao.
    /// </summary>
    decimal FgtsConhecidoPeloSistema,
    decimal Total,
    IReadOnlyList<VerbaResposta> Verbas);

/// <summary>
/// A rescisao de um contrato desligado.
///
/// APURACAO, e nao folha: ela responde "quanto esta rescisao vale e por que".
/// A folha de rescisao usa exatamente as mesmas verbas, e e aberta com
/// TipoFolha.Rescisao.
///
/// O VALOR BASE DO FGTS e GRAVADO por PUT, e nao passa por query string. Ele
/// NAO e calculado: o saldo real da conta vinculada inclui correcao e juros
/// que o Prisma RH nao conhece. O que o sistema sabe - a soma dos depositos que
/// ele mesmo apurou - volta na resposta para comparacao, nunca como
/// substituto.
///
/// TRES MOTIVOS SAO BLOQUEADOS. Para eles a resposta vem com Suportado=false e
/// a razao por escrito, mas COM o contexto (avos, dias, datas): quem le
/// precisa entender o que falta, e nao apenas receber um erro.
/// </summary>
public static class RescisaoEndpoints
{
    public static IEndpointRouteBuilder MapearRescisao(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/contratos/{idContrato:guid}/rescisao")
            .WithTags("Rescisao");

        grupo.MapGet("/", ApurarAsync)
            .WithSummary("Apura as verbas rescisorias do contrato desligado")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapPut("/valor-base-fgts", InformarValorBaseAsync)
            .WithSummary("Informa o valor base do FGTS para fins rescisorios")
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        // ⚠️ Movida em 01/09/2026, na Fase 12, de
        // `/api/contratos/{idContrato}/rescisao/matriz` para ca.
        //
        // A varredura de IDOR encontrou a rota antiga devolvendo 200 para um
        // contrato de ninguem. Nao vazava dado - `MatrizAsync` nao recebe
        // parametro e devolve tabela de referencia do sistema, igual para todo
        // mundo. O defeito era de CONTRATO DE API: a rota se apresentava como
        // sub-recurso de um contrato e ignorava o contrato.
        //
        // Isso importa porque a promessa falsa envelhece mal: no dia em que
        // alguem acrescentar ali algo especifico do contrato, a validacao que
        // deveria existir ja nao existe, e ninguem vai lembrar de escreve-la.
        //
        // Tabela de referencia nao e sub-recurso de tenant. Fica na raiz.
        var referencia = rotas.MapGroup("/api/rescisao").WithTags("Rescisao");

        referencia.MapGet("/matriz", MatrizAsync)
            .WithSummary("O que cada motivo de desligamento gera, com a fonte")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        return rotas;
    }

    private static IResult MatrizAsync() =>
        Results.Ok(MatrizVerbasRescisorias.Todas
            .OrderBy(v => v.Motivo)
            .Select(v => new
            {
                v.Motivo,
                v.Suportado,
                v.DevedorDoAviso,
                v.AvisoPelaMetade,
                v.FeriasProporcionais,
                PercentualMultaFgts = v.PercentualMultaFgts * 100m,
                v.Fonte,
                v.MotivoDoBloqueio,
            })
            .ToList());

    /// <summary>
    /// Grava o valor base do FGTS deste contrato.
    ///
    /// PUT e nao POST porque e idempotente: ha UM valor por contrato, e chamar
    /// duas vezes com o mesmo numero deixa o sistema no mesmo estado.
    /// Corrigir o valor e legitimo - ao contrario do motivo do desligamento,
    /// que e a razao do fato; este e uma medida dele, e medida se corrige.
    /// </summary>
    /// <summary>
    /// Dinheiro na descricao da auditoria, sempre em pt-BR.
    ///
    /// Cultura EXPLICITA, e nao a da maquina: a trilha e lida por uma pessoa no
    /// Brasil, e depender do ambiente faria o mesmo evento sair "10,000.00" num
    /// servidor e "10.000,00" noutro - dois textos para o mesmo fato.
    /// </summary>
    private static string Reais(decimal valor) =>
        valor.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    /// <summary>Dinheiro no contexto tecnico: invariante, para poder comparar.</summary>
    private static string Tecnico(decimal? valor) => valor is { } v
        ? v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        : "-";

    private static async Task<IResult> InformarValorBaseAsync(
        Guid idContrato,
        [FromBody] InformarValorBaseRequisicao requisicao,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        var contrato = await db.ContratosTrabalho
            .FirstOrDefaultAsync(c => c.Id == idContrato, ct);

        if (contrato is null)
        {
            return Results.NotFound();
        }

        if (contrato.DataDesligamento is null)
        {
            return Results.Conflict(new
            {
                detalhe = "Contrato ainda esta ativo: nao ha rescisao a informar."
            });
        }

        var existente = await db.ValoresBaseFgts
            .FirstOrDefaultAsync(v => v.IdContrato == idContrato, ct);

        // O valor ANTERIOR e lido antes de ser sobrescrito. Sem isto, a
        // auditoria diria que algo mudou, mas nao de quanto para quanto - e e
        // exatamente a diferenca que importa num numero que multiplica dinheiro.
        var anterior = existente?.Valor;
        var registro = existente;

        try
        {
            if (existente is null)
            {
                registro = new ValorBaseFgtsRescisorio(
                    usuario.IdOrganizacao, idContrato,
                    requisicao.Valor, requisicao.Observacao, relogio.Agora);

                db.ValoresBaseFgts.Add(registro);
            }
            else
            {
                existente.Informar(requisicao.Valor, requisicao.Observacao, relogio.Agora);
            }
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        // ⚠️ Resolve a pendencia do `CLAUDE.md secao 24.19 item 6`, aberta na
        // Fase 4G em 29/08/2026 e apontada para esta fase.
        //
        // O Valor Base do FGTS rescisorio e entrada humana que MULTIPLICA
        // dinheiro: 40% ou 20% dele viram a indenizacao compensatoria. A
        // entidade continua alteravel - corrigir uma medida e legitimo -, mas a
        // ALTERACAO agora e fato registrado, com autor, valor anterior, valor
        // novo e data, em tabela somente-insercao.
        db.Registrar(
            usuario, relogio,
            AcaoAuditada.ValorBaseFgtsInformado,
            EntidadeAuditada.ValorBaseFgtsRescisorio,
            registro!.Id,
            anterior is null
                ? $"Valor base do FGTS rescisorio informado: {Reais(requisicao.Valor)}."
                : $"Valor base do FGTS rescisorio corrigido de {Reais(anterior.Value)} "
                  + $"para {Reais(requisicao.Valor)}.",
            // O CONTEXTO usa cultura INVARIANTE, ao contrario da descricao.
            //
            // A diferenca e proposital: a descricao e prosa para uma pessoa
            // ler; o contexto e `chave=valor` para alguem filtrar e comparar
            // depois. Numero legivel por maquina com separador que muda com o
            // ambiente e numero que nao se compara.
            $"contrato={idContrato};anterior={Tecnico(anterior)};"
            + $"novo={Tecnico(requisicao.Valor)}");

        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ApurarAsync(
        Guid idContrato,
        PrismaRhDbContext db,
        IRelogio relogio,
        CancellationToken ct)
    {
        // Passa pelo filtro global: contrato de outra organizacao nao existe
        // daqui, e a resposta e 404 - nunca 403.
        var contrato = await db.ContratosTrabalho
            .Include(c => c.Vigencias)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == idContrato, ct);

        if (contrato is null)
        {
            return Results.NotFound();
        }

        if (contrato.DataDesligamento is not { } desligamento || contrato.MotivoDesligamento is null)
        {
            return Results.Conflict(new
            {
                detalhe = "Contrato ainda esta ativo: nao ha rescisao a apurar."
            });
        }

        // CLT art. 477 e seguintes: o salario de referencia e o da data do
        // desligamento. Mesma logica das ferias (art. 142).
        var vigencia = contrato.VigenciaEm(desligamento);
        var salario = vigencia?.Salario ?? 0m;

        // Ferias VENCIDAS: os periodos ja adquiridos e ainda com saldo, na
        // data do desligamento.
        var concessoes = await db.ConcessoesFerias
            .AsNoTracking()
            .Where(c => c.IdContrato == idContrato)
            .ComTeto()
            .ToListAsync(ct);

        var diasVencidas = PeriodosAquisitivos.Adquiridos(contrato, desligamento)
            .Select(p => new PeriodoComSaldo(p, [.. concessoes.Where(c => c.EDoPeriodo(p))]))
            .Sum(p => p.Saldo);

        // O que o SISTEMA conhece de FGTS: a soma dos lancamentos de FGTS nos
        // holerites deste contrato. Serve para comparar com o informado, nunca
        // para substitui-lo.
        var conhecido = await db.LancamentosFolha
            .AsNoTracking()
            .Where(l => l.Estrategia == EstrategiaRubrica.FgtsMensal
                        && db.FolhasFuncionario.Any(f => f.Id == l.IdFolhaFuncionario
                                                         && f.IdContrato == idContrato))
            .SumAsync(l => (decimal?)l.Valor, ct) ?? 0m;

        // O valor base vem do que foi GRAVADO, e nao mais de parametro: e
        // entrada humana que multiplica dinheiro, e precisa ser auditavel.
        var gravado = await db.ValoresBaseFgts
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.IdContrato == idContrato, ct);

        var baseFgts = gravado is null
            ? null
            : new ValorBaseFgts(gravado.Valor, conhecido);

        Dominio.Rescisao.Rescisao apuracao;

        try
        {
            apuracao = CalculadoraRescisao.Apurar(contrato, salario, diasVencidas, baseFgts);
        }
        catch (ArgumentException erro)
        {
            return RespostasValidacao.De(erro);
        }

        return Results.Ok(new RescisaoResposta(
            contrato.Id,
            contrato.Matricula,
            apuracao.Motivo,
            apuracao.DataDesligamento,
            apuracao.DataProjetada,
            apuracao.SalarioReferencia,
            apuracao.Suportado,
            apuracao.MotivoDoBloqueio,
            apuracao.Fonte,
            apuracao.Aviso is { } a
                ? new AvisoResposta(a.Devedor, a.AnosCompletos, a.DiasBase, a.DiasAcrescidos, a.Dias, a.Reduzido)
                : null,
            apuracao.FeriasProporcionais is { } f
                ? new FeriasProporcionaisResposta(
                    f.InicioPeriodo, f.FimPeriodo, f.Avos, f.Fracao,
                    [.. f.Meses.Select(m => new MesProporcionalResposta(m.Inicio, m.Fim, m.Dias, m.Conta, m.Motivo))])
                : null,
            apuracao.DiasFeriasVencidas,
            apuracao.Avos13?.Avos ?? 0,
            apuracao.Avos13?.Fracao,
            apuracao.AvosDoAviso,
            apuracao.ValorBaseFgts is { } b
                ? new ValorBaseFgtsResposta(
                    b.Informado, b.ConhecidoPeloSistema, b.AbaixoDoConhecido,
                    gravado?.Observacao, gravado?.InformadoEm)
                : null,
            conhecido,
            apuracao.Total,
            [.. apuracao.Verbas.Select(v => new VerbaResposta(
                v.Codigo, v.Nome, v.Valor, v.Referencia,
                [.. v.Passos.Select((p, i) => new LinhaMemoriaResposta(i + 1, p.Descricao, p.Expressao, p.Valor))]))]));
    }
}
