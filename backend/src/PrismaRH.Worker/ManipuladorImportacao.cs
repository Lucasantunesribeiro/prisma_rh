using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Importacao;
using PrismaRH.Dominio.Assincrono;
using PrismaRH.Dominio.Importacao;
using PrismaRH.Infraestrutura.Persistencia;
using PrismaRH.Infraestrutura.Planilhas;

namespace PrismaRH.Worker;

/// <summary>Como o worker terminou uma mensagem. Vocabulario fechado.</summary>
public enum DesfechoMensagem
{
    /// <summary>Processou. A mensagem sai da fila.</summary>
    Concluida = 1,

    /// <summary>Ja estava concluida. Sai da fila sem refazer nada.</summary>
    JaFeita = 2,

    /// <summary>Mensagem invalida ou trabalho inexistente. Vai para a DLQ.</summary>
    Descartada = 3,

    /// <summary>Falhou por algo que pode melhorar sozinho. Volta para a fila.</summary>
    Retentar = 4,
}

/// <summary>
/// O worker de importacao.
///
/// ## A ordem dos passos e a seguranca
///
/// <code>
/// 1. le a mensagem  ................  esquema: versao, ids, tipo, tamanho
/// 2. carrega o trabalho ............  IgnoreQueryFilters - ainda nao ha tenant
/// 3. CONFERE mensagem x trabalho ...  tenant divergente para AQUI
/// 4. abre o contexto do tenant .....  a partir do TRABALHO, nunca da mensagem
/// 5. processa ......................  dai em diante, filtro global valendo
/// 6. apaga os bytes ................  conclua ou falhe, o arquivo vai embora
/// </code>
///
/// O passo 2 usa `IgnoreQueryFilters`, e essa e a unica forma de faze-lo: o
/// worker precisa achar o trabalho **antes** de saber de quem ele e. O que
/// torna isso seguro e o passo 3 - o unico dado que sai do passo 2 e usado
/// depois e a organizacao do proprio trabalho, ja conferida contra a mensagem.
///
/// ## PII nunca entra em log
///
/// Nenhum log daqui carrega nome, CPF, salario, conteudo de arquivo ou a
/// connection string. O que vai para o CloudWatch sao **identificadores** -
/// id do trabalho, quantidade de linhas, desfecho. `CLAUDE.md secao 24.16`:
/// log de nuvem e caro e persistente, e despejar folha nele cria um banco
/// paralelo de dado pessoal e uma conta.
/// </summary>
public sealed class ManipuladorImportacao(IServiceProvider raiz)
{
    /// <summary>
    /// Processa uma mensagem, do inicio ao fim, **num escopo proprio**.
    ///
    /// Escopo por mensagem, e nao por invocacao: a Lambda reaproveita o
    /// processo, e um contexto compartilhado faria a mensagem seguinte herdar
    /// o tenant da anterior.
    /// </summary>
    public async Task<DesfechoMensagem> ProcessarAsync(string corpo, Action<string> log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(log);

        // ---------------------------------------------------------- 1. esquema
        var recusa = MensagemTrabalho.Ler(corpo, OrcamentoSemCusto.TamanhoMaximoMensagemBytes, out var mensagem);

        if (recusa != RecusaMensagem.Aceita || mensagem is null)
        {
            // Nao logamos o corpo: ele e conteudo nao confiavel, e num cenario
            // de adulteracao seria justamente o que nao se quer copiar para o
            // log. O motivo do enum basta para diagnosticar.
            log($"mensagem recusada no esquema: {recusa}");
            return DesfechoMensagem.Descartada;
        }

        using var escopo = raiz.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PrismaRhDbContext>();
        var contexto = escopo.ServiceProvider.GetRequiredService<ContextoDoTrabalho>();
        var relogio = escopo.ServiceProvider.GetRequiredService<IRelogio>();

        // -------------------------------------------------------- 2. o trabalho
        var trabalho = await db.TrabalhosAssincronos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == mensagem.IdTrabalho, ct);

        if (trabalho is null)
        {
            log($"trabalho {mensagem.IdTrabalho} nao existe");
            return DesfechoMensagem.Descartada;
        }

        // ---------------------------------------------------- 3. A CONFERENCIA
        var conferencia = mensagem.Conferir(trabalho);

