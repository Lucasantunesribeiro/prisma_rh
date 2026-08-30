using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Aplicacao.Importacao;
using PrismaRH.Dominio.Importacao;
using PrismaRH.Dominio.Pessoas;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Api.Endpoints;

public sealed record LinhaPreviewResposta(
    int Linha,
    string? Nome,
    string? Cpf,
    DateOnly? DataNascimento,
    IReadOnlyList<string> Erros);

public sealed record PreviewResposta(
    string NomeArquivo,
    long TamanhoBytes,
    string HashSha256,
    int Total,
    int Validas,
    int ComErro,
    bool Importavel,
    IReadOnlyList<string> ErrosDoArquivo,
    IReadOnlyList<LinhaPreviewResposta> Linhas);

public sealed record ConfirmacaoResposta(
    Guid IdImportacao,
    string Status,
    string HashSha256,
    int Total,
    int Validas,
    int ComErro,
    int FuncionariosCriados,
    IReadOnlyList<LinhaPreviewResposta> Linhas);

/// <summary>
/// Importacao de arquivos (Fase 5, etapa 3). Por enquanto so **CSV** e so
/// **funcionarios** - XLSX e a etapa 4.
///
/// ## O desenho, e a razao dele
///
/// <code>
/// POST /preview    -> le, valida, devolve. NADA e gravado.
/// POST /confirmar  -> RELE o arquivo, revalida, e so entao grava.
/// </code>
///
/// O servidor **nao guarda o arquivo entre as duas chamadas** - decisao da
/// etapa 2, que evita armazenamento isolado, retencao e download autorizado
/// antes da Fase 9. A consequencia e que a confirmacao precisa do arquivo de
/// novo.
///
/// E isso e uma vantagem de seguranca, nao um custo. **O cliente nunca diz ao
/// backend quais linhas sao validas.** Ele reenvia o arquivo, e o servidor
/// refaz tudo: recalcula o SHA-256, rele, revalida, remapeia. Um preview
/// adulterado no navegador nao tem efeito nenhum, porque nada dele e
/// aproveitado - nem os totais, nem as linhas, nem o hash.
///
/// Se o arquivo mudar entre as duas chamadas, o que vale e **o que foi
/// reenviado**, e o hash gravado e o dele. Nao ha comparacao com o preview
/// anterior porque nao ha preview anterior: o servidor nao guardou nenhum.
/// </summary>
public static class ImportacoesEndpoints
{
    /// <summary>
    /// Teto do corpo da requisicao, no ASP.NET Core.
    ///
    /// E o MESMO numero de <see cref="LimitesImportacao.TamanhoPadraoBytes"/>,
    /// e existe alem dele por uma razao: o limite do dominio para de ler aos
    /// 5 MB, mas o servidor ja teria recebido o resto do corpo. Este aqui
    /// recusa antes, no pipeline.
    ///
    /// Uma folga de 1 MB cobre o cabecalho do multipart, que viaja junto com o
    /// arquivo e nao faz parte dele.
    /// </summary>
    public const long TamanhoMaximoRequisicao = LimitesImportacao.TamanhoPadraoBytes + (1024 * 1024);

    public static IEndpointRouteBuilder MapearImportacoes(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/importacoes").WithTags("Importacoes");

        grupo.MapPost("/funcionarios/preview", PreviewAsync)
            .WithSummary("Le e valida um CSV de funcionarios sem gravar nada")
            .DisableAntiforgery()
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        grupo.MapPost("/funcionarios/confirmar", ConfirmarAsync)
            .WithSummary("Rele o arquivo, revalida e grava os funcionarios")
            .DisableAntiforgery()
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        grupo.MapGet("/", ListarAsync)
            .WithSummary("Historico de importacoes da organizacao")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        grupo.MapGet("/{id:guid}", ObterAsync)
            .WithSummary("Uma importacao, com o relatorio linha a linha")
            .RequireAuthorization(PoliticasAutorizacao.LerDadosEmpresariais);

        return rotas;
    }

    // --------------------------------------------------------------- preview

    /// <summary>
    /// Le, valida e devolve. **Nao grava nada** - nem quando o arquivo esta
    /// perfeito, nem quando esta todo errado.
    ///
    /// Um preview que gravasse encheria a tabela de tentativas que ninguem
    /// levou adiante, e o `ROADMAP.md` e explicito: preview e validacao vem
    /// ANTES da persistencia.
    /// </summary>
    private static async Task<IResult> PreviewAsync(
        IFormFile? arquivo,
        PrismaRhDbContext db,
        IRelogio relogio,
        CancellationToken ct)
    {
        if (RecusarArquivo(arquivo) is { } recusa)
        {
            return recusa;
        }

        var (bytes, erroTamanho) = await LerComTetoAsync(arquivo!, ct);

        if (erroTamanho is not null)
        {
            return erroTamanho;
        }

        var resultado = await InterpretarAsync(bytes, db, relogio, ct);

        return Results.Ok(new PreviewResposta(
            NomeSeguro(arquivo!.FileName),
            bytes.Length,
            Importacao.CalcularHash(bytes),
            resultado.Total,
            resultado.Validas,
            resultado.ComErro,
            resultado.Importavel,
            [.. resultado.ErrosDoArquivo.Select(Descrever)],
            [.. resultado.Linhas.Select(Converter)]));
    }

