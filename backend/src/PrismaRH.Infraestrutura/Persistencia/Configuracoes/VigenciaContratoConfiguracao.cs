using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Contratos;
using PrismaRH.Dominio.Empresas;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class VigenciaContratoConfiguracao : IEntityTypeConfiguration<VigenciaContrato>
{
    public void Configure(EntityTypeBuilder<VigenciaContrato> builder)
    {
        builder.ToTable("vigencias_contrato");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id");

        builder.Property(v => v.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(v => v.IdContrato).HasColumnName("id_contrato").IsRequired();

        builder.Property(v => v.ValidoDe).HasColumnName("valido_de").IsRequired();
        builder.Property(v => v.ValidoAte).HasColumnName("valido_ate");

        // Dinheiro nunca em float (CLAUDE.md secao 22). 14 inteiros e 2 casas
        // cobrem qualquer salario real com folga; a precisao e escolhida aqui,
        // nao herdada de um padrao qualquer.
        builder.Property(v => v.Salario)
            .HasColumnName("salario")
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(v => v.IdCargo).HasColumnName("id_cargo").IsRequired();
        builder.Property(v => v.IdEstabelecimento).HasColumnName("id_estabelecimento").IsRequired();
        builder.Property(v => v.JornadaMensalHoras).HasColumnName("jornada_mensal_horas").IsRequired();
        builder.Property(v => v.Motivo).HasColumnName("motivo").HasConversion<int>().IsRequired();
        builder.Property(v => v.CriadoEm).HasColumnName("criado_em").IsRequired();

        builder.HasOne<Cargo>()
            .WithMany()
            .HasForeignKey(v => v.IdCargo)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Estabelecimento>()
            .WithMany()
            .HasForeignKey(v => v.IdEstabelecimento)
            .OnDelete(DeleteBehavior.Restrict);

        // A CONSULTA da Fase 3: "qual vigencia cobre esta data neste contrato".
        builder.HasIndex(v => new { v.IdContrato, v.ValidoDe })
            .HasDatabaseName("ix_vigencias_contrato_inicio");

        // No maximo UMA vigencia aberta por contrato - garantido pelo BANCO.
        // O agregado ja impoe a regra, mas duas requisicoes simultaneas passam
        // pela validacao em C# ao mesmo tempo e gravam as duas. O indice unico
        // parcial e o que transforma a corrida em erro em vez de dado corrompido.
        builder.HasIndex(v => v.IdContrato)
            .IsUnique()
            .HasFilter("valido_ate IS NULL")
            .HasDatabaseName("ix_vigencias_uma_aberta_por_contrato");
    }
}
