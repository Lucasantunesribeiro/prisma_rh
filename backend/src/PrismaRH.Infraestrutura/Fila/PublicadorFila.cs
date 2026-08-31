using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using PrismaRH.Dominio.Assincrono;
using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Infraestrutura.Fila;

/// <summary>
/// Publica o trabalho na SQS.
///
/// ## Sem fila configurada, o sistema continua funcionando
///
/// Se `PRISMARH_SQS_URL` nao existir, a publicacao vira **no-op** e registra
/// aviso. Isso e proposital: o ambiente de desenvolvimento roda sem AWS, e a
/// suite de testes tambem. Um construtor que estourasse sem a variavel faria a
/// API inteira depender de nuvem para subir - exatamente o oposto do que o
/// `CLAUDE.md secao 1` pede.
///
/// O trabalho fica `Enfileirado` no banco e a tela mostra isso. O que nao
/// acontece e o processamento - e essa e a diferenca honesta entre "nao
/// configurado" e "quebrado".
///
/// ## A mensagem nunca leva dado pessoal
///
/// `MensagemTrabalho` carrega ids, e ha teste medindo o corpo serializado e
/// exigindo ausencia de CPF, nome e conteudo de arquivo. A fila tem retencao
/// propria e uma DLQ onde a mensagem pode ficar quatorze dias - dado pessoal
/// ali seria uma segunda copia, com regras diferentes das do banco.
/// </summary>
public sealed class PublicadorFila(
    IAmazonSQS? sqs,
    ILogger<PublicadorFila> log)
{
    public const string VariavelUrl = "PRISMARH_SQS_URL";

    private readonly string? _url = Environment.GetEnvironmentVariable(VariavelUrl);

    /// <summary>Ha fila configurada? A tela usa isto para nao oferecer o que nao funciona.</summary>
    public bool Configurada => sqs is not null && !string.IsNullOrWhiteSpace(_url);

    public async Task PublicarAsync(MensagemTrabalho mensagem, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mensagem);

        if (!Configurada)
        {
            log.LogWarning(
                "Fila nao configurada ({Variavel} ausente). Trabalho {Id} fica enfileirado sem worker.",
                VariavelUrl, mensagem.IdTrabalho);
            return;
        }

        var corpo = mensagem.Serializar();

        // Cinto e suspensorio: o teto ja e garantido pelo formato da mensagem,
        // mas conferir antes de enviar evita descobrir o problema como erro da
        // SQS, que e mais dificil de diagnosticar.
        if (System.Text.Encoding.UTF8.GetByteCount(corpo) > OrcamentoSemCusto.TamanhoMaximoMensagemBytes)
        {
            throw new InvalidOperationException("Mensagem acima do teto. Nao deveria acontecer.");
        }

        await sqs!.SendMessageAsync(new SendMessageRequest { QueueUrl = _url, MessageBody = corpo }, ct);

        // Log com identificador, nunca com o corpo.
        log.LogInformation("Trabalho {Id} publicado na fila.", mensagem.IdTrabalho);
    }
}
