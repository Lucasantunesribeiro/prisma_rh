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
        builder.Property(v => v.Id).HasColumnName("id").ValueGeneratedNever();

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

        // A garantia de que periodos NAO se sobrepoem vive no banco, mas nao
        // cabe no modelo do EF: e uma constraint de exclusao, criada por SQL na
        // migration RestricaoDeSobreposicaoDeVigencias.
        //
        // A primeira tentativa foi um indice unico parcial em "valido_ate IS
        // NULL". Ele recusava operacao legitima: o EF INSERE a vigencia nova
        // antes de fechar a anterior, e nesse instante existem duas abertas.
        // A constraint de exclusao e DEFERRABLE - verifica no commit, quando o
        // estado ja esta correto - e ainda impede qualquer sobreposicao, nao
        // apenas duas vigencias abertas.
    }
}
