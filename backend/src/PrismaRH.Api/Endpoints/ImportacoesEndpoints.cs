using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrismaRH.Api.Identidade;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Aplicacao.Importacao;
using PrismaRH.Dominio.Importacao;
using PrismaRH.Dominio.Pessoas;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Infraestrutura.Auditoria;
using PrismaRH.Infraestrutura.Persistencia;
using PrismaRH.Infraestrutura.Planilhas;

namespace PrismaRH.Api.Endpoints;

public sealed record LinhaPreviewResposta(
    int Linha,
    string? Nome,
    string? Cpf,
    DateOnly? DataNascimento,
    IReadOnlyList<string> Erros);

public sealed record MapeamentoResposta(string Nome, string Cpf, string DataNascimento);

public sealed record PreviewResposta(
    string NomeArquivo,
    string Formato,
    long TamanhoBytes,
    string HashSha256,
    IReadOnlyList<string> Colunas,
    MapeamentoResposta Mapeamento,
    int Total,
    int Validas,
    int ComErro,
    bool Importavel,
    IReadOnlyList<string> ErrosDoArquivo,
    IReadOnlyList<LinhaPreviewResposta> Linhas);

public sealed record ConfirmacaoResposta(
    Guid IdImportacao,
    string Status,
    string Formato,
    string HashSha256,
    int Total,
    int Validas,
    int ComErro,
    int FuncionariosCriados,
    IReadOnlyList<string> ErrosDoArquivo,
    IReadOnlyList<LinhaPreviewResposta> Linhas);