    // ------------------------------------------------------------ confirmacao

    /// <summary>
    /// Rele o arquivo REENVIADO e grava, tudo numa transacao so.
    ///
    /// Nao recebe id de preview, nem lista de linhas, nem contagem. Recebe o
    /// arquivo. Tudo o que o cliente afirmaria sobre a validacao anterior seria
    /// afirmacao do cliente sobre dado que ele controla.
    ///
    /// A transacao cobre a Importacao, as linhas, os funcionarios e o vinculo
    /// de origem. Qualquer falha desfaz tudo - inclusive o registro da propria
    /// importacao, que sem os funcionarios seria mentira.
    /// </summary>
    private static async Task<IResult> ConfirmarAsync(
        IFormFile? arquivo,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        if (RecusarArquivo(arquivo) is { } recusa)
        {
            return recusa;
        }

        var (bytes, erroTamanho) = await LerComTetoAsync(arquivo!, ct);

        if (erroTamanho is not null)
        {
            return erroTamanho;
        }

        // Recalculado AQUI, sobre o que acabou de chegar. O hash do preview
        // nao entra na conta - ele nem e enviado.
        var hash = Importacao.CalcularHash(bytes);

        var resultado = await InterpretarAsync(bytes, db, relogio, ct);

        var importacao = new Importacao(
            usuario.IdOrganizacao,
            usuario.IdUsuario,
            NomeSeguro(arquivo!.FileName),
            FormatoImportacao.Csv,
            bytes.Length,
            hash,
            relogio.Agora);

        foreach (var erro in resultado.ErrosDoArquivo)
        {
            // Erro do arquivo inteiro nao tem linha. Vai na linha 1, que e o
            // cabecalho, para que o relatorio tenha onde pendurar a mensagem.
            importacao.Registrar(erro.Linha == 0 ? 1 : erro.Linha, [erro.Mensagem]);
        }

        foreach (var linha in resultado.Linhas)
        {
            importacao.Registrar(linha.NumeroNoArquivo, linha.Erros);
        }

        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        db.Importacoes.Add(importacao);

        var criados = 0;

        if (resultado.Importavel)
        {
            var porNumero = importacao.Linhas.ToDictionary(l => l.NumeroNoArquivo);

            foreach (var linha in resultado.Linhas)
            {
                var funcionario = new Funcionario(
                    usuario.IdOrganizacao,
                    linha.Nome!,
                    linha.Cpf!.Value,
                    linha.DataNascimento!.Value,
                    relogio.Agora);

                funcionario.RegistrarOrigem(porNumero[linha.NumeroNoArquivo].Id);

                db.Funcionarios.Add(funcionario);
                criados++;
            }

            importacao.Aplicar();
        }
        else
        {
            // Recusada TAMBEM fica registrada: a tentativa de confirmar
            // aconteceu, e apagar o vestigio deixaria "por que o cadastro nao
            // mudou?" sem resposta. O preview que ninguem confirmou continua
            // nao gerando registro nenhum.
            importacao.Recusar();
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(ct);

            // Ultima rede: o indice unico de CPF, uma corrida entre duas
            // importacoes simultaneas, qualquer constraint. Nada foi gravado -
            // nem os funcionarios, nem a importacao.
            return Results.Conflict(new
            {
                detalhe = "A importacao foi desfeita por conflito no banco. "
                    + "Nenhum funcionario foi criado. Confira se alguem importou "
                    + "o mesmo arquivo ao mesmo tempo e tente de novo.",
            });
        }

        return Results.Ok(new ConfirmacaoResposta(
            importacao.Id,
            importacao.Status.ToString(),
            hash,
            importacao.TotalLinhas,
            importacao.LinhasValidas,
            importacao.LinhasComErro,
            criados,
            [.. resultado.Linhas.Select(Converter)]));
    }

    // --------------------------------------------------------------- consulta

    private static async Task<IResult> ListarAsync(
        PrismaRhDbContext db,
        CancellationToken ct,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 25)
    {
        // Teto na listagem, como toda listagem nova desde a Fase 4G.
        var porPagina = Math.Clamp(tamanho, 1, 100);
        var salto = (Math.Max(pagina, 1) - 1) * porPagina;

        var consulta = db.Importacoes.AsNoTracking().OrderByDescending(i => i.EnviadaEm);

        var total = await consulta.CountAsync(ct);

        var itens = await consulta
            .Skip(salto)
            .Take(porPagina)
            .Select(i => new
            {
                i.Id,
                i.NomeOriginalArquivo,
                Formato = i.Formato.ToString(),
                i.TamanhoBytes,
                i.HashSha256,
                i.EnviadaEm,
                Status = i.Status.ToString(),
                i.TotalLinhas,
                i.LinhasValidas,
                i.LinhasComErro,
            })
            .ToListAsync(ct);

        return Results.Ok(new { Total = total, Pagina = Math.Max(pagina, 1), Itens = itens });
    }

