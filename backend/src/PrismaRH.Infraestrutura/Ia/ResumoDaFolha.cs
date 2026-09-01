using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Dominio.Analises;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Workflow;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Infraestrutura.Ia;

/// <summary>Quantas inconsistências de um rótulo. Contado no banco.</summary>
public sealed record ContagemResumo(string Rotulo, int Quantidade);

/// <summary>
/// O retrato da folha — **todo ele apurado por consulta determinística**.
///
/// ⚠️ Esta é a peça que faz a 11B obedecer ao `ROADMAP.md`:
///
/// > *"nunca é a fonte de um número: as contagens e os valores citados no resumo
/// > devem vir de consultas determinísticas da aplicação, não da contagem feita
/// > pelo modelo."*
///
/// A API devolve este retrato **junto** com o texto. A tela mostra os números
/// daqui e a prosa ao lado — então, se o modelo escrever "sete inconsistências"
/// onde há seis, a divergência fica visível na mesma tela, em vez de virar um
/// número que ninguém confere.
/// </summary>
public sealed record RetratoDaFolha(
    string Competencia,
    string Tipo,
    string Situacao,
    int VersaoCalculo,
    int Holerites,
    decimal TotalProventos,
    decimal TotalDescontos,
    decimal TotalLiquido,
    int Inconsistencias,
    int Pendentes,
    IReadOnlyList<ContagemResumo> PorSeveridade,
    IReadOnlyList<ContagemResumo> PorCategoria,
    string? CompetenciaAnterior,
    decimal? VariacaoLiquido,
    int? InconsistenciasAnterior);

/// <summary>O resumo pronto: os números do C#, e a prosa da IA ao lado.</summary>
public sealed record ResumoExecutivo(
    SituacaoIa Situacao,
    RetratoDaFolha Retrato,
    string Texto,
    bool DoCache,
    int TokensUsados);

/// <summary>
/// O resumo executivo da folha (Fase 11B).
///
/// ## A divisão de trabalho
///
/// ```text
/// EF Core   →  conta, soma, compara com a competencia anterior   ← os NUMEROS
/// Modelo    →  escreve o paragrafo que interpreta esses numeros  ← a PROSA
/// ```
///
/// Nunca o contrário. `CLAUDE.md §37.3`: *"se o valor entra numa conta, num
/// holerite ou numa obrigação, ele veio do C#."*
///
/// ## Ninguém aparece por nome
///
/// O `ROADMAP.md` da 11B fala em *"funcionários ou grupos que merecem
/// atenção"*. Entregamos **grupos** — categoria e severidade —, e não pessoas.
///
/// Motivo: mandar uma lista de nomes para fora transformaria o resumo na maior
/// transferência de dado pessoal do produto, e num resumo executivo o nome não
/// acrescenta nada — quem quer saber quem clica na inconsistência, que está
/// logo ali, com o nome vindo do banco e nunca do modelo (`§37.6`).
/// </summary>
public sealed class ResumoDaFolha(ClienteGemini cliente, CacheExplicacoes cache)
{
    public bool Disponivel => cliente.Configurada;

    /// <summary>
    /// Apura o retrato. **Não chama IA nenhuma** — é consulta pura, e a tela
    /// consegue mostrar os números mesmo sem assistente configurado.
    /// </summary>
    public static async Task<RetratoDaFolha?> ApurarAsync(
        PrismaRhDbContext db, Guid idFolha, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        // Sob o filtro global: folha de outra organizacao nao existe daqui.
        var folha = await db.Folhas
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == idFolha, ct);

        if (folha is null)
        {
            return null;
        }

        var holerites = await db.FolhasFuncionario
            .AsNoTracking()
            .CountAsync(h => h.IdFolha == idFolha, ct);

        var achados = db.ResultadosAnalise.AsNoTracking().Where(r => r.IdFolha == idFolha);

        var total = await achados.CountAsync(ct);
        var pendentes = await achados.CountAsync(r => r.Status != StatusInconsistencia.Resolvida, ct);

        var porSeveridade = await achados
            .GroupBy(r => r.Severidade)
            .Select(g => new { g.Key, Quantidade = g.Count() })
            .ToListAsync(ct);

        var porCategoria = await achados
            .GroupBy(r => r.Categoria)
            .Select(g => new { g.Key, Quantidade = g.Count() })
            .ToListAsync(ct);

        // A competencia anterior, da MESMA empresa e do MESMO tipo de folha.
        // Comparar mensal com ferias produziria uma variacao que nao significa
        // nada e que a prosa apresentaria como fato.
        var anterior = await db.Folhas
            .AsNoTracking()
            .Where(f => f.IdEmpresa == folha.IdEmpresa
                && f.Tipo == folha.Tipo
                && f.Competencia < folha.Competencia)
            .OrderByDescending(f => f.Competencia)
            .Select(f => new { f.Id, f.Competencia, f.TotalLiquido })
            .FirstOrDefaultAsync(ct);

        int? achadosAnterior = anterior is null
            ? null
            : await db.ResultadosAnalise
                .AsNoTracking()
                .CountAsync(r => r.IdFolha == anterior.Id, ct);

