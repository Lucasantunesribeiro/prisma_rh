using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class LinhaMemoriaCalculoConfiguracao : IEntityTypeConfiguration<LinhaMemoriaCalculo>
{
    public void Configure(EntityTypeBuilder<LinhaMemoriaCalculo> builder)
    {
        builder.ToTable("memorias_calculo");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(m => m.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(m => m.IdLancamento).HasColumnName("id_lancamento").IsRequired();
        builder.Property(m => m.Ordem).HasColumnName("ordem").IsRequired();

        builder.Property(m => m.Descricao)
            .HasColumnName("descricao")
            .HasMaxLength(LinhaMemoriaCalculo.TamanhoMaximoDescricao)
            .IsRequired();

        builder.Property(m => m.Expressao)
            .HasColumnName("expressao")
            .HasMaxLength(LinhaMemoriaCalculo.TamanhoMaximoExpressao)
            .IsRequired();

        builder.Property(m => m.Valor).HasColumnName("valor").HasPrecision(14, 2).IsRequired();

        builder.HasIndex(m => new { m.IdLancamento, m.Ordem })
            .HasDatabaseName("ix_memorias_calculo_ordem");
    }
}
