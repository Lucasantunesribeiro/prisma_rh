using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class OrganizacaoConfiguracao : IEntityTypeConfiguration<Organizacao>
{
    public void Configure(EntityTypeBuilder<Organizacao> builder)
    {
        builder.ToTable("organizacoes");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.Nome)
            .HasColumnName("nome")
            .HasMaxLength(Organizacao.TamanhoMaximoNome)
            .IsRequired();

        builder.Property(o => o.Ativa).HasColumnName("ativa").IsRequired();
        builder.Property(o => o.CriadaEm).HasColumnName("criada_em").IsRequired();
    }
}
