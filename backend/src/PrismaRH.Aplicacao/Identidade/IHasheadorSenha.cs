namespace PrismaRH.Aplicacao.Identidade;

public interface IHasheadorSenha
{
    string Gerar(string senha);

    /// <summary>
    /// Confere a senha. Precisa gastar o mesmo tempo com hash valido e
    /// invalido, senao o tempo de resposta vira um oraculo.
    /// </summary>
    bool Conferir(string hashArmazenado, string senhaInformada);
}
