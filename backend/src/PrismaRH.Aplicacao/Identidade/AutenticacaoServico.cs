using PrismaRH.Aplicacao.Comum;
using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Aplicacao.Identidade;

/// <summary>
/// Entrar, renovar e sair. Concentra as decisoes de seguranca da sessao para
/// que nenhuma delas fique espalhada em endpoint.
/// </summary>
public sealed class AutenticacaoServico(
    IArmazenamentoIdentidade armazenamento,
    IHasheadorSenha hasheador,
    IGeradorTokens tokens,
    IRelogio relogio)
{
    /// <summary>
    /// Hash descartavel de uma senha inexistente. Serve para gastar o mesmo
    /// tempo de CPU quando o e-mail nao existe: sem isso, a resposta volta
    /// rapido demais e revela quais e-mails estao cadastrados.
    /// </summary>
    private static readonly string HashFalso =
        "AQAAAAIAAYagAAAAEK0000000000000000000000000000000000000000000000000000000000000000000000w==";

    public async Task<ResultadoAutenticacao> EntrarAsync(string email, string senha, CancellationToken ct)
    {
        string emailNormalizado;

        try
        {
            emailNormalizado = Usuario.NormalizarEmail(email);
        }
        catch (ArgumentException)
        {
            // E-mail malformado nao merece resposta diferente de senha errada.
            hasheador.Conferir(HashFalso, senha ?? string.Empty);
            return ResultadoAutenticacao.Erro(FalhaAutenticacao.CredencialInvalida);
        }

        var usuario = await armazenamento.ObterUsuarioPorEmailAsync(emailNormalizado, ct);

        if (usuario is null)
        {
            hasheador.Conferir(HashFalso, senha ?? string.Empty);
            return ResultadoAutenticacao.Erro(FalhaAutenticacao.CredencialInvalida);
        }

        var agora = relogio.Agora;

        // ⚠️ A senha e conferida MESMO com a conta bloqueada, e o resultado e
        // descartado.
        //
        // Nao e desperdicio: e o que mantem o tempo de resposta igual nos tres
        // caminhos - e-mail inexistente, senha errada e conta esperando. Sair
        // antes de conferir devolveria rapido demais e contaria ao atacante que
        // aquela conta existe E que ele acertou o suficiente para bloquea-la
        // (`CLAUDE.md secao 24.3`).
        var senhaConfere = hasheador.Conferir(usuario.SenhaHash, senha ?? string.Empty);

        if (usuario.EstaBloqueado(agora))
        {
            // Resposta IDENTICA a de senha errada. A conta em espera nao se
            // anuncia - senao o bloqueio vira oraculo de existencia.
            return ResultadoAutenticacao.Erro(FalhaAutenticacao.CredencialInvalida);
        }

        if (!senhaConfere)
        {
            usuario.RegistrarFalhaDeLogin(agora);
            await armazenamento.SalvarAsync(ct);

            return ResultadoAutenticacao.Erro(FalhaAutenticacao.CredencialInvalida);
        }

        if (!usuario.Ativo)
        {
            // Depois da senha conferida de proposito: antes disso, a diferenca
            // de resposta contaria a quem tentar que aquele e-mail existe.
            return ResultadoAutenticacao.Erro(FalhaAutenticacao.UsuarioInativo);
        }

        // Um acerto zera o contador. E o que impede o bloqueio de virar arma:
        // quem sabe a senha recupera o acesso sem depender de administrador.
        if (usuario.FalhasDeLogin > 0)
        {
            usuario.RegistrarEntradaBemSucedida();
        }

        return ResultadoAutenticacao.Ok(await EmitirSessaoAsync(usuario, ct));
    }

    public async Task<ResultadoAutenticacao> RenovarAsync(string? refreshBruto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshBruto))
        {
            return ResultadoAutenticacao.Erro(FalhaAutenticacao.RefreshInvalido);
        }

        var hash = tokens.HashearRefreshToken(refreshBruto);
        var token = await armazenamento.ObterRefreshTokenPorHashAsync(hash, ct);

        if (token is null)
        {
            return ResultadoAutenticacao.Erro(FalhaAutenticacao.RefreshInvalido);
        }

        // Token ja revogado reaparecendo significa uma de duas coisas: alguem
        // copiou o cookie, ou o token vazou. Nos dois casos nao da para saber
        // qual das duas pontas e a legitima, entao derruba-se a familia toda e
        // ambas precisam entrar de novo.
        if (token.EstaRevogado)
        {
            await RevogarFamiliaAsync(token.IdUsuario, ct);
            return ResultadoAutenticacao.Erro(FalhaAutenticacao.RefreshReutilizado);
        }

        if (token.EstaExpirado(relogio.Agora))
        {
            return ResultadoAutenticacao.Erro(FalhaAutenticacao.RefreshInvalido);
        }

        var usuario = await armazenamento.ObterUsuarioPorIdAsync(token.IdUsuario, ct);

        if (usuario is null || !usuario.Ativo)
        {
            token.Revogar(relogio.Agora);
            await armazenamento.SalvarAsync(ct);
            return ResultadoAutenticacao.Erro(FalhaAutenticacao.RefreshInvalido);
        }

        var sessao = await EmitirSessaoAsync(usuario, ct, tokenAnterior: token);
        return ResultadoAutenticacao.Ok(sessao);
    }

    /// <summary>Revoga o refresh apresentado. Sair sem token valido nao e erro.</summary>
    public async Task SairAsync(string? refreshBruto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshBruto))
        {
            return;
        }

        var hash = tokens.HashearRefreshToken(refreshBruto);
        var token = await armazenamento.ObterRefreshTokenPorHashAsync(hash, ct);

        if (token is null || token.EstaRevogado)
        {
            return;
        }

        token.Revogar(relogio.Agora);
        await armazenamento.SalvarAsync(ct);
    }

    private async Task<SessaoEmitida> EmitirSessaoAsync(
        Usuario usuario,
        CancellationToken ct,
        RefreshToken? tokenAnterior = null)
    {
        var agora = relogio.Agora;
        var (accessToken, accessExpira) = tokens.GerarAccessToken(usuario);
        var (refreshBruto, refreshHash) = tokens.GerarRefreshToken();

        var novo = new RefreshToken(
            usuario.Id,
            refreshHash,
            agora,
            agora.Add(tokens.DuracaoRefreshToken));

        armazenamento.AdicionarRefreshToken(novo);

        // Rotacao: o anterior morre apontando para o novo, formando a corrente
        // que permite achar a familia inteira depois.
        tokenAnterior?.Revogar(agora, novo.Id);

        await armazenamento.SalvarAsync(ct);

        return new SessaoEmitida(
            accessToken,
            accessExpira,
            refreshBruto,
            novo.ExpiraEm,
            UsuarioAutenticado.De(usuario));
    }

    private async Task RevogarFamiliaAsync(Guid idUsuario, CancellationToken ct)
    {
        var agora = relogio.Agora;

        foreach (var t in await armazenamento.ObterTokensNaoRevogadosAsync(idUsuario, ct))
        {
            t.Revogar(agora);
        }

        await armazenamento.SalvarAsync(ct);
    }
}
