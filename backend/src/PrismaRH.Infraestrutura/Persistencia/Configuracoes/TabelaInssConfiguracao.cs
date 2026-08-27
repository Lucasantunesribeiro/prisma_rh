using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Parametros;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

/// <summary>
/// A unica tabela do sistema SEM id_organizacao, e portanto sem filtro global.
///
/// INSS e lei federal: a mesma tabela vale para todas as organizacoes. Dar uma
/// copia para cada uma permitiria que uma delas descontasse errado, e o
/// isolamento multiempresa nao ganharia nada - nao ha dado de ninguem aqui,
/// so numero publicado em portaria.
///
/// A contrapartida e a escrita: so o Administrador da Plataforma cadastra
/// vigencia, porque um erro aqui atinge todo mundo de uma vez.
/// </summary>
public sealed class TabelaInssConfiguracao : IEntityTypeConfiguration<TabelaInss>
{
    public void Configure(EntityTypeBuilder<TabelaInss> builder)
    {
        builder.ToTable("tabelas_inss");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(t => t.VigenciaInicio).HasColumnName("vigencia_inicio").IsRequired();

        builder.Property(t => t.Fonte)
            .HasColumnName("fonte")
            .HasMaxLength(TabelaInss.TamanhoMaximoFonte)
            .IsRequired();

        builder.Property(t => t.CriadoEm).HasColumnName("criado_em").IsRequired();

        // Teto e derivado da ultima faixa: guardar seria permitir que os dois
        // discordassem.
        builder.Ignore(t => t.Teto);

        // Uma tabela por data de inicio. Duas vigencias comecando no mesmo dia
        // tornariam ambigua a pergunta "qual valia em 01/01/2026?" - e a
        // resposta dependeria da ordem que o banco devolvesse.
        builder.HasIndex(t => t.VigenciaInicio)
            .HasDatabaseName("ux_tabelas_inss_vigencia_inicio")
            .IsUnique();

        builder.HasMany(t => t.Faixas)
            .WithOne()
            .HasForeignKey(f => f.IdTabelaInss)
            .OnDelete(DeleteBehavior.Cascade);

        // Acesso por campo: Faixas devolve copia ordenada, como em
        // FolhaFuncionario.
        builder.Navigation(t => t.Faixas)
            .HasField("_faixas")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class FaixaInssConfiguracao : IEntityTypeConfiguration<FaixaInss>
{
    public void Configure(EntityTypeBuilder<FaixaInss> builder)
    {
        builder.ToTable("faixas_inss");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(f => f.IdTabelaInss).HasColumnName("id_tabela_inss").IsRequired();
        builder.Property(f => f.Ordem).HasColumnName("ordem").IsRequired();

        builder.Property(f => f.LimiteSuperior)
            .HasColumnName("limite_superior").HasPrecision(14, 2).IsRequired();

        // Fracao, nao percentual: 7,5% e 0.075. Seis casas acomodam aliquotas
        // com decimal, como 7,5% ou 11,25%, sem perder precisao na conta.
        builder.Property(f => f.Aliquota)
            .HasColumnName("aliquota").HasPrecision(7, 6).IsRequired();

        builder.Ignore(f => f.AliquotaPercentual);

        builder.HasIndex(f => new { f.IdTabelaInss, f.Ordem })
            .HasDatabaseName("ux_faixas_inss_tabela_ordem")
            .IsUnique();
    }
}
