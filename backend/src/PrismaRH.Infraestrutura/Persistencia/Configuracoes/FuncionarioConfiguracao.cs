using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Importacao;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Dominio.Pessoas;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class FuncionarioConfiguracao : IEntityTypeConfiguration<Funcionario>
{
    public void Configure(EntityTypeBuilder<Funcionario> builder)
    {
        builder.ToTable("funcionarios");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(f => f.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();

        builder.Property(f => f.Nome)
            .HasColumnName("nome")
            .HasMaxLength(Funcionario.TamanhoMaximoNome)
            .IsRequired();

        builder.Property(f => f.Cpf)
            .HasColumnName("cpf")
            .HasMaxLength(Cpf.Tamanho)
            .IsRequired()
            .HasConversion(cpf => cpf.Valor, valor => Cpf.Criar(valor));

        // Data civil: nasce num dia, nao num instante. Vira "date" no
        // PostgreSQL, sem fuso horario envolvido.
        builder.Property(f => f.DataNascimento).HasColumnName("data_nascimento").IsRequired();

        builder.Property(f => f.Ativo).HasColumnName("ativo").IsRequired();
        builder.Property(f => f.CriadoEm).HasColumnName("criado_em").IsRequired();

        // Fase 5: de qual linha de arquivo este cadastro veio. Nulo e o caso
        // normal - quem foi cadastrado a mao nao tem origem em arquivo.
        builder.Property(f => f.IdLinhaImportacao).HasColumnName("id_linha_importacao");

        // RESTRICT, e nao Cascade: apagar uma importacao NAO pode levar pessoas
        // junto. Na pratica isso torna indeletavel a importacao que produziu
        // cadastro - que e o que "rastreabilidade da origem" significa.
        builder.HasOne<LinhaImportacao>()
            .WithMany()
            .HasForeignKey(f => f.IdLinhaImportacao)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(f => f.IdLinhaImportacao)
            .HasDatabaseName("ix_funcionarios_linha_importacao")
            .HasFilter("id_linha_importacao is not null");

        builder.HasOne<Organizacao>()
            .WithMany()
            .HasForeignKey(f => f.IdOrganizacao)
            .OnDelete(DeleteBehavior.Restrict);

        // Unico por organizacao, nao global: a mesma pessoa pode ser
        // funcionaria de empresas administradas por organizacoes diferentes.
        builder.HasIndex(f => new { f.IdOrganizacao, f.Cpf })
            .IsUnique()
            .HasDatabaseName("ix_funcionarios_organizacao_cpf");

        builder.HasIndex(f => f.Nome).HasDatabaseName("ix_funcionarios_nome");
    }
}
