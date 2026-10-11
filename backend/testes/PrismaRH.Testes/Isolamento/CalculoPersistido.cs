using System.Text.Json;

namespace PrismaRH.Testes.Isolamento;

/// <summary>
/// Um HTTP 200 no calculo nao prova que o calculo foi gravado.
///
/// Desde que a escrita na folha passou a rodar numa transacao explicita
/// (trava pessimista, 08/10/2026), esquecer o commit num dos ramos devolveria
/// 200 com o resultado montado em memoria - e o rollback desfaria tudo ao
/// descartar o contexto. Esta conferencia rele a folha numa requisicao NOVA,
/// com outro DbContext, e exige que o que voltou no calculo seja o que esta no
/// banco. Ela roda dentro do ajudante de calculo de cada tipo de folha, entao
/// todo teste de mensal, ferias, 13o e rescisao passa por ela.
/// </summary>
public static class CalculoPersistido
{
    private static readonly string[] Campos =
    [
        "situacao", "versaoCalculo", "quantidadeFuncionarios",
        "totalProventos", "totalDescontos", "totalLiquido",
    ];

    public static async Task ConferirCalculoPersistidoAsync(
        this HttpClient cliente, HttpResponseMessage calculo)
    {
        using var resposta = JsonDocument.Parse(await calculo.Content.ReadAsStringAsync());
        var calculada = resposta.RootElement.GetProperty("folha");
        var id = calculada.GetProperty("id").GetGuid();

        using var relida = JsonDocument.Parse(await cliente.GetStringAsync($"/api/folhas/{id}"));
        var gravada = relida.RootElement.GetProperty("folha");

        Assert.Equal("Calculada", gravada.GetProperty("situacao").GetString());

        foreach (var campo in Campos)
        {
            Assert.True(
                calculada.GetProperty(campo).ToString() == gravada.GetProperty(campo).ToString(),
                $"O campo '{campo}' devolvido pelo calculo nao e o que esta gravado: "
                + $"{calculada.GetProperty(campo)} x {gravada.GetProperty(campo)}.");
        }

        Assert.Equal(
            resposta.RootElement.GetProperty("funcionarios").GetArrayLength(),
            relida.RootElement.GetProperty("funcionarios").GetArrayLength());
    }
}
