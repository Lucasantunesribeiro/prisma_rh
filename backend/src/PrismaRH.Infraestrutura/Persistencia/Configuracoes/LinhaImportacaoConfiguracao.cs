using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class LinhaImportacaoConfiguracao : IEntityTypeConfiguration<LinhaImportacao>
{
    public void Configure(EntityTypeBuilder<LinhaImportacao> builder)
    {
        builder.ToTable("linhas_importacao");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(l => l.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(l => l.IdImportacao).HasColumnName("id_importacao").IsRequired();

        builder.Property(l => l.NumeroNoArquivo)
            .HasColumnName("numero_no_arquivo").IsRequired();

        builder.Property(l => l.Situacao)
            .HasColumnName("situacao").HasConversion<int>().IsRequired();

        // Os erros vao como ARRAY de texto do PostgreSQL, e nao numa terceira
        // tabela. Eles nunca sao consultados isoladamente - so lidos junto com
        // a linha, para montar o relatorio -, entao uma tabela filha
        // acrescentaria um join a toda leitura sem responder nenhuma pergunta
        // nova. O teto de dez por linha vive no dominio.
        builder.PrimitiveCollection(l => l.Erros)
            .HasColumnName("erros")
            .HasField("_erros")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired();

        // Uma linha por numero, por importacao. Duas entradas para a linha 7 do
        // mesmo arquivo tornariam o relatorio contraditorio, e o vinculo de
        // origem ambiguo.
        builder.HasIndex(l => new { l.IdImportacao, l.NumeroNoArquivo })
            .HasDatabaseName("ux_linhas_importacao_numero")
            .IsUnique();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_linhas_importacao_numero", "numero_no_arquivo > 0"));
    }
}
