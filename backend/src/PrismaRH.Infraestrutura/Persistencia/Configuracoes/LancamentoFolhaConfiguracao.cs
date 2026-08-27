using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Folha;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class LancamentoFolhaConfiguracao : IEntityTypeConfiguration<LancamentoFolha>
{
    public void Configure(EntityTypeBuilder<LancamentoFolha> builder)
    {
        builder.ToTable("lancamentos_folha");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(l => l.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(l => l.IdFolhaFuncionario).HasColumnName("id_folha_funcionario").IsRequired();
        builder.Property(l => l.IdRubrica).HasColumnName("id_rubrica").IsRequired();

        // Codigo e nome sao COPIAS do que a rubrica dizia no momento do
        // calculo. Renomear a rubrica no ano que vem nao pode reescrever o
        // holerite de agosto - CLAUDE.md secao 4.3.
        builder.Property(l => l.CodigoRubrica)
            .HasColumnName("codigo_rubrica")
            .HasMaxLength(Rubrica.TamanhoMaximoCodigo)
            .IsRequired();

        builder.Property(l => l.NomeRubrica)
            .HasColumnName("nome_rubrica")
            .HasMaxLength(Rubrica.TamanhoMaximoNome)
            .IsRequired();

        builder.Property(l => l.Tipo).HasColumnName("tipo").HasConversion<int>().IsRequired();
        builder.Property(l => l.Estrategia)
            .HasColumnName("estrategia").HasConversion<int>().IsRequired();

        // Congeladas no calculo, como codigo, nome e tipo acima.
        builder.Property(l => l.BasesIncidentes)
            .HasColumnName("bases_incidentes").HasConversion<int>().IsRequired();

        builder.Property(l => l.Origem).HasColumnName("origem").HasConversion<int>().IsRequired();
        builder.Property(l => l.Valor).HasColumnName("valor").HasPrecision(14, 2).IsRequired();

        builder.Property(l => l.Referencia)
            .HasColumnName("referencia")
            .HasMaxLength(LancamentoFolha.TamanhoMaximoReferencia);

        builder.Property(l => l.Ordem).HasColumnName("ordem").IsRequired();

        builder.HasOne<Rubrica>()
            .WithMany()
            .HasForeignKey(l => l.IdRubrica)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.IdFolhaFuncionario, l.Ordem })
            .HasDatabaseName("ix_lancamentos_folha_ordem");

        builder.HasMany(l => l.Memoria)
            .WithOne()
            .HasForeignKey(m => m.IdLancamento)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(l => l.Memoria)
            .HasField("_memoria")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