        return new RetratoDaFolha(
            folha.Competencia.ToString(),
            folha.Tipo.ToString(),
            folha.Situacao.ToString(),
            folha.VersaoCalculo,
            holerites,
            folha.TotalProventos,
            folha.TotalDescontos,
            folha.TotalLiquido,
            total,
            pendentes,
            [.. porSeveridade.OrderByDescending(x => x.Quantidade)
                .Select(x => new ContagemResumo(x.Key.ToString(), x.Quantidade))],
            [.. porCategoria.OrderByDescending(x => x.Quantidade)
                .Select(x => new ContagemResumo(x.Key.ToString(), x.Quantidade))],
            anterior?.Competencia.ToString(),
            anterior is null ? null : folha.TotalLiquido - anterior.TotalLiquido,
            achadosAnterior);
    }

    /// <summary>
    /// Pede a prosa sobre um retrato já apurado.
    ///
    /// A chave do cache inclui a **versão de cálculo** e o total de
    /// inconsistências: recalcular a folha ou rodar as análises de novo produz
    /// um retrato diferente, e o resumo velho deixa de valer na hora.
    /// </summary>
    public async Task<ResumoExecutivo> ResumirAsync(
        RetratoDaFolha retrato,
        Guid idFolha,
        Guid idOrganizacao,
        Guid correlacao,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(retrato);

        if (!cliente.Configurada)
        {
            return new ResumoExecutivo(SituacaoIa.NaoConfigurada, retrato, string.Empty, false, 0);
        }

        var chave = $"resumo:{idOrganizacao:N}:{idFolha:N}:{retrato.VersaoCalculo}:{retrato.Inconsistencias}";

        if (cache.Buscar(chave) is { } guardado)
        {
            return new ResumoExecutivo(SituacaoIa.Respondeu, retrato, guardado, true, 0);
        }

        var resposta = await cliente.ExplicarAsync(
            "Voce escreve o resumo executivo de uma folha de pagamento brasileira "
            + "ja processada e analisada, para um gestor de RH. Em no maximo quatro "
            + "frases, diga o que chama atencao e o que merece conferencia. Nao "
            + "recalcule nada e nao invente numero que nao esteja nos dados.",
            Descrever(retrato),
            correlacao,
            ct);

        if (resposta.Situacao == SituacaoIa.Respondeu)
        {
            cache.Guardar(chave, resposta.Texto);
        }

        return new ResumoExecutivo(
            resposta.Situacao, retrato, resposta.Texto, false, resposta.TokensUsados);
    }

    /// <summary>
    /// Monta o bloco de dados. **Agregados apenas** — nenhuma pessoa.
    ///
    /// Cultura invariante nos números pelo mesmo motivo do resto do sistema: é
    /// campo técnico, e o modelo lê ponto decimal sem ambiguidade. A prosa que
    /// ele devolve fica em português; os números que a tela mostra vêm do
    /// retrato, formatados em pt-BR pelo frontend.
    /// </summary>
    private static string Descrever(RetratoDaFolha r)
    {
        var invariante = CultureInfo.InvariantCulture;
        var texto = new StringBuilder();

        texto.Append("Competencia: ").Append(r.Competencia).Append('\n')
            .Append("Tipo de folha: ").Append(r.Tipo).Append('\n')
            .Append("Situacao: ").Append(r.Situacao).Append('\n')
            .Append("Holerites: ").Append(r.Holerites.ToString(invariante)).Append('\n')
            .Append("Total de proventos: ").Append(r.TotalProventos.ToString("F2", invariante)).Append('\n')
            .Append("Total de descontos: ").Append(r.TotalDescontos.ToString("F2", invariante)).Append('\n')
            .Append("Total liquido: ").Append(r.TotalLiquido.ToString("F2", invariante)).Append('\n')
            .Append("Inconsistencias: ").Append(r.Inconsistencias.ToString(invariante))
            .Append(" (pendentes: ").Append(r.Pendentes.ToString(invariante)).Append(")\n");

        if (r.PorSeveridade.Count > 0)
        {
            texto.Append("Por severidade: ")
                .AppendJoin(", ", r.PorSeveridade.Select(c => $"{c.Rotulo}={c.Quantidade}"))
                .Append('\n');
        }

        if (r.PorCategoria.Count > 0)
        {
            texto.Append("Por categoria: ")
                .AppendJoin(", ", r.PorCategoria.Select(c => $"{c.Rotulo}={c.Quantidade}"))
                .Append('\n');
        }

        if (r.CompetenciaAnterior is { } anterior)
        {
            texto.Append("Competencia anterior: ").Append(anterior).Append('\n');

            if (r.VariacaoLiquido is { } variacao)
            {
                texto.Append("Variacao do liquido contra a anterior: ")
                    .Append(variacao.ToString("F2", invariante)).Append('\n');
            }

            if (r.InconsistenciasAnterior is { } antes)
            {
                texto.Append("Inconsistencias na anterior: ")
                    .Append(antes.ToString(invariante)).Append('\n');
            }
        }

        // Nome, matricula e CPF NAO entram - nem aqui, nem em lugar nenhum
        // desta camada. Ver o quadro na documentacao da classe.
        return texto.ToString();
    }
}
