using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Parametros;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

/// <summary>
/// Parametro legal federal, como tabelas_inss e tabelas_fgts: sem
/// id_organizacao e sem filtro global. So o Administrador da Plataforma
/// escreve.
/// </summary>
public sealed class TabelaIrrfConfiguracao : IEntityTypeConfiguration<TabelaIrrf>
{
    public void Configure(EntityTypeBuilder<TabelaIrrf> builder)
    {
        builder.ToTable("tabelas_irrf");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(t => t.VigenciaInicio).HasColumnName("vigencia_inicio").IsRequired();

        builder.Property(t => t.Fonte)
            .HasColumnName("fonte")
            .HasMaxLength(TabelaIrrf.TamanhoMaximoFonte)
            .IsRequired();

        builder.Property(t => t.DeducaoPorDependente)
            .HasColumnName("deducao_por_dependente").HasPrecision(14, 2).IsRequired();

        builder.Property(t => t.DescontoSimplificado)
            .HasColumnName("desconto_simplificado").HasPrecision(14, 2).IsRequired();

        builder.Property(t => t.RedutorBase)
            .HasColumnName("redutor_base").HasPrecision(14, 2).IsRequired();

        // Seis casas: o coeficiente da Lei 15.270/2025 e 0,133145. Arredondar
        // para menos casas mudaria o redutor de todo mundo.
        builder.Property(t => t.RedutorCoeficiente)
            .HasColumnName("redutor_coeficiente").HasPrecision(7, 6).IsRequired();

        builder.Property(t => t.CriadoEm).HasColumnName("criado_em").IsRequired();

        // Todos derivados: guardar permitiria que discordassem da fonte.
        builder.Ignore(t => t.TemRedutor);
        builder.Ignore(t => t.LimiteDoRedutor);
        builder.Ignore(t => t.LimiteIsencao);

        builder.HasIndex(t => t.VigenciaInicio)
            .HasDatabaseName("ux_tabelas_irrf_vigencia_inicio")
            .IsUnique();

        builder.HasMany(t => t.Faixas)
            .WithOne()
            .HasForeignKey(f => f.IdTabelaIrrf)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Faixas)
            .HasField("_faixas")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class FaixaIrrfConfiguracao : IEntityTypeConfiguration<FaixaIrrf>
{
    public void Configure(EntityTypeBuilder<FaixaIrrf> builder)
    {
        builder.ToTable("faixas_irrf");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(f => f.IdTabelaIrrf).HasColumnName("id_tabela_irrf").IsRequired();
        builder.Property(f => f.Ordem).HasColumnName("ordem").IsRequired();

        // NULO na ultima faixa: o IRRF nao tem teto. Por isso a coluna e
        // anulavel, e nao "obrigatoria com um numero enorme dentro".
        builder.Property(f => f.LimiteSuperior)
            .HasColumnName("limite_superior").HasPrecision(14, 2);

        builder.Property(f => f.Aliquota)
            .HasColumnName("aliquota").HasPrecision(7, 6).IsRequired();

        builder.Property(f => f.ParcelaADeduzir)
            .HasColumnName("parcela_a_deduzir").HasPrecision(14, 2).IsRequired();

        builder.Ignore(f => f.AliquotaPercentual);
        builder.Ignore(f => f.SemTeto);

        builder.HasIndex(f => new { f.IdTabelaIrrf, f.Ordem })
            .HasDatabaseName("ux_faixas_irrf_tabela_ordem")
            .IsUnique();
    }
}
