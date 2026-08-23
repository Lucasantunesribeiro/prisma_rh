using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Empresas;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class EstabelecimentoConfiguracao : IEntityTypeConfiguration<Estabelecimento>
{
    public void Configure(EntityTypeBuilder<Estabelecimento> builder)
    {
        builder.ToTable("estabelecimentos");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(e => e.IdEmpresa).HasColumnName("id_empresa").IsRequired();

        builder.Property(e => e.Codigo)
            .HasColumnName("codigo")
            .HasMaxLength(Estabelecimento.TamanhoMaximoCodigo)
            .IsRequired();

        builder.Property(e => e.Nome)
            .HasColumnName("nome")
            .HasMaxLength(Estabelecimento.TamanhoMaximoNome)
            .IsRequired();

        builder.Property(e => e.Ativo).HasColumnName("ativo").IsRequired();
        builder.Property(e => e.CriadoEm).HasColumnName("criado_em").IsRequired();

        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(e => e.IdEmpresa)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.IdEmpresa, e.Codigo })
            .IsUnique()
            .HasDatabaseName("ix_estabelecimentos_empresa_codigo");
    }
}
