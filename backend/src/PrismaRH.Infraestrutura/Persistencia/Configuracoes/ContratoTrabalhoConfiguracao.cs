using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Dominio.Pessoas;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class ContratoTrabalhoConfiguracao : IEntityTypeConfiguration<ContratoTrabalho>
{
    public void Configure(EntityTypeBuilder<ContratoTrabalho> builder)
    {
        builder.ToTable("contratos_trabalho");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(c => c.IdFuncionario).HasColumnName("id_funcionario").IsRequired();
        builder.Property(c => c.IdEmpresa).HasColumnName("id_empresa").IsRequired();

        builder.Property(c => c.Matricula)
            .HasColumnName("matricula")
            .HasMaxLength(ContratoTrabalho.TamanhoMaximoMatricula)
            .IsRequired();

        builder.Property(c => c.DataAdmissao).HasColumnName("data_admissao").IsRequired();
        builder.Property(c => c.DataDesligamento).HasColumnName("data_desligamento");

        // Nulo enquanto ativo, e NAO ha check constraint exigindo motivo em
        // contrato desligado - de proposito.
        //
        // Os contratos desligados ANTES da Fase 4G ficam com motivo nulo:
        // eles foram encerrados quando o campo nao existia, e ninguem sabe por
        // que. Uma constraint obrigaria a inventar um motivo no backfill, e
        // motivo inventado decide verba rescisoria errada.
        //
        // Para os novos a garantia e do dominio: Desligar exige o motivo e
        // recusa valor nao definido.
        builder.Property(c => c.MotivoDesligamento)
            .HasColumnName("motivo_desligamento").HasConversion<int>();
        builder.Property(c => c.Situacao).HasColumnName("situacao").HasConversion<int>().IsRequired();
        builder.Property(c => c.CriadoEm).HasColumnName("criado_em").IsRequired();

        // As vigencias sao parte do agregado: so existem atraves do contrato.
        // O acesso pelo CAMPO, e nao pela propriedade, e o que permite manter
        // a lista somente-leitura para quem consome o dominio.
        builder.HasMany(c => c.Vigencias)
            .WithOne()
            .HasForeignKey(v => v.IdContrato)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(ContratoTrabalho.Vigencias))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Funcionario>()
            .WithMany()
            .HasForeignKey(c => c.IdFuncionario)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(c => c.IdEmpresa)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.IdEmpresa, c.Matricula })
            .IsUnique()
            .HasDatabaseName("ix_contratos_empresa_matricula");

        builder.HasIndex(c => c.IdFuncionario).HasDatabaseName("ix_contratos_funcionario");
    }
}
