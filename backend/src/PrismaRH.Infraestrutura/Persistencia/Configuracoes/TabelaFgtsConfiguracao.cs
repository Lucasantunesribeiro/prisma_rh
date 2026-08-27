using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Parametros;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

/// <summary>
/// Parametro legal federal, como tabelas_inss: sem id_organizacao e sem filtro
/// global. FGTS e lei, e a mesma aliquota vale para todas as organizacoes.
/// </summary>
public sealed class TabelaFgtsConfiguracao : IEntityTypeConfiguration<TabelaFgts>
{
    public void Configure(EntityTypeBuilder<TabelaFgts> builder)
    {
        builder.ToTable("tabelas_fgts");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(t => t.VigenciaInicio).HasColumnName("vigencia_inicio").IsRequired();

        // Fracao, nao percentual: 8% e 0.08. Seis casas acomodam aliquota com
        // decimal sem perder precisao na conta.
        builder.Property(t => t.Aliquota)
            .HasColumnName("aliquota").HasPrecision(7, 6).IsRequired();

        builder.Property(t => t.Fonte)
            .HasColumnName("fonte")
            .HasMaxLength(TabelaFgts.TamanhoMaximoFonte)
            .IsRequired();

        builder.Property(t => t.CriadoEm).HasColumnName("criado_em").IsRequired();

        builder.Ignore(t => t.AliquotaPercentual);

        // Uma aliquota por data de inicio. Duas vigencias no mesmo dia
        // tornariam ambigua a pergunta "qual valia nesta data?".
        builder.HasIndex(t => t.VigenciaInicio)
            .HasDatabaseName("ux_tabelas_fgts_vigencia_inicio")
            .IsUnique();
    }
}
