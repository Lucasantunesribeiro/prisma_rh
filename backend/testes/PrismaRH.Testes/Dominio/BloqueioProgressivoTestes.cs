using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// O bloqueio progressivo por conta.
///
/// ## O que estes testes protegem
///
/// O `CLAUDE.md §24.18` pede limite por **IP e por conta**, porque cada um vê um
/// ataque que o outro não vê. O limite por IP existe desde a Fase 10; este é o
/// outro lado.
///
/// ⚠️ Mas o risco maior desta funcionalidade **não é o atacante entrar — é a
/// defesa virar arma**. Bloqueio que precisa de alguém para destravar permite
/// que qualquer um tranque qualquer conta errando a senha algumas vezes.
///
/// Por isso a maior parte dos testes aqui prova que o bloqueio **solta
/// sozinho**, e não que ele prende.
/// </summary>
public sealed class BloqueioProgressivoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static Usuario NovoUsuario() => new(
        Guid.CreateVersion7(),
        "Pessoa Teste",
        "pessoa@teste.com",
        "hash-irrelevante-para-esta-classe",
        Perfil.AnalistaRh,
        Agora);

    // ------------------------------------------------------------- politica

    /// <summary>
    /// As primeiras falhas não custam nada. Quem digitou errado, trocou o
    /// layout do teclado ou tentou a senha antiga não é castigado.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AsFalhasToleradasNaoGeramEspera(int falhas) =>
        Assert.Equal(TimeSpan.Zero, PoliticaBloqueioConta.EsperaApos(falhas));

    [Fact]
    public void AEsperaCresceEParaNoTeto()
    {
        var quarta = PoliticaBloqueioConta.EsperaApos(4);
        var quinta = PoliticaBloqueioConta.EsperaApos(5);
        var sexta = PoliticaBloqueioConta.EsperaApos(6);

        Assert.Equal(PoliticaBloqueioConta.EsperaInicial, quarta);
        Assert.True(quinta > quarta);
        Assert.True(sexta > quinta);

        // ⚠️ O teto e o que impede o bloqueio de virar permanente na pratica.
        Assert.Equal(PoliticaBloqueioConta.EsperaMaxima, PoliticaBloqueioConta.EsperaApos(50));
    }

    /// <summary>
    /// ⚠️ Um número absurdo de falhas não pode quebrar a conta.
    ///
    /// `2^60` estoura o `double` e vira infinito, e `TimeSpan.FromSeconds` de
    /// infinito **lança exceção** — que numa rota de login viraria 500, e um 500
    /// no login é indisponibilidade para todo mundo.
    /// </summary>
    [Theory]
    [InlineData(100)]
    [InlineData(10_000)]
    [InlineData(int.MaxValue)]
    public void UmNumeroAbsurdoDeFalhasContinuaNoTetoESemExcecao(int falhas) =>
        Assert.Equal(PoliticaBloqueioConta.EsperaMaxima, PoliticaBloqueioConta.EsperaApos(falhas));

    // -------------------------------------------------------------- usuario

    [Fact]
    public void UsuarioNovoNaoNasceBloqueado()
    {
        var usuario = NovoUsuario();

        Assert.Equal(0, usuario.FalhasDeLogin);
        Assert.Null(usuario.BloqueadoAte);
        Assert.False(usuario.EstaBloqueado(Agora));
    }

    [Fact]
    public void AsPrimeirasFalhasNaoBloqueiam()
    {
        var usuario = NovoUsuario();

        for (var i = 0; i < PoliticaBloqueioConta.FalhasToleradas; i++)
        {
            usuario.RegistrarFalhaDeLogin(Agora);
            Assert.False(usuario.EstaBloqueado(Agora));
        }

        usuario.RegistrarFalhaDeLogin(Agora);

        Assert.True(usuario.EstaBloqueado(Agora));
    }

    /// <summary>
    /// ⚠️ **O teste que impede a defesa de virar arma.**
    ///
    /// O bloqueio expira pelo relógio, sem ninguém destravar. Se este teste
    /// falhar, qualquer pessoa que conheça um e-mail consegue trancar aquele
    /// usuário fora do sistema — e a defesa vira negação de serviço.
    /// </summary>
    [Fact]
    public void OBloqueioExpiraSozinho()
    {
        var usuario = NovoUsuario();

        for (var i = 0; i <= PoliticaBloqueioConta.FalhasToleradas; i++)
        {
            usuario.RegistrarFalhaDeLogin(Agora);
        }

        Assert.True(usuario.EstaBloqueado(Agora));

        var depois = Agora + PoliticaBloqueioConta.EsperaMaxima + TimeSpan.FromSeconds(1);

        Assert.False(usuario.EstaBloqueado(depois));
    }

    /// <summary>
    /// ⚠️ **A segunda metade da mesma proteção.** Quem sabe a senha recupera o
    /// acesso e volta ao estado limpo — não fica "marcado".
    /// </summary>
    [Fact]
    public void UmAcertoZeraTudo()
    {
        var usuario = NovoUsuario();

        for (var i = 0; i < 6; i++)
        {
            usuario.RegistrarFalhaDeLogin(Agora);
        }

        usuario.RegistrarEntradaBemSucedida();

        Assert.Equal(0, usuario.FalhasDeLogin);
        Assert.Null(usuario.BloqueadoAte);
        Assert.Null(usuario.UltimaFalhaEm);
        Assert.False(usuario.EstaBloqueado(Agora));
    }

    /// <summary>
    /// ⚠️ Falhas antigas são esquecidas.
    ///
    /// Sem isto, três erros espalhados por seis meses somariam com o quarto e
    /// bloqueariam alguém que nunca foi atacado — o pior tipo de falso
    /// positivo, porque acontece com o usuário legítimo e ninguém entende.
    /// </summary>
    [Fact]
    public void FalhasAntigasSaoEsquecidas()
    {
        var usuario = NovoUsuario();

        for (var i = 0; i < 3; i++)
        {
            usuario.RegistrarFalhaDeLogin(Agora);
        }

        Assert.Equal(3, usuario.FalhasDeLogin);

        var muitoDepois = Agora + PoliticaBloqueioConta.JanelaDeEsquecimento + TimeSpan.FromMinutes(1);

        usuario.RegistrarFalhaDeLogin(muitoDepois);

        // Recomeca do um, e nao vai para quatro.
        Assert.Equal(1, usuario.FalhasDeLogin);
        Assert.False(usuario.EstaBloqueado(muitoDepois));
    }

    /// <summary>
    /// Falhas dentro da janela continuam somando — senão bastaria esperar a
    /// janela entre tentativas para nunca bloquear.
    /// </summary>
    [Fact]
    public void FalhasDentroDaJanelaContinuamSomando()
    {
        var usuario = NovoUsuario();

        var instante = Agora;

        for (var i = 0; i <= PoliticaBloqueioConta.FalhasToleradas; i++)
        {
            usuario.RegistrarFalhaDeLogin(instante);
            instante += TimeSpan.FromMinutes(1);
        }

        Assert.True(usuario.FalhasDeLogin > PoliticaBloqueioConta.FalhasToleradas);
    }

    /// <summary>
    /// A espera devolvida cresce a cada falha — é o que a torna progressiva, e
    /// não um bloqueio de tamanho fixo.
    /// </summary>
    [Fact]
    public void CadaFalhaDevolveUmaEsperaMaiorOuIgualAAnterior()
    {
        var usuario = NovoUsuario();
        var anterior = TimeSpan.Zero;

        for (var i = 0; i < 10; i++)
        {
            var espera = usuario.RegistrarFalhaDeLogin(Agora);

            Assert.True(espera >= anterior, $"falha {i + 1}: {espera} < {anterior}");

            anterior = espera;
        }

        Assert.Equal(PoliticaBloqueioConta.EsperaMaxima, anterior);
    }
}