        if (conferencia != RecusaMensagem.Aceita)
        {
            // ⚠️ Tenant divergente e nivel de ALERTA, e nao de informacao: em
            // operacao normal isso NUNCA acontece. Quando acontece, ou houve
            // adulteracao, ou um defeito montou a mensagem errada.
            log($"ALERTA: mensagem recusada contra o trabalho {trabalho.Id}: {conferencia}");
            return DesfechoMensagem.Descartada;
        }

        // ------------------------------------------------ 4. o contexto do tenant
        contexto.Abrir(trabalho.IdOrganizacao, trabalho.IdUsuario);

        if (!trabalho.Iniciar(relogio.Agora))
        {
            // Idempotencia: a SQS entrega pelo menos uma vez. A segunda entrega
            // de um trabalho concluido vai embora sem refazer nada.
            log($"trabalho {trabalho.Id} ja concluido - entrega repetida ignorada");
            return DesfechoMensagem.JaFeita;
        }

        await db.SaveChangesAsync(ct);

        // ------------------------------------------------------- 5. o arquivo
        var blob = await db.BlobsTemporarios
            .FirstOrDefaultAsync(b => b.IdTrabalho == trabalho.Id, ct);

        if (blob is null)
        {
            // Sem bytes nao ha o que processar, e o arquivo nao vai reaparecer.
            // Definitivo: devolver a mensagem para a fila so gastaria franquia
            // tres vezes ate a DLQ, por algo que nunca vai dar certo.
            trabalho.FalharDefinitivamente(
                "Arquivo temporario nao encontrado ou ja expirado.", relogio.Agora);
            await db.SaveChangesAsync(ct);

            log($"trabalho {trabalho.Id} sem arquivo");
            return DesfechoMensagem.Descartada;
        }

        try
        {
            // ---------------------------------------------------- 6. processa
            var resultado = await ProcessadorImportacao.ExecutarAsync(
                db, contexto, relogio,
                blob.Conteudo,
                $"importacao-{trabalho.Id:N}",
                FormatoImportacao.Csv,
                MapeamentoFuncionarios.Padrao,
                ct);

            if (resultado.Conflito)
            {
                trabalho.Falhar("Conflito no banco. Nada foi gravado.",
                    OrcamentoSemCusto.MaximoTentativas, relogio.Agora);
                await db.SaveChangesAsync(ct);

                log($"trabalho {trabalho.Id} em conflito - nada gravado");
                return DesfechoMensagem.Retentar;
            }

            trabalho.Concluir(resultado.IdImportacao, relogio.Agora);
            await db.SaveChangesAsync(ct);

            // ------------------------------------------- 7. os bytes vao embora
            //
            // Depois da conclusao, e fora da transacao dela: se a remocao
            // falhar, a importacao continua valida e a varredura de expirados
            // pega o arquivo depois. O contrario - apagar antes de concluir -
            // deixaria um trabalho sem como ser reprocessado.
            await OrcamentoBlobs.ApagarDoTrabalhoAsync(db, trabalho.Id, ct);

            log($"trabalho {trabalho.Id} concluido: {resultado.TotalLinhas} linhas, "
                + $"{resultado.FuncionariosCriados} criados, status {resultado.Status}");

            return DesfechoMensagem.Concluida;
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            // A mensagem do erro pode conter detalhe do banco; o log leva so o
            // TIPO. O texto vai para o campo `Erro` do trabalho, que esta sob
            // controle de acesso do tenant - diferente do CloudWatch.
            trabalho.Falhar(excecao.Message, OrcamentoSemCusto.MaximoTentativas, relogio.Agora);
            await db.SaveChangesAsync(ct);

            log($"trabalho {trabalho.Id} falhou ({excecao.GetType().Name}), "
                + $"tentativa {trabalho.Tentativas} de {OrcamentoSemCusto.MaximoTentativas}");

            // Esgotadas as tentativas o trabalho para de vez, e insistir na fila
            // so gastaria franquia. Antes disso, vale tentar: banco fora do ar
            // e falha que melhora sozinha.
            return trabalho.Status == StatusTrabalho.Falhou
                ? DesfechoMensagem.Descartada
                : DesfechoMensagem.Retentar;
        }
    }
}
