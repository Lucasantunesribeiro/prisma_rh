using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Assincrono;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class TrabalhoAssincronoConfiguracao : IEntityTypeConfiguration<TrabalhoAssincrono>
{
    public void Configure(EntityTypeBuilder<TrabalhoAssincrono> construtor)
    {
        ArgumentNullException.ThrowIfNull(construtor);

        construtor.ToTable("trabalhos_assincronos");
        construtor.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        construtor.HasKey(t => t.Id);

        construtor.Property(t => t.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        construtor.Property(t => t.IdUsuario).HasColumnName("id_usuario").IsRequired();
        construtor.Property(t => t.Tipo).HasColumnName("tipo").IsRequired();
        construtor.Property(t => t.Status).HasColumnName("status").IsRequired();
        construtor.Property(t => t.Tentativas).HasColumnName("tentativas").IsRequired();

        construtor.Property(t => t.ChaveIdempotencia).HasColumnName("chave_idempotencia")
            .IsRequired()
            .HasMaxLength(TrabalhoAssincrono.TamanhoMaximoChave);

        construtor.Property(t => t.Erro).HasColumnName("erro").HasMaxLength(TrabalhoAssincrono.TamanhoMaximoErro);

        construtor.Property(t => t.IdRecurso).HasColumnName("id_recurso");
        construtor.Property(t => t.CriadoEm).HasColumnName("criado_em").IsRequired();
        construtor.Property(t => t.IniciadoEm).HasColumnName("iniciado_em");
        construtor.Property(t => t.ConcluidoEm).HasColumnName("concluido_em");

        // ⚠️ A garantia de idempotencia mora AQUI, e nao no C#.
        //
        // Duas requisicoes simultaneas com o mesmo arquivo passariam as duas
        // pelo "ja existe?" antes de qualquer uma gravar - e criariam dois
        // trabalhos. O indice unico e a unica coisa que nenhuma corrida vence,
        // porque a decisao final e do banco.
        //
        // A chave ja inclui a organizacao (ver `ChaveDeImportacao`), entao o
        // indice e global de proposito: duas organizacoes com o mesmo arquivo
        // produzem chaves diferentes e nao colidem.
        construtor.HasIndex(t => t.ChaveIdempotencia)
            .IsUnique()
            .HasDatabaseName("ux_trabalhos_chave_idempotencia");

        // Consulta de status pela tela: "meus trabalhos, mais recentes primeiro".
        construtor.HasIndex(t => new { t.IdOrganizacao, t.CriadoEm })
            .IsDescending(false, true)
            .HasDatabaseName("ix_trabalhos_organizacao_data");

        construtor.HasOne<Dominio.Identidade.Usuario>()
            .WithMany()
            .HasForeignKey(t => t.IdUsuario)
            // RESTRICT: apagar um usuario nao apaga o registro do que ele pediu.
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BlobTemporarioConfiguracao : IEntityTypeConfiguration<BlobTemporario>
{
    public void Configure(EntityTypeBuilder<BlobTemporario> construtor)
    {
        ArgumentNullException.ThrowIfNull(construtor);

        construtor.ToTable("blobs_temporarios");
        construtor.Property(b => b.Id).HasColumnName("id").ValueGeneratedNever();
        construtor.HasKey(b => b.Id);

        construtor.Property(b => b.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();
        construtor.Property(b => b.IdTrabalho).HasColumnName("id_trabalho").IsRequired();
        construtor.Property(b => b.TamanhoBytes).HasColumnName("tamanho_bytes").IsRequired();
        construtor.Property(b => b.CriadoEm).HasColumnName("criado_em").IsRequired();
        construtor.Property(b => b.ExpiraEm).HasColumnName("expira_em").IsRequired();

        construtor.Property(b => b.Conteudo).HasColumnName("conteudo")
            .HasColumnType("bytea")
            .IsRequired();

        // Um trabalho tem no maximo um arquivo. Sem isto, um retry que gravasse
        // de novo duplicaria os bytes - e o orcamento global e pequeno demais
        // para tolerar copia.
        construtor.HasIndex(b => b.IdTrabalho)
            .IsUnique()
            .HasDatabaseName("ux_blobs_trabalho");

        // A varredura de expirados percorre por data, sem filtro de tenant:
        // blob orfao pode ser de qualquer organizacao, e a limpeza roda fora de
        // requisicao.
        construtor.HasIndex(b => b.ExpiraEm).HasDatabaseName("ix_blobs_expiracao");

        construtor.HasOne<TrabalhoAssincrono>()
            .WithMany()
            .HasForeignKey(b => b.IdTrabalho)
            // CASCADE: o blob nao faz sentido sem o trabalho. Ao contrario da
            // `Importacao`, que e historico e sobrevive.
            .OnDelete(DeleteBehavior.Cascade);
    }
}
