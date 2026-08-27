using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Pessoas;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class FolhaFuncionarioConfiguracao : IEntityTypeConfiguration<FolhaFuncionario>
{
    public void Configure(EntityTypeBuilder<FolhaFuncionario> builder)
    {
        builder.ToTable("folhas_funcionario");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(f => f.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(f => f.IdFolha).HasColumnName("id_folha").IsRequired();
        builder.Property(f => f.IdContrato).HasColumnName("id_contrato").IsRequired();
        builder.Property(f => f.IdFuncionario).HasColumnName("id_funcionario").IsRequired();

        builder.Property(f => f.Avos).HasColumnName("avos").IsRequired();
        builder.Property(f => f.Divisor).HasColumnName("divisor").IsRequired();

        builder.Property(f => f.SalarioReferencia)
            .HasColumnName("salario_referencia").HasPrecision(14, 2).IsRequired();

        builder.Property(f => f.IdVigenciaReferencia).HasColumnName("id_vigencia_referencia");

        builder.Property(f => f.TotalProventos).HasColumnName("total_proventos").HasPrecision(14, 2).IsRequired();
        builder.Property(f => f.TotalDescontos).HasColumnName("total_descontos").HasPrecision(14, 2).IsRequired();
        builder.Property(f => f.Liquido).HasColumnName("liquido").HasPrecision(14, 2).IsRequired();

        builder.HasOne<ContratoTrabalho>()
            .WithMany()
            .HasForeignKey(f => f.IdContrato)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Funcionario>()
            .WithMany()
            .HasForeignKey(f => f.IdFuncionario)
            .OnDelete(DeleteBehavior.Restrict);

        // A vigencia que originou o salario. Restrict, e nao Cascade: apagar
        // uma vigencia nao pode levar junto o holerite que ela originou.
        builder.HasOne<VigenciaContrato>()
            .WithMany()
            .HasForeignKey(f => f.IdVigenciaReferencia)
            .OnDelete(DeleteBehavior.Restrict);

        // Um contrato aparece uma unica vez em cada folha.
        builder.HasIndex(f => new { f.IdFolha, f.IdContrato })
            .HasDatabaseName("ux_folhas_funcionario_folha_contrato")
            .IsUnique();

        builder.HasMany(f => f.Lancamentos)
            .WithOne()
            .HasForeignKey(l => l.IdFolhaFuncionario)
            .OnDelete(DeleteBehavior.Cascade);

        // Acesso por campo obrigatorio: a propriedade Lancamentos devolve uma
        // copia ORDENADA, e o EF precisa da lista real para rastrear insercoes
        // e remocoes.
        builder.Navigation(f => f.Lancamentos)
            .HasField("_lancamentos")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(f => f.Bases)
            .WithOne()
            .HasForeignKey(b => b.IdFolhaFuncionario)
            .OnDelete(DeleteBehavior.Cascade);

        // Mesmo motivo dos lancamentos: Bases devolve copia ordenada.
        builder.Navigation(f => f.Bases)
            .HasField("_bases")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
