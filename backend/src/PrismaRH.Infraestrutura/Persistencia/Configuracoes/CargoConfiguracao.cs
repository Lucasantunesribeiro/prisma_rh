using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class CargoConfiguracao : IEntityTypeConfiguration<Cargo>
{
    public void Configure(EntityTypeBuilder<Cargo> builder)
    {
        builder.ToTable("cargos");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();

        builder.Property(c => c.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(Cargo.TamanhoMaximoCodigo)
            .IsRequired();

        builder.Property(c => c.Nome)
            .HasColumnName("nome")
            .HasMaxLength(Cargo.TamanhoMaximoNome)
            .IsRequired();

        builder.Property(c => c.Ativo).HasColumnName("ativo").IsRequired();
        builder.Property(c => c.CriadoEm).HasColumnName("criado_em").IsRequired();

        builder.HasOne<Organizacao>()
            .WithMany()
            .HasForeignKey(c => c.IdOrganizacao)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.IdOrganizacao, c.Codigo })
            .IsUnique()
            .HasDatabaseName("ix_cargos_organizacao_codigo");
    }
}