/// <summary>
/// Importacao de arquivos - **CSV e XLSX**, e por enquanto so **funcionarios**.
///
/// ## O desenho, e a razao dele
///
/// <code>
/// GET  /funcionarios/modelo -> baixa um exemplo pronto
/// POST /preview             -> le, valida, devolve. NADA e gravado.
/// POST /confirmar           -> RELE o arquivo, revalida, e so entao grava.
/// </code>
///
/// O servidor **nao guarda o arquivo entre as duas chamadas** - decisao da
/// etapa 2, que evita armazenamento isolado, retencao e download autorizado
/// antes da Fase 9. A consequencia e que a confirmacao precisa do arquivo de
/// novo.
///
/// E isso e uma vantagem de seguranca, nao um custo. **O cliente nunca diz ao
/// backend quais linhas sao validas.** Ele reenvia o arquivo e o mapeamento, e
/// o servidor refaz tudo: recalcula o SHA-256, rele, revalida, remapeia. Um
/// preview adulterado no navegador nao tem efeito nenhum, porque nada dele e
/// aproveitado - nem os totais, nem as linhas, nem o hash.
///
/// Se o arquivo mudar entre as duas chamadas, o que vale e **o que foi
/// reenviado**, e o hash gravado e o dele. Nao ha comparacao com o preview
/// anterior porque nao ha preview anterior: o servidor nao guardou nenhum.
///
/// ## Um pipeline, dois formatos
///
/// CSV e XLSX divergem so no leitor. Os dois produzem o mesmo
/// <see cref="ResultadoLeitura"/>, e dali para a frente - mapeamento,
/// validacao, duplicata, transacao, rastreabilidade - o caminho e literalmente
/// o mesmo codigo. Duplicar a regra por formato seria duplicar a chance de as
/// duas divergirem.
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

    /// <summary>
    /// O <see cref="TamanhoMaximoRequisicao"/> como metadado das rotas de envio.
    ///
    /// ⚠️ **Ligado em 30/08/2026, na etapa 4.** A constante existia desde a
    /// etapa 3 e o Security Gate daquela etapa falava em "dois tetos" - mas ela
    /// nao estava aplicada a rota nenhuma. Na pratica valia o padrao do
    /// Kestrel, e o arquivo grande so era recusado DEPOIS de o corpo inteiro
    /// ter sido recebido, que e exatamente o que o teto existia para evitar.
    /// </summary>
    private static readonly RequestSizeLimitAttribute TetoDoCorpo = new(TamanhoMaximoRequisicao);

    public static IEndpointRouteBuilder MapearImportacoes(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/importacoes").WithTags("Importacoes");

        grupo.MapGet("/funcionarios/modelo", Modelo)
            .WithSummary("Baixa um arquivo de exemplo com as colunas esperadas")
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        grupo.MapPost("/funcionarios/preview", PreviewAsync)
            .WithSummary("Le e valida um arquivo de funcionarios sem gravar nada")
            .WithMetadata(TetoDoCorpo)
            .DisableAntiforgery()
            .RequireAuthorization(PoliticasAutorizacao.AdministrarPessoas);

        grupo.MapPost("/funcionarios/confirmar", ConfirmarAsync)
            .WithSummary("Rele o arquivo, revalida e grava os funcionarios")
            .WithMetadata(TetoDoCorpo)
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

    // ---------------------------------------------------------------- modelo

    /// <summary>
    /// Um arquivo de exemplo, ja no formato que o importador espera.
    ///
    /// Sem isto, a primeira importacao de qualquer pessoa falha por nome de
    /// coluna, e o relatorio de erro acaba fazendo o papel de manual.
    ///
    /// Nao carrega dado nenhum da organizacao: e estatico, igual para todo
    /// mundo. A politica e a de quem importa, e nao a de leitura geral, porque
    /// e para importar que ele serve (`CLAUDE.md secao 24.4`).
    /// </summary>
    private static IResult Modelo([FromQuery] string? formato) =>
        string.Equals(formato, "xlsx", StringComparison.OrdinalIgnoreCase)
            ? Results.File(
                ModeloFuncionarios.Xlsx(), ModeloFuncionarios.TipoXlsx, ModeloFuncionarios.NomeXlsx)
            : Results.File(
                ModeloFuncionarios.Csv(), ModeloFuncionarios.TipoCsv, ModeloFuncionarios.NomeCsv);

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
        [FromForm] string? colunaNome,
        [FromForm] string? colunaCpf,
        [FromForm] string? colunaDataNascimento,
        PrismaRhDbContext db,
        IRelogio relogio,
        CancellationToken ct)
    {
        var (entrada, recusa) = await ReceberAsync(arquivo, ct);

        if (recusa is not null)
        {
            return recusa;
        }

        var mapeamento = MapeamentoFuncionarios.De(colunaNome, colunaCpf, colunaDataNascimento);
        var resultado = await InterpretarAsync(entrada, mapeamento, db, relogio, ct);

        return Results.Ok(new PreviewResposta(
            entrada.Nome,
            entrada.Formato.ToString(),
            entrada.Bytes.Length,
            Importacao.CalcularHash(entrada.Bytes),
            resultado.Colunas,
            Converter(resultado.Mapeamento),
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
    /// arquivo e o mapeamento. Tudo o que o cliente afirmaria sobre a validacao
    /// anterior seria afirmacao do cliente sobre dado que ele controla.
    ///
    /// O mapeamento e a unica coisa vinda do cliente que influencia a leitura -
    /// e ele e conferido contra o cabecalho do arquivo RELIDO, em
    /// <see cref="MapeamentoFuncionarios.Conferir"/>. Nome de coluna que nao
    /// existe naquele arquivo nao vira indice: vira recusa.
    ///
    /// A transacao cobre a Importacao, as linhas, os funcionarios e o vinculo
    /// de origem. Qualquer falha desfaz tudo - inclusive o registro da propria
    /// importacao, que sem os funcionarios seria mentira.
    /// </summary>
    private static async Task<IResult> ConfirmarAsync(
        IFormFile? arquivo,
        [FromForm] string? colunaNome,
        [FromForm] string? colunaCpf,
        [FromForm] string? colunaDataNascimento,
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        CancellationToken ct)
    {
        var (entrada, recusa) = await ReceberAsync(arquivo, ct);

        if (recusa is not null)
        {
            return recusa;
        }

        // Recalculado AQUI, sobre o que acabou de chegar. O hash do preview
        // nao entra na conta - ele nem e enviado.
        var hash = Importacao.CalcularHash(entrada.Bytes);

        var mapeamento = MapeamentoFuncionarios.De(colunaNome, colunaCpf, colunaDataNascimento);
        var resultado = await InterpretarAsync(entrada, mapeamento, db, relogio, ct);

        var importacao = new Importacao(
            usuario.IdOrganizacao,
            usuario.IdUsuario,
            entrada.Nome,
            entrada.Formato,
            entrada.Bytes.Length,
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

        db.Registrar(
            usuario, relogio,
            resultado.Importavel ? AcaoAuditada.ImportacaoAplicada : AcaoAuditada.ImportacaoRecusada,
            EntidadeAuditada.Importacao, importacao.Id,
            resultado.Importavel
                ? $"Importacao de '{entrada.Nome}' aplicada: {criados} funcionarios criados."
                : $"Importacao de '{entrada.Nome}' recusada: {importacao.LinhasComErro} linhas com erro.",
            $"formato={entrada.Formato};hash={hash[..12]};linhas={importacao.TotalLinhas}");

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
            entrada.Formato.ToString(),
            hash,
            importacao.TotalLinhas,
            importacao.LinhasValidas,
            importacao.LinhasComErro,
            criados,
            [.. resultado.ErrosDoArquivo.Select(Descrever)],
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

    /// <summary>O arquivo recebido, ja conferido e com o formato decidido.</summary>
    private sealed record Entrada(string Nome, FormatoImportacao Formato, byte[] Bytes);

    /// <summary>
    /// Recebe o arquivo: confere, le com teto e decide o formato.
    ///
    /// O mesmo caminho para preview e confirmacao - as duas precisam recusar
    /// exatamente as mesmas coisas, e um `if` a menos numa delas seria uma
    /// porta.
    /// </summary>
    private static async Task<(Entrada Entrada, IResult? Erro)> ReceberAsync(
        IFormFile? arquivo, CancellationToken ct)
    {
        var vazio = new Entrada(string.Empty, FormatoImportacao.Csv, []);

        if (arquivo is null || arquivo.Length == 0)
        {
            return (vazio, Results.BadRequest(new { detalhe = "Envie um arquivo em 'arquivo'." }));
        }

        var extensao = Path.GetExtension(arquivo.FileName)?.ToLowerInvariant();

        if (extensao is not (".csv" or ".xlsx"))
        {
            return (vazio, Results.BadRequest(new { detalhe = "Formatos aceitos: .csv e .xlsx." }));
        }

        if (arquivo.Length > LimitesImportacao.TamanhoPadraoBytes)
        {
            var megabytes = LimitesImportacao.TamanhoPadraoBytes / (1024d * 1024d);

            return (vazio, Results.BadRequest(new
            {
                detalhe = $"Arquivo maior que o limite de {megabytes:N1} MB.",
            }));
        }

        using var memoria = new MemoryStream();

        await using (var origem = arquivo.OpenReadStream())
        {
            await origem.CopyToAsync(memoria, ct);
        }

        var bytes = memoria.ToArray();

        // **A extensao nao decide o formato: o conteudo decide.**
        //
        // Um `.xlsx` comeca com a assinatura de ZIP; um CSV nunca comeca. Se os
        // dois discordam, o arquivo e recusado em vez de adivinhado - porque
        // adivinhar erraria justamente no caso interessante, que e o de alguem
        // tentando fazer um arquivo passar por outro.
        var pareceZip = GuardaXlsx.PareceZip(bytes);

        if (extensao == ".csv" && pareceZip)
        {
            return (vazio, Results.BadRequest(new
            {
                detalhe = "O conteudo e uma planilha, mas o arquivo tem extensao .csv. "
                    + "Renomeie para .xlsx ou salve de verdade como CSV.",
            }));
        }

        if (extensao == ".xlsx" && !pareceZip)
        {
            return (vazio, Results.BadRequest(new
            {
                detalhe = "O arquivo tem extensao .xlsx, mas o conteudo nao e uma planilha. "
                    + "Se for um CSV, renomeie para .csv.",
            }));
        }

        var formato = pareceZip ? FormatoImportacao.Xlsx : FormatoImportacao.Csv;

        return (new Entrada(NomeSeguro(arquivo.FileName), formato, bytes), null);
    }

    /// <summary>
    /// Le, valida e mapeia. **O mesmo caminho para preview e confirmacao.**
    ///
    /// Duas implementacoes acabariam divergindo, e a divergencia apareceria
    /// como "a tela dizia que estava tudo certo e a gravacao recusou".
    ///
    /// O formato so escolhe o LEITOR. Dali para a frente, os dois produzem o
    /// mesmo <see cref="ResultadoLeitura"/> e seguem por codigo identico.
    /// </summary>
    private static async Task<ResultadoFuncionarios> InterpretarAsync(
        Entrada entrada,
        MapeamentoFuncionarios mapeamento,
        PrismaRhDbContext db,
        IRelogio relogio,
        CancellationToken ct)
    {
        var leitura = entrada.Formato == FormatoImportacao.Xlsx
            ? LeitorXlsx.Ler(entrada.Bytes)
            : LerCsv(entrada.Bytes);

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
            DateOnly.FromDateTime(relogio.Agora.UtcDateTime),
            mapeamento);
    }

    private static ResultadoLeitura LerCsv(byte[] bytes)
    {
        using var fluxo = new MemoryStream(bytes, writable: false);

        return LeitorCsv.Ler(fluxo);
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
            return "arquivo";
        }

        return limpo.Length > Importacao.TamanhoMaximoNomeArquivo
            ? limpo[^Importacao.TamanhoMaximoNomeArquivo..]
            : limpo;
    }

    private static string Descrever(ErroImportacao erro) =>
        erro.Linha > 0 ? $"Linha {erro.Linha}: {erro.Mensagem}" : erro.Mensagem;

    private static LinhaPreviewResposta Converter(LinhaFuncionario linha) =>
        new(linha.NumeroNoArquivo, linha.Nome, linha.CpfMascarado, linha.DataNascimento, linha.Erros);

    private static MapeamentoResposta Converter(MapeamentoFuncionarios mapeamento) =>
        new(mapeamento.Nome, mapeamento.Cpf, mapeamento.DataNascimento);
}
