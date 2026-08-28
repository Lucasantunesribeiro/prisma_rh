using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Ferias;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class ConcessaoFeriasConfiguracao : IEntityTypeConfiguration<ConcessaoFerias>
{
    public void Configure(EntityTypeBuilder<ConcessaoFerias> builder)
    {
        builder.ToTable("concessoes_ferias");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(c => c.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(c => c.IdContrato).HasColumnName("id_contrato").IsRequired();

        // O periodo aquisitivo e identificado pelas DATAS, e nao por um id:
        // ele nao tem tabela porque e derivado do calendario.
        builder.Property(c => c.InicioPeriodoAquisitivo)
            .HasColumnName("inicio_periodo_aquisitivo").IsRequired();

        builder.Property(c => c.FimPeriodoAquisitivo)
            .HasColumnName("fim_periodo_aquisitivo").IsRequired();

        builder.Property(c => c.Inicio).HasColumnName("inicio").IsRequired();
        builder.Property(c => c.Dias).HasColumnName("dias").IsRequired();

        builder.Property(c => c.DiasAbonoPecuniario)
            .HasColumnName("dias_abono_pecuniario").IsRequired();

        builder.Property(c => c.CriadaEm).HasColumnName("criada_em").IsRequired();

        // Derivados: guardar permitiria que discordassem das datas.
        builder.Ignore(c => c.Fim);
        builder.Ignore(c => c.DiasBaixados);

        // Cascade a partir do contrato: a concessao so existe por causa dele.
        builder.HasOne<ContratoTrabalho>()
            .WithMany()
            .HasForeignKey(c => c.IdContrato)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.IdOrganizacao, c.IdContrato })
            .HasDatabaseName("ix_concessoes_ferias_organizacao_contrato");

        // As invariantes tambem no banco, e nao so no C#: a garantia final e do
        // PostgreSQL (CLAUDE.md secao 24.21).
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_concessoes_ferias_dias",
            """
            dias >= 0
            AND dias_abono_pecuniario >= 0
            AND (dias + dias_abono_pecuniario) > 0
            AND inicio > fim_periodo_aquisitivo
            AND fim_periodo_aquisitivo > inicio_periodo_aquisitivo
            """));
    }
}
