using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Dominio.Workflow;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class AndamentoInconsistenciaConfiguracao
    : IEntityTypeConfiguration<AndamentoInconsistencia>
{
    public void Configure(EntityTypeBuilder<AndamentoInconsistencia> builder)
    {
        builder.ToTable("andamentos_inconsistencia");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(a => a.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(a => a.IdResultadoAnalise).HasColumnName("id_resultado_analise").IsRequired();

        builder.Property(a => a.Tipo).HasColumnName("tipo").HasConversion<int>().IsRequired();
        builder.Property(a => a.IdAutor).HasColumnName("id_autor");
        builder.Property(a => a.OcorridoEm).HasColumnName("ocorrido_em").IsRequired();

        // A ordem da linha do tempo. Ver AndamentoInconsistencia.Sequencia:
        // ordenar por instante nao basta, porque duas linhas da mesma
        // requisicao compartilham o milissegundo.
        builder.Property(a => a.Sequencia).HasColumnName("sequencia").IsRequired();

        builder.Property(a => a.Texto)
            .HasColumnName("texto")
            .HasMaxLength(AndamentoInconsistencia.TamanhoMaximoTexto);

        builder.Property(a => a.StatusAnterior)
            .HasColumnName("status_anterior").HasConversion<int?>();

        builder.Property(a => a.StatusNovo)
            .HasColumnName("status_novo").HasConversion<int?>();

        builder.Property(a => a.ResponsavelAnterior).HasColumnName("responsavel_anterior");
        builder.Property(a => a.ResponsavelNovo).HasColumnName("responsavel_novo");

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(a => a.IdAutor)
            // Restrict: apagar um usuario nao apaga o que ele escreveu. O
            // historico e registro do que aconteceu, nao propriedade de quem fez.
            .OnDelete(DeleteBehavior.Restrict);

        // A unica pergunta que a tela faz: "a linha do tempo desta
        // inconsistencia, da mais antiga para a mais nova".
        // Ordenado pela SEQUENCIA, e nao pelo instante: e ela que define a
        // ordem da linha do tempo, e o indice precisa servir a consulta que a
        // tela faz de verdade.
        builder.HasIndex(a => new { a.IdOrganizacao, a.IdResultadoAnalise, a.Sequencia })
            .HasDatabaseName("ix_andamentos_inconsistencia_resultado");
    }
}

/// <summary>
/// A trilha de auditoria de negocio.
///
/// ⚠️ **Somente-insercao.** Nao ha configuracao de atualizacao nem de remocao
/// porque nao ha metodo de dominio para nenhuma das duas - e nao ha endpoint.
/// A garantia esta no codigo, e ha teste de integracao percorrendo as rotas
/// para provar que nao existe caminho de edicao para perfil nenhum.
/// </summary>
public sealed class EventoAuditoriaConfiguracao : IEntityTypeConfiguration<EventoAuditoria>
{
    public void Configure(EntityTypeBuilder<EventoAuditoria> builder)
    {
        builder.ToTable("eventos_auditoria");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(e => e.IdUsuario).HasColumnName("id_usuario");

        builder.Property(e => e.Acao).HasColumnName("acao").HasConversion<int>().IsRequired();
        builder.Property(e => e.Entidade).HasColumnName("entidade").HasConversion<int>().IsRequired();
        builder.Property(e => e.IdEntidade).HasColumnName("id_entidade").IsRequired();

        builder.Property(e => e.Descricao)
            .HasColumnName("descricao")
            .HasMaxLength(EventoAuditoria.TamanhoMaximoDescricao)
            .IsRequired();

        builder.Property(e => e.Contexto)
            .HasColumnName("contexto")
            .HasMaxLength(EventoAuditoria.TamanhoMaximoContexto);

        builder.Property(e => e.OcorridoEm).HasColumnName("ocorrido_em").IsRequired();

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(e => e.IdUsuario)
            // Restrict, e nao Cascade: apagar um usuario nao pode apagar o
            // registro do que ele fez. Se pudesse, apagar a propria conta seria
            // a forma mais simples de sumir com a trilha.
            .OnDelete(DeleteBehavior.Restrict);

        // "O que aconteceu nesta organizacao, do mais recente para o mais
        // antigo" - a tela de auditoria e o filtro por periodo.
        builder.HasIndex(e => new { e.IdOrganizacao, e.OcorridoEm })
            .HasDatabaseName("ix_eventos_auditoria_organizacao_data")
            .IsDescending(false, true);

        // "O que aconteceu com ESTE funcionario / ESTA folha" - a pergunta que
        // uma reclamacao trabalhista faz.
        builder.HasIndex(e => new { e.IdOrganizacao, e.Entidade, e.IdEntidade })
            .HasDatabaseName("ix_eventos_auditoria_entidade");
    }
}
