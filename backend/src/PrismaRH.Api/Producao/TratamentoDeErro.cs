using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace PrismaRH.Api.Producao;

/// <summary>
/// Traduz falhas de protocolo em **400**, e não em 500.
///
/// ## A pendência que isto fecha
///
/// `CLAUDE.md §24.19 item 4`, registrada em 27/08/2026 durante a Fase 4D: um
/// enum com valor desconhecido no corpo — `"relacao": "Papagaio"` — devolvia
/// **500 Internal Server Error**. O mesmo valia para JSON malformado, e a API
/// inteira se comportava assim.
///
/// Não era vazamento nem furo de autorização: o valor inválido é rejeitado e
/// nunca chega ao domínio. O problema é de **contrato e de diagnóstico** — o
/// cliente não distingue "eu mandei errado" de "o servidor caiu", e um 500
/// recorrente mascara falha real no monitoramento.
///
/// ## Por que um handler, e não try/catch em cada rota
///
/// Porque a falha acontece **antes** do handler da rota: quem estoura é o
/// binding do corpo, e nesse momento o código da rota nem começou. Só um
/// tratamento no pipeline alcança.
///
/// ## O que NÃO vai na resposta
///
/// Nem stack trace, nem o texto do parser, nem o valor recebido. A mensagem do
/// `System.Text.Json` costuma incluir um trecho do JSON — que é entrada não
/// confiável e pode conter dado pessoal. O cliente recebe o campo e o motivo em
/// vocabulário próprio; o detalhe fica no log do servidor.
/// </summary>
public sealed class TratamentoDeErro(
    IProblemDetailsService problemas,
    ILogger<TratamentoDeErro> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext contexto,
        Exception excecao,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var (status, titulo, detalhe) = Classificar(excecao);

        if (status is null)
        {
            // Não é falha de protocolo: deixa o handler padrão registrar como
            // erro do servidor, que é o que ela é.
            return false;
        }

        // Nível de INFORMAÇÃO, e não de erro: requisição malformada é
        // comportamento normal de cliente, e alertar sobre ela encheria o
        // monitoramento de ruído que esconde a falha de verdade.
        // Conflito e nivel de AVISO: nao e erro de cliente nem defeito, mas
        // repetido em volume indica contencao que merece olhar.
        log.LogInformation(
            "Requisicao tratada em {Metodo} {Rota}: {Tipo}",
            contexto.Request.Method,
            contexto.Request.Path,
            excecao.GetType().Name);

        contexto.Response.StatusCode = status.Value;

        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexto,
            Exception = null, // de propósito: nada da exceção vaza para o cliente
            ProblemDetails = new ProblemDetails
            {
                Status = status.Value,
                Title = titulo,
                Detail = detalhe,
            },
        });
    }

    private static (int? Status, string Titulo, string Detalhe) Classificar(Exception excecao) => excecao switch
    {
        // Corpo acima do teto declarado na rota. Vem ANTES do caso geral: o
        // padrao de tipo mais especifico precisa ser testado primeiro, senao
        // ele fica inalcancavel e todo payload grande vira 400 - o cliente
        // procuraria erro de formato num arquivo que estava so grande demais.
        BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => (
            StatusCodes.Status413PayloadTooLarge,
            "Conteudo grande demais",
            "O conteudo enviado excede o limite da rota."),

        // Corpo ilegivel, JSON quebrado, enum fora do vocabulario, tipo errado
        // num campo. O ASP.NET Core embrulha tudo isso aqui.
        BadHttpRequestException => (
            StatusCodes.Status400BadRequest,
            "Requisicao invalida",
            "O corpo da requisicao nao pode ser lido. Confira o formato dos campos."),

        // JSON malformado que escapa sem embrulho.
        System.Text.Json.JsonException => (
            StatusCodes.Status400BadRequest,
            "JSON invalido",
            "O corpo nao e um JSON valido."),

        // O cliente desistiu. Não é erro, e responder qualquer coisa é inútil
        // porque não há mais ninguém do outro lado.
        OperationCanceledException => (
            StatusCodesExtras.ClientClosedRequest,
            "Requisicao cancelada",
            "O cliente encerrou a requisicao."),

        // Conflito transitorio do banco: deadlock, falha de serializacao,
        // violacao de unica. Nada foi gravado, e o cliente pode tentar de novo.
        //
        // Precisa estar AQUI, e nao so no `catch` de cada rota: dependendo de
        // onde o PostgreSQL aborta - no comando, no COMMIT, ou ao devolver a
        // conexao - a excecao sobe crua, sem o embrulho do EF, e escapa do
        // `catch (DbUpdateException)`. O cliente recebia 500.
        _ when ConflitoDeBanco.EhConflito(excecao) => (
            StatusCodes.Status409Conflict,
            "Conflito ao gravar",
            "A operacao foi desfeita por conflito no banco. Nada foi gravado. Tente de novo."),

        _ => (null, string.Empty, string.Empty),
    };
}

/// <summary>Códigos que o ASP.NET Core não expõe como constante.</summary>
internal static class StatusCodesExtras
{
    /// <summary>499 — cliente fechou a conexão. Convenção do nginx, adotada amplamente.</summary>
    public const int ClientClosedRequest = 499;
}
