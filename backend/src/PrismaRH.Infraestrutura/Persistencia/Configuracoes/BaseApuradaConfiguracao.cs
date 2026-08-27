using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class BaseApuradaConfiguracao : IEntityTypeConfiguration<BaseApurada>
{
    public void Configure(EntityTypeBuilder<BaseApurada> builder)
    {
        builder.ToTable("bases_apuradas");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(b => b.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(b => b.IdFolhaFuncionario).HasColumnName("id_folha_funcionario").IsRequired();

        builder.Property(b => b.Base).HasColumnName("base").IsRequired();

        // Dinheiro em numeric, nunca float (CLAUDE.md secao 22). A base chega
        // a somar o holerite inteiro, entao usa a mesma precisao dos totais.
        builder.Property(b => b.Valor).HasColumnName("valor").HasPrecision(14, 2).IsRequired();

        // Uma linha por base em cada holerite. Sem isto, um recalculo com bug
        // poderia acumular bases duplicadas e a soma exibida dobraria.
        builder.HasIndex(b => new { b.IdFolhaFuncionario, b.Base })
            .HasDatabaseName("ux_bases_apuradas_holerite_base")
            .IsUnique();
    }
}
