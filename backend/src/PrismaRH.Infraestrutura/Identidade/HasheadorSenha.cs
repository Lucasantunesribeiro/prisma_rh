using Microsoft.AspNetCore.Identity;
using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Infraestrutura.Identidade;

/// <summary>
/// Hash de senha usando o PasswordHasher do proprio ASP.NET Core (PBKDF2 com
/// HMAC-SHA512, 210.000 iteracoes e salt por senha na versao 3 do formato).
///
/// Nao escrevemos o algoritmo a mao de proposito: criptografia caseira e a
/// forma mais comum de transformar "temos hash de senha" em "temos senha".
/// Usar o hasher da Microsoft evita a dependencia de uma lib externa e ainda
/// entrega upgrade de fator de trabalho de graca.
/// </summary>
public sealed class HasheadorSenha : IHasheadorSenha
{
    private readonly PasswordHasher<Usuario> _interno = new();

    // O PasswordHasher exige uma instancia de usuario, mas nao a usa no
    // formato v3. Um objeto vazio evita ter que carregar o usuario real.
    private static readonly Usuario Irrelevante = (Usuario)System.Runtime.CompilerServices
        .RuntimeHelpers.GetUninitializedObject(typeof(Usuario));

    public string Gerar(string senha)
    {
        if (string.IsNullOrEmpty(senha))
        {
            throw new ArgumentException("Senha e obrigatoria.", nameof(senha));
        }

        return _interno.HashPassword(Irrelevante, senha);
    }

    public bool Conferir(string hashArmazenado, string senhaInformada)
    {
        if (string.IsNullOrEmpty(hashArmazenado))
        {
            return false;
        }

        try
        {
            var resultado = _interno.VerifyHashedPassword(Irrelevante, hashArmazenado, senhaInformada ?? string.Empty);
            return resultado is PasswordVerificationResult.Success
                or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            // Hash corrompido ou em formato desconhecido nao pode derrubar o
            // login de todo mundo: vale como senha errada.
            return false;
        }
    }
}
