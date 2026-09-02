using System.Net.Http.Json;
using System.Text.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// `GET /api/autenticacao/eu` devolve **o mesmo contrato** de `entrar`.
///
/// ## O defeito que isto corrige
///
/// ⚠️ Visto na produção em 02/09/2026: depois de um F5, a barra lateral
/// mostrava apenas *"Visualizador"* — o nome da pessoa sumia.
///
/// A causa era um contrato desalinhado. `POST entrar` devolvia o usuário
/// completo; `GET eu`, usada para **restaurar** a sessão, devolvia só id,
/// organização e perfil, lidos das claims. Duas respostas diferentes para a
/// mesma pergunta.
///
/// O frontend já tinha marcado `nome` e `email` como opcionais, com um
/// comentário dizendo que a correção "depende de decisão do responsável" — o
/// tipo estava honesto e o contrato é que estava errado.
///
/// Este teste compara as duas respostas campo a campo, para elas não voltarem
/// a divergir.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class SessaoRestauradaTestes(BancoPostgresFixture banco)
{
    [Fact]
    public async Task EuDevolveOsMesmosCamposQueEntrar()
    {
        var fabrica = new FabricaApiIsolada(banco.StringConexao);
        using var cliente = fabrica.CreateClient();

        using var entrada = await cliente.PostAsJsonAsync(
            "/api/autenticacao/entrar",
            new { email = BancoPostgresFixture.EmailAdminA, senha = BancoPostgresFixture.Senha });

        var sessao = await entrada.Content.ReadFromJsonAsync<JsonElement>();
        var doLogin = sessao.GetProperty("usuario");

        cliente.DefaultRequestHeaders.Authorization = new(
            "Bearer", sessao.GetProperty("accessToken").GetString());

        using var eu = await cliente.GetAsync("/api/autenticacao/eu");
        var doEu = await eu.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var campo in new[] { "id", "idOrganizacao", "perfil", "nome", "email" })
        {
            Assert.True(
                doEu.TryGetProperty(campo, out var valor),
                $"`eu` nao devolve `{campo}`. Depois de um F5 a tela perde este dado.");

            Assert.Equal(
                doLogin.GetProperty(campo).ToString(),
                valor.ToString());
        }
    }
}
