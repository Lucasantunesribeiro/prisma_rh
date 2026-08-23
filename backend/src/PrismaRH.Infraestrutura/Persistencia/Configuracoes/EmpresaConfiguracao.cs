using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Empresas;
using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class EmpresaConfiguracao : IEntityTypeConfiguration<Empresa>
{
    public void Configure(EntityTypeBuilder<Empresa> builder)
    {
        builder.ToTable("empresas");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();

        builder.Property(e => e.RazaoSocial)
            .HasColumnName("razao_social")
            .HasMaxLength(Empresa.TamanhoMaximoRazaoSocial)
            .IsRequired();

        builder.Property(e => e.NomeFantasia)
            .HasColumnName("nome_fantasia")
            .HasMaxLength(Empresa.TamanhoMaximoNomeFantasia);

        // O value object vira os 14 digitos na coluna. Ler de volta passa pela
        // validacao: dado corrompido no banco estoura na leitura em vez de
        // circular pelo sistema como se fosse valido.
        builder.Property(e => e.Cnpj)
            .HasColumnName("cnpj")
            .HasMaxLength(Cnpj.Tamanho)
            .IsRequired()
            .HasConversion(
                cnpj => cnpj.Valor,
                valor => Cnpj.Criar(valor));

        builder.Property(e => e.Ativa).HasColumnName("ativa").IsRequired();
        builder.Property(e => e.CriadaEm).HasColumnName("criada_em").IsRequired();

        builder.HasOne<Organizacao>()
            .WithMany()
            .HasForeignKey(e => e.IdOrganizacao)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.IdOrganizacao, e.Cnpj })
            .IsUnique()
            .HasDatabaseName("ix_empresas_organizacao_cnpj");
    }
}
