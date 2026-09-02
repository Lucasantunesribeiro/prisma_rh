using System.Net;
using System.Net.Http.Json;
using PrismaRH.Testes.Isolamento;

namespace PrismaRH.Testes.Seguranca;

/// <summary>
/// Rejeição estrita de propriedades JSON desconhecidas.
///
/// ## O defeito que isto corrige
///
/// O `CLAUDE.md §24.7` promete: *"payload inesperado é rejeitado, não ignorado
/// em silêncio"*. Até **02/09/2026 o código não cumpria** — um teste contra a
/// produção provou que
/// `{"codigo":"X","nome":"Y","campoQueNaoExiste":1}` devolvia **201**.
///
/// Documento e código divergiam, e quem estava errado era o código. A correção
/// é uma linha em `Program.cs`:
/// `SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow`.
///
/// ## Por que ignorar era pior do que parece
///
/// Não é vulnerabilidade — os records de entrada não têm `Id` nem
/// `IdOrganizacao`, então overposting já estava fechado. O problema é de
/// **diagnóstico**: um cliente que erra o nome de um campo — `basesIncidentes`
/// virando `baseIncidente` — recebia **201** e acreditava ter configurado algo
/// que o servidor descartou. O erro aparecia depois, como dado faltando, longe
/// da causa.
///
/// ⚠️ E há um ganho de segurança colateral: a tentativa clássica de overposting
/// deixou de ser um no-op silencioso e passou a **morrer na porta**. Quem sonda
/// recebe uma recusa explícita em vez de um 201 que exige conferir depois onde
/// o registro caiu.
/// </summary>
[Collection(ColecaoApi.Nome)]
public class PayloadEstritoTestes(BancoPostgresFixture banco)
{
    private static int _sufixo;

    private static string Codigo() =>
        "PE" + Interlocked.Increment(ref _sufixo).ToString("D4");

    private async Task<HttpResponseMessage> CriarCargoAsync(object corpo)
    {
        var fabrica = new FabricaApiIsolada(banco.StringConexao);
        var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        return await cliente.PostAsJsonAsync("/api/cargos", corpo);
    }

    /// <summary>O contrato correto continua funcionando.</summary>
    [Fact]
    public async Task DtoValidoContinuaSendoAceito()
    {
        using var r = await CriarCargoAsync(new { codigo = Codigo(), nome = "Cargo valido" });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    /// <summary>⚠️ **O teste que o defeito exigia.**</summary>
    [Fact]
    public async Task CampoExtraNoTopoERecusadoCom400()
    {
        using var r = await CriarCargoAsync(new
        {
            codigo = Codigo(),
            nome = "Cargo com intruso",
            campoQueNaoExiste = 1,
        });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    /// <summary>
    /// Campo com o nome **quase** certo — o caso real que motivou a mudança.
    /// Antes devolvia 201 e descartava a intenção do cliente.
    /// </summary>
    [Fact]
    public async Task CampoComNomeQuaseCertoERecusado()
    {
        using var r = await CriarCargoAsync(new
        {
            codigo = Codigo(),
            Nome = "maiuscula errada nao e o caso",
            nomee = "Cargo",
        });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    /// <summary>
    /// ⚠️ **Campo aninhado: o caso não existe nesta API, e isso é uma resposta.**
    ///
    /// Todos os records de entrada são **planos** — nenhum tem um record dentro.
    /// Inventar um corpo aninhado só para testar provaria que o teste consegue
    /// inventar, não que o produto rejeita.
    ///
    /// O que existe de "estrutura" é `Dictionary&lt;string, string?&gt; Parametros`
    /// na configuração de regra, e ali a distinção importa: **chave de
    /// dicionário é DADO, não membro desconhecido**. Uma chave nova é um
    /// parâmetro que o cliente está informando, e o `Disallow` não a rejeita —
    /// corretamente. Quem valida parâmetro de regra é o domínio, contra a
    /// `DefinicaoParametro` que cada regra declara.
    ///
    /// Este teste trava essa fronteira: dicionário aceita chave nova, o objeto
    /// que o contém não aceita campo novo.
    /// </summary>
    [Fact]
    public async Task ChaveDeDicionarioEDadoMasCampoDoObjetoNaoE()
    {
        var fabrica = new FabricaApiIsolada(banco.StringConexao);
        var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        // Chave desconhecida DENTRO do dicionario: nao e membro desconhecido.
        // Passa pela serializacao e e o dominio quem decide.
        using var comChaveNova = await cliente.PutAsJsonAsync(
            "/api/regras-analise/VariacaoSalarial",
            new
            {
                ativa = true,
                severidade = "Baixa",
                parametros = new Dictionary<string, string?> { ["chaveInventada"] = "1" },
            });

        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, comChaveNova.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, comChaveNova.StatusCode);

        // Campo desconhecido no OBJETO: recusado.
        using var comCampoNovo = await cliente.PutAsJsonAsync(
            "/api/regras-analise/VariacaoSalarial",
            new { ativa = true, severidade = "Baixa", campoIntruso = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, comCampoNovo.StatusCode);
    }

    [Fact]
    public async Task JsonMalformadoERecusadoCom400()
    {
        var fabrica = new FabricaApiIsolada(banco.StringConexao);
        var cliente = await fabrica.ClienteComoAsync(BancoPostgresFixture.EmailAdminA);

        using var conteudo = new StringContent(
            "{isto nao e json", System.Text.Encoding.UTF8, "application/json");

        using var r = await cliente.PostAsync("/api/cargos", conteudo);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task TipoInvalidoERecusadoCom400()
    {
        using var r = await CriarCargoAsync(new { codigo = 123, nome = true });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    /// <summary>
    /// ⚠️ A recusa **não devolve o trecho do JSON**.
    ///
    /// A mensagem do `System.Text.Json` cita a propriedade e às vezes o
    /// conteúdo — que é entrada não confiável e pode carregar dado pessoal. A
    /// pendência `§24.19 item 4` exigiu essa ausência, e ela continua valendo
    /// para este caminho novo.
    /// </summary>
    [Fact]
    public async Task ARecusaNaoEcoaOConteudoEnviado()
    {
        const string Marcador = "MARCADOR-QUE-NAO-PODE-VOLTAR";

        using var r = await CriarCargoAsync(new
        {
            codigo = Codigo(),
            nome = "x",
            campoIntruso = Marcador,
        });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);

        var corpo = await r.Content.ReadAsStringAsync();

        Assert.DoesNotContain(Marcador, corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("campoIntruso", corpo, StringComparison.Ordinal);
    }
}
