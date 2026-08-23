using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Testes.Dominio;

public class UsuarioTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organizacao = Guid.CreateVersion7();

    [Theory]
    [InlineData("Lucas@Exemplo.COM.br", "lucas@exemplo.com.br")]
    [InlineData("  ana@empresa.com  ", "ana@empresa.com")]
    public void NormalizarEmail_PadronizaParaOIndiceUnicoFuncionar(string entrada, string esperado)
    {
        // Se o cadastro grava minusculo e a busca do login procura o que foi
        // digitado, o indice unico deixa de impedir duplicata.
        Assert.Equal(esperado, Usuario.NormalizarEmail(entrada));
    }

    [Theory]
    [InlineData("sem-arroba.com")]
    [InlineData("@semlocal.com")]
    [InlineData("semdominio@")]
    [InlineData("dois@@arrobas.com")]
    [InlineData("com espaco@x.com")]
    [InlineData("sem.ponto@dominio")]
    public void NormalizarEmail_RecusaFormatoInvalido(string entrada)
    {
        Assert.Throws<ArgumentException>(() => Usuario.NormalizarEmail(entrada));
    }

    [Fact]
    public void Criar_ExigeOrganizacao()
    {
        var erro = Assert.Throws<ArgumentException>(() =>
            new Usuario(Guid.Empty, "Lucas", "lucas@x.com", "hash", Perfil.AnalistaRh, Agora));

        Assert.Contains("organizacao", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Criar_GuardaEmailNormalizadoENasceAtivo()
    {
        var usuario = new Usuario(Organizacao, "  Lucas  ", "Lucas@X.com", "hash", Perfil.Auditor, Agora);

        Assert.Equal("lucas@x.com", usuario.Email);
        Assert.Equal("Lucas", usuario.Nome);
        Assert.True(usuario.Ativo);
        Assert.Equal(Perfil.Auditor, usuario.Perfil);
        Assert.NotEqual(Guid.Empty, usuario.Id);
    }

    [Fact]
    public void AlterarSenha_RecusaHashVazio()
    {
        var usuario = new Usuario(Organizacao, "Lucas", "lucas@x.com", "hash", Perfil.AnalistaRh, Agora);

        Assert.Throws<ArgumentException>(() => usuario.AlterarSenha("   "));
    }

    [Fact]
    public void Perfil_TemValoresFixos_ParaNaoRemapearQuemJaEstaNoBanco()
    {
        // Reordenar a enum sem valores explicitos trocaria o perfil de todo
        // usuario ja gravado. Este teste trava isso.
        Assert.Equal(1, (int)Perfil.AdministradorPlataforma);
        Assert.Equal(2, (int)Perfil.AdministradorEmpresa);
        Assert.Equal(3, (int)Perfil.AnalistaRh);
        Assert.Equal(4, (int)Perfil.Auditor);
        Assert.Equal(5, (int)Perfil.Visualizador);
    }
}

public class OrganizacaoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Criar_NasceAtivaComNomeLimpo()
    {
        var org = new Organizacao("  Prisma Servicos de RH Ltda.  ", Agora);

        Assert.Equal("Prisma Servicos de RH Ltda.", org.Nome);
        Assert.True(org.Ativa);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_RecusaNomeVazio(string nome)
    {
        Assert.Throws<ArgumentException>(() => new Organizacao(nome, Agora));
    }

    [Fact]
    public void Criar_RecusaNomeAcimaDoLimite()
    {
        var longo = new string('a', Organizacao.TamanhoMaximoNome + 1);

        Assert.Throws<ArgumentException>(() => new Organizacao(longo, Agora));
    }

    [Fact]
    public void Inativar_E_Reativar_AlternamOEstado()
    {
        var org = new Organizacao("Prisma", Agora);

        org.Inativar();
        Assert.False(org.Ativa);

        org.Reativar();
        Assert.True(org.Ativa);
    }
}

public class RefreshTokenTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Usuario = Guid.CreateVersion7();

    private static RefreshToken Novo() =>
        new(Usuario, "hash-do-token", Agora, Agora.AddDays(7));

    [Fact]
    public void Criar_ExigeExpiracaoPosteriorACriacao()
    {
        Assert.Throws<ArgumentException>(() => new RefreshToken(Usuario, "hash", Agora, Agora));
        Assert.Throws<ArgumentException>(() => new RefreshToken(Usuario, "hash", Agora, Agora.AddSeconds(-1)));
    }

    [Fact]
    public void EstaAtivo_SoEnquantoNaoExpirouENaoFoiRevogado()
    {
        var token = Novo();

        Assert.True(token.EstaAtivo(Agora));
        Assert.True(token.EstaAtivo(Agora.AddDays(6)));
        Assert.False(token.EstaAtivo(Agora.AddDays(7)));   // no instante da expiracao ja vale como expirado
        Assert.False(token.EstaAtivo(Agora.AddDays(8)));
    }

    [Fact]
    public void Revogar_MarcaOInstanteELigaAoSubstituto()
    {
        var token = Novo();
        var substituto = Guid.CreateVersion7();

        token.Revogar(Agora.AddHours(1), substituto);

        Assert.True(token.EstaRevogado);
        Assert.Equal(Agora.AddHours(1), token.RevogadoEm);
        Assert.Equal(substituto, token.SubstituidoPorId);
        Assert.False(token.EstaAtivo(Agora.AddHours(2)));
    }

    [Fact]
    public void Revogar_EIdempotente_ENaoReescreveOInstanteOriginal()
    {
        // Revogar a familia inteira passa por tokens ja revogados. Se isso
        // estourasse, a defesa contra reuso de token quebraria no meio.
        var token = Novo();

        token.Revogar(Agora.AddHours(1));
        token.Revogar(Agora.AddHours(5));

        Assert.Equal(Agora.AddHours(1), token.RevogadoEm);
    }
}
