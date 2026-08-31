using Microsoft.EntityFrameworkCore;
using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Aplicacao.Importacao;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Dominio.Importacao;
using PrismaRH.Dominio.Pessoas;
using PrismaRH.Infraestrutura.Auditoria;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Infraestrutura.Planilhas;

/// <summary>Como a importacao terminou.</summary>
public sealed record ResultadoProcessamento(
    Guid IdImportacao,
    StatusImportacao Status,
    int TotalLinhas,
    int LinhasValidas,
    int LinhasComErro,
    int FuncionariosCriados,
    bool Conflito);

/// <summary>
/// O nucleo da importacao de funcionarios, **sem HTTP e sem fila**.
///
/// ## Por que esta classe existe
///
/// A Fase 9 acrescenta um segundo caminho para a mesma importacao: alem do
/// `POST /confirmar` sincrono, agora existe o worker que consome da fila.
///
/// Duas copias da regra divergiriam, e a que ninguem olha e sempre a que fica
/// errada. Aqui a regra e uma so; o que muda entre os dois caminhos e **de onde
/// vem o tenant** - da requisicao autenticada, ou da mensagem conferida contra
/// o trabalho gravado.
///
/// ## Transacao e atomicidade
///
/// Tudo acontece em **uma transacao**: a `Importacao`, as linhas, os
/// funcionarios e o evento de auditoria. Ou tudo entra, ou nada entra - e a
/// auditoria nunca registra uma importacao que o banco depois desfez.
/// </summary>
public static class ProcessadorImportacao
{
    public static async Task<ResultadoProcessamento> ExecutarAsync(
        PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        byte[] bytes,
        string nomeArquivo,
        FormatoImportacao formato,
        MapeamentoFuncionarios mapeamento,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(usuario);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(mapeamento);

        var hash = Importacao.CalcularHash(bytes);
        var resultado = await InterpretarAsync(db, relogio, bytes, formato, mapeamento, ct);

        var importacao = new Importacao(
            usuario.IdOrganizacao,
            usuario.IdUsuario,
            nomeArquivo,
            formato,
            bytes.Length,
            hash,
            relogio.Agora);

        foreach (var erro in resultado.ErrosDoArquivo)
        {
            // Erro do arquivo inteiro nao tem linha. Vai na 1, o cabecalho,
            // para que o relatorio tenha onde pendurar a mensagem.
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
            // Recusada TAMBEM fica registrada: a tentativa aconteceu, e apagar
            // o vestigio deixaria "por que o cadastro nao mudou?" sem resposta.
            importacao.Recusar();
        }

        db.Registrar(
            usuario, relogio,
            resultado.Importavel ? AcaoAuditada.ImportacaoAplicada : AcaoAuditada.ImportacaoRecusada,
            EntidadeAuditada.Importacao, importacao.Id,
            resultado.Importavel
                ? $"Importacao de '{nomeArquivo}' aplicada: {criados} funcionarios criados."
                : $"Importacao de '{nomeArquivo}' recusada: {importacao.LinhasComErro} linhas com erro.",
            $"formato={formato};hash={hash[..12]};linhas={importacao.TotalLinhas}");

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
            // nem os funcionarios, nem a importacao, nem a auditoria.
            return new ResultadoProcessamento(Guid.Empty, StatusImportacao.Recusada, 0, 0, 0, 0, true);
        }

        return new ResultadoProcessamento(
            importacao.Id,
            importacao.Status,
            importacao.TotalLinhas,
            importacao.LinhasValidas,
            importacao.LinhasComErro,
            criados,
            false);
    }

    private static async Task<ResultadoFuncionarios> InterpretarAsync(
        PrismaRhDbContext db,
        IRelogio relogio,
        byte[] bytes,
        FormatoImportacao formato,
        MapeamentoFuncionarios mapeamento,
        CancellationToken ct)
    {
        var leitura = formato == FormatoImportacao.Xlsx
            ? LeitorXlsx.Ler(bytes)
            : LeitorCsv.Ler(new MemoryStream(bytes), LimitesImportacao.Padrao);

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
}
