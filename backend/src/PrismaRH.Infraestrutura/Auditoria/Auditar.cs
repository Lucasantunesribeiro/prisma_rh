using PrismaRH.Aplicacao.Comum;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Infraestrutura.Auditoria;

/// <summary>
/// Registra um evento de negocio.
///
/// ## Por que um metodo de extensao, e nao um servico injetado
///
/// Todo endpoint que audita ja recebe `PrismaRhDbContext`, `IContextoUsuario` e
/// `IRelogio` - sao os tres ingredientes de um evento. Um servico injetado
/// acrescentaria uma interface, um registro no contentor e um duble nos testes
/// para nao fazer nada alem de juntar esses tres.
///
/// O `CLAUDE.md secao 20` proibe abstracao sem necessidade demonstrada, e a
/// necessidade aqui nao existe: o evento e gravado pelo MESMO
/// `SaveChangesAsync` da operacao que o gerou, o que da a garantia mais
/// importante de todas - **ou os dois acontecem, ou nenhum dos dois**.
///
/// Uma auditoria gravada fora da transacao registraria alteracoes que o banco
/// depois desfez, e essa e a pior falha possivel numa trilha: ela mentiria com
/// aparencia de prova.
///
/// ## O evento nao e salvo aqui
///
/// Este metodo so faz `Add`. Quem chama continua dono do `SaveChangesAsync`, e
/// e assim que o evento entra na transacao da operacao.
/// </summary>
public static class Auditar
{
    public static void Registrar(
        this PrismaRhDbContext db,
        IContextoUsuario usuario,
        IRelogio relogio,
        AcaoAuditada acao,
        EntidadeAuditada entidade,
        Guid idEntidade,
        string descricao,
        string? contexto = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(usuario);
        ArgumentNullException.ThrowIfNull(relogio);

        db.EventosAuditoria.Add(new EventoAuditoria(
            usuario.IdOrganizacao,
            usuario.IdUsuario,
            acao,
            entidade,
            idEntidade,
            descricao,
            relogio.Agora,
            contexto));
    }
}
