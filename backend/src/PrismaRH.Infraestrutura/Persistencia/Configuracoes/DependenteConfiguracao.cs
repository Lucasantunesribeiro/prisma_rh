using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Pessoas;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class DependenteConfiguracao : IEntityTypeConfiguration<Dependente>
{
    public void Configure(EntityTypeBuilder<Dependente> builder)
    {
        builder.ToTable("dependentes");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(d => d.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(d => d.IdFuncionario).HasColumnName("id_funcionario").IsRequired();

        builder.Property(d => d.Nome)
            .HasColumnName("nome")
            .HasMaxLength(Dependente.TamanhoMaximoNome)
            .IsRequired();

        builder.Property(d => d.DataNascimento).HasColumnName("data_nascimento").IsRequired();

        builder.Property(d => d.Relacao)
            .HasColumnName("relacao").HasConversion<int>().IsRequired();

        // Nulo e informacao aqui, nao ausencia de dado: sem inicio, o
        // dependente existe no cadastro e nao abate IRRF.
        builder.Property(d => d.InicioDeducaoIrrf).HasColumnName("inicio_deducao_irrf");
        builder.Property(d => d.FimDeducaoIrrf).HasColumnName("fim_deducao_irrf");

        builder.Property(d => d.CriadoEm).HasColumnName("criado_em").IsRequired();

        // Derivado de InicioDeducaoIrrf: nao vira coluna, senao passariam a
        // existir duas fontes de verdade que podem discordar.
        builder.Ignore(d => d.DedutivelIrrf);

        // Cascade: apagar a pessoa apaga os dependentes dela. Sao dados
        // pessoais de TERCEIROS que so existem por causa dela - deixa-los
        // orfaos seria reter dado sem finalidade (CLAUDE.md secao 25).
        builder.HasOne<Funcionario>()
            .WithMany()
            .HasForeignKey(d => d.IdFuncionario)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => new { d.IdOrganizacao, d.IdFuncionario })
            .HasDatabaseName("ix_dependentes_organizacao_funcionario");

        // O periodo precisa fazer sentido tambem no banco, e nao so no C#: a
        // garantia final e do PostgreSQL (CLAUDE.md secao 24.21).
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_dependentes_periodo_deducao",
            """
            (inicio_deducao_irrf IS NOT NULL OR fim_deducao_irrf IS NULL)
            AND (fim_deducao_irrf IS NULL OR fim_deducao_irrf >= inicio_deducao_irrf)
            """));
    }
}
