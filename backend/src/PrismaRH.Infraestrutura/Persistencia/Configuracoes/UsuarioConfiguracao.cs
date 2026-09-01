using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Infraestrutura.Persistencia.Configuracoes;

public sealed class UsuarioConfiguracao : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("usuarios");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(u => u.IdOrganizacao).HasColumnName("id_organizacao").IsRequired();

        builder.Property(u => u.Nome)
            .HasColumnName("nome")
            .HasMaxLength(Usuario.TamanhoMaximoNome)
            .IsRequired();

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(Usuario.TamanhoMaximoEmail)
            .IsRequired();

        builder.Property(u => u.SenhaHash).HasColumnName("senha_hash").IsRequired();
        builder.Property(u => u.Perfil).HasColumnName("perfil").HasConversion<int>().IsRequired();
        builder.Property(u => u.Ativo).HasColumnName("ativo").IsRequired();
        builder.Property(u => u.CriadoEm).HasColumnName("criado_em").IsRequired();

        // Bloqueio progressivo por conta.
        //
        // ⚠️ No BANCO, e nao em memoria: a API roda em Lambda, e memoria de
        // processo some no proximo cold start - o contador reiniciaria a cada
        // invocacao e a defesa nao existiria.
        builder.Property(u => u.FalhasDeLogin)
            .HasColumnName("falhas_de_login")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(u => u.BloqueadoAte).HasColumnName("bloqueado_ate");

        builder.Property(u => u.UltimaFalhaEm).HasColumnName("ultima_falha_em");

        builder.HasOne<Organizacao>()
            .WithMany()
            .HasForeignKey(u => u.IdOrganizacao)
            .OnDelete(DeleteBehavior.Restrict);

        // Unico GLOBALMENTE, nao por organizacao. O login recebe apenas e-mail
        // e senha: sem saber a organizacao de antemao, a busca por e-mail
        // precisa ter no maximo um resultado. O preco e que a mesma pessoa nao
        // pode ter conta em duas organizacoes com o mesmo endereco.
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("ix_usuarios_email");

        // Listar usuarios de uma organizacao e a consulta mais comum da tela
        // de administracao.
        builder.HasIndex(u => u.IdOrganizacao)
            .HasDatabaseName("ix_usuarios_organizacao");
    }
}