    private static async Task<IResult> ObterAsync(
        Guid id, PrismaRhDbContext db, CancellationToken ct)
    {
        // Passa pelo filtro global: importacao de outra organizacao nao existe
        // daqui, e a resposta e 404 - nunca 403.
        var importacao = await db.Importacoes
            .AsNoTracking()
            .Include(i => i.Linhas)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (importacao is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            importacao.Id,
            importacao.NomeOriginalArquivo,
            Formato = importacao.Formato.ToString(),
            importacao.TamanhoBytes,
            importacao.HashSha256,
            importacao.EnviadaEm,
            Status = importacao.Status.ToString(),
            importacao.TotalLinhas,
            importacao.LinhasValidas,
            importacao.LinhasComErro,
            Linhas = importacao.Linhas
                .OrderBy(l => l.NumeroNoArquivo)
                .Select(l => new
                {
                    l.NumeroNoArquivo,
                    Situacao = l.Situacao.ToString(),
                    l.Erros,
                })
                .ToList(),
        });
    }

    // ----------------------------------------------------------------- apoio

    /// <summary>
    /// Le, valida e mapeia. **O mesmo caminho para preview e confirmacao.**
    ///
    /// Duas implementacoes acabariam divergindo, e a divergencia apareceria
    /// como "a tela dizia que estava tudo certo e a gravacao recusou".
    /// </summary>
    private static async Task<ResultadoFuncionarios> InterpretarAsync(
        byte[] bytes, PrismaRhDbContext db, IRelogio relogio, CancellationToken ct)
    {
        using var fluxo = new MemoryStream(bytes);

        var leitura = LeitorCsv.Ler(fluxo);

        // Sob o filtro global: so os CPFs DESTA organizacao. Sem isso, um CPF
        // da empresa vizinha faria a linha ser recusada aqui - e o erro
        // revelaria que aquele documento existe em outro tenant.
        var cpfs = await db.Funcionarios
            .AsNoTracking()
            .Select(f => f.Cpf.Valor)
            .ToListAsync(ct);

        return ImportadorFuncionarios.Interpretar(
            leitura,
            cpfs.ToHashSet(StringComparer.Ordinal),
            DateOnly.FromDateTime(relogio.Agora.UtcDateTime));
    }

    private static IResult? RecusarArquivo(IFormFile? arquivo)
    {
        if (arquivo is null || arquivo.Length == 0)
        {
            return Results.BadRequest(new { detalhe = "Envie um arquivo em 'arquivo'." });
        }

        // A extensao NAO e a validacao - o conteudo e, e quem o valida e o
        // LeitorCsv. Mas recusar .exe aqui evita ler 5 MB de um arquivo que
        // nunca teria chance, e da uma mensagem melhor que "arquivo vazio".
        var extensao = Path.GetExtension(arquivo.FileName);

        if (!string.Equals(extensao, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                detalhe = "Nesta etapa so CSV e aceito. XLSX vem na proxima entrega.",
            });
        }

        return null;
    }

    private static async Task<(byte[] Bytes, IResult? Erro)> LerComTetoAsync(
        IFormFile arquivo, CancellationToken ct)
    {
        if (arquivo.Length > LimitesImportacao.TamanhoPadraoBytes)
        {
            var megabytes = LimitesImportacao.TamanhoPadraoBytes / (1024d * 1024d);

            return ([], Results.BadRequest(new
            {
                detalhe = $"Arquivo maior que o limite de {megabytes:N1} MB.",
            }));
        }

        using var memoria = new MemoryStream();
        await using var origem = arquivo.OpenReadStream();

        await origem.CopyToAsync(memoria, ct);

        return (memoria.ToArray(), null);
    }

    /// <summary>
    /// So o nome, sem diretorio nenhum.
    ///
    /// `Path.GetFileName` descarta qualquer caminho que venha no cabecalho do
    /// multipart - e um navegador honesto nao manda caminho, mas o cabecalho e
    /// texto que o cliente escreve. O nome nunca vira caminho no servidor
    /// (nada e salvo em disco), mas guardar `../../etc/passwd` no banco como
    /// "nome do arquivo" seria guardar lixo com cara de dado.
    /// </summary>
    private static string NomeSeguro(string? nome)
    {
        var limpo = Path.GetFileName(nome ?? string.Empty).Trim();

        if (limpo.Length == 0)
        {
            return "arquivo.csv";
        }

        return limpo.Length > Importacao.TamanhoMaximoNomeArquivo
            ? limpo[^Importacao.TamanhoMaximoNomeArquivo..]
            : limpo;
    }

    private static string Descrever(ErroImportacao erro) =>
        erro.Linha > 0 ? $"Linha {erro.Linha}: {erro.Mensagem}" : erro.Mensagem;

    private static LinhaPreviewResposta Converter(LinhaFuncionario linha) =>
        new(linha.NumeroNoArquivo, linha.Nome, linha.CpfMascarado, linha.DataNascimento, linha.Erros);
}
