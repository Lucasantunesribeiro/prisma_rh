using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Rescisao;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class ValorBaseFgtsRescisorioConfiguracao
    : IEntityTypeConfiguration<ValorBaseFgtsRescisorio>
{
    public void Configure(EntityTypeBuilder<ValorBaseFgtsRescisorio> builder)
    {
        builder.ToTable("valores_base_fgts_rescisorio");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(v => v.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(v => v.IdContrato).HasColumnName("id_contrato").IsRequired();

        builder.Property(v => v.Valor)
            .HasColumnName("valor").HasPrecision(14, 2).IsRequired();

        builder.Property(v => v.Observacao)
            .HasColumnName("observacao")
            .HasMaxLength(ValorBaseFgtsRescisorio.TamanhoMaximoObservacao);

        builder.Property(v => v.InformadoEm).HasColumnName("informado_em").IsRequired();

        builder.HasOne<ContratoTrabalho>()
            .WithMany()
            .HasForeignKey(v => v.IdContrato)
            .OnDelete(DeleteBehavior.Cascade);

        // UM por contrato. Dois valores base para a mesma rescisao tornariam
        // ambigua a pergunta "sobre qual numero a multa foi calculada?" - e a
        // resposta dependeria da ordem que o banco devolvesse.
        builder.HasIndex(v => v.IdContrato)
            .HasDatabaseName("ux_valores_base_fgts_contrato")
            .IsUnique();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_valores_base_fgts_nao_negativo", "valor >= 0"));
    }
}
