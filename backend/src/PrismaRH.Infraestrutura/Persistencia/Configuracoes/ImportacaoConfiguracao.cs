using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Identidade;
using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class ImportacaoConfiguracao : IEntityTypeConfiguration<Importacao>
{
    public void Configure(EntityTypeBuilder<Importacao> builder)
    {
        builder.ToTable("importacoes");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(i => i.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        builder.Property(i => i.IdUsuario).HasColumnName("id_usuario").IsRequired();

        builder.Property(i => i.NomeOriginalArquivo)
            .HasColumnName("nome_original_arquivo")
            .HasMaxLength(Importacao.TamanhoMaximoNomeArquivo)
            .IsRequired();

        builder.Property(i => i.Formato)
            .HasColumnName("formato").HasConversion<int>().IsRequired();

        builder.Property(i => i.TamanhoBytes).HasColumnName("tamanho_bytes").IsRequired();

        // char(64) fixo: SHA-256 em hexadecimal tem sempre esse tamanho, e uma
        // coluna de tamanho fixo torna a comparacao mais barata e o dado errado
        // impossivel de gravar.
        builder.Property(i => i.HashSha256)
            .HasColumnName("hash_sha256")
            .HasColumnType("char(64)")
            .IsRequired();

        builder.Property(i => i.EnviadaEm).HasColumnName("enviada_em").IsRequired();

        builder.Property(i => i.Status)
            .HasColumnName("status").HasConversion<int>().IsRequired();

        builder.Property(i => i.TotalLinhas).HasColumnName("total_linhas").IsRequired();
        builder.Property(i => i.LinhasValidas).HasColumnName("linhas_validas").IsRequired();
        builder.Property(i => i.LinhasComErro).HasColumnName("linhas_com_erro").IsRequired();

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(i => i.IdUsuario)
            // Restrict, e nao Cascade: apagar um usuario nao pode apagar o
            // rastro do que ele importou. A importacao e registro do que
            // aconteceu, e nao propriedade de quem a fez.
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(i => i.Linhas)
            .HasField("_linhas")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(i => i.Linhas)
            .WithOne()
            .HasForeignKey(l => l.IdImportacao)
            .OnDelete(DeleteBehavior.Cascade);

        // A listagem sempre pergunta "as importacoes desta organizacao, das
        // mais recentes para as mais antigas".
        builder.HasIndex(i => new { i.IdOrganizacao, i.EnviadaEm })
            .HasDatabaseName("ix_importacoes_organizacao_enviada_em")
            .IsDescending(false, true);

        // Achar a importacao a partir do arquivo: "este arquivo aqui ja foi
        // importado?". NAO e unico de proposito - reimportar o mesmo arquivo e
        // legitimo (a primeira vez pode ter sido recusada), e uma constraint
        // aqui transformaria uma correcao em erro de sistema.
        builder.HasIndex(i => new { i.IdOrganizacao, i.HashSha256 })
            .HasDatabaseName("ix_importacoes_organizacao_hash");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_importacoes_tamanho", "tamanho_bytes > 0");

            // Os contadores nao podem se contradizer. A entidade ja garante
            // isso em memoria; a constraint garante contra qualquer caminho
            // que nao passe pelo dominio - script de correcao, por exemplo.
            t.HasCheckConstraint(
                "ck_importacoes_contadores",
                "total_linhas >= 0 and linhas_validas >= 0 and linhas_com_erro >= 0 "
                + "and total_linhas = linhas_validas + linhas_com_erro");
        });
    }
}
