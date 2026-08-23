using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Aplicacao.Identidade;

/// <summary>Motivo pelo qual uma tentativa de autenticacao falhou.</summary>
public enum FalhaAutenticacao
{
    CredencialInvalida,
    UsuarioInativo,
    RefreshInvalido,
    RefreshReutilizado
}

/// <summary>
/// Sessao emitida. O refresh bruto so existe aqui e no cookie do navegador -
/// no banco fica apenas o hash.
/// </summary>
public sealed record SessaoEmitida(
    string AccessToken,
    DateTimeOffset AccessTokenExpiraEm,
    string RefreshTokenBruto,
    DateTimeOffset RefreshTokenExpiraEm,
    UsuarioAutenticado Usuario);

public sealed record UsuarioAutenticado(
    Guid Id,
    Guid IdOrganizacao,
    string Nome,
    string Email,
    Perfil Perfil)
{
    public static UsuarioAutenticado De(Usuario usuario) =>
        new(usuario.Id, usuario.IdOrganizacao, usuario.Nome, usuario.Email, usuario.Perfil);
}

/// <summary>Sucesso com a sessao, ou falha com o motivo. Sem excecao para fluxo esperado.</summary>
public readonly record struct ResultadoAutenticacao
{
    private ResultadoAutenticacao(SessaoEmitida? sessao, FalhaAutenticacao? falha)
    {
        Sessao = sessao;
        Falha = falha;
    }

    public SessaoEmitida? Sessao { get; }
    public FalhaAutenticacao? Falha { get; }
    public bool Sucesso => Sessao is not null;

    public static ResultadoAutenticacao Ok(SessaoEmitida sessao) => new(sessao, null);
    public static ResultadoAutenticacao Erro(FalhaAutenticacao falha) => new(null, falha);
}
