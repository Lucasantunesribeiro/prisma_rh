using System.Net;
using System.Text;

namespace PrismaRH.Testes.Integracoes;

/// <summary>
/// Faz o papel do servidor da BrasilAPI, sem rede.
///
/// Substitui apenas o ultimo elo - quem poe os bytes no fio. A guarda de
/// destino, o controle de redirect, o teto de corpo e o parsing continuam sendo
/// o codigo de producao, e sao eles que os testes exercitam.
/// </summary>
public sealed class ParceiroFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    private readonly List<Uri> _chamadas = [];

    /// <summary>As URLs que o cliente realmente pediu, na ordem.</summary>
    public IReadOnlyList<Uri> Chamadas => _chamadas;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage pedido,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(pedido);

        lock (_chamadas)
        {
            _chamadas.Add(pedido.RequestUri!);
        }

        var resposta = responder(pedido);

        // Deixa o cancelamento agir: e assim que o teste de prazo funciona sem
        // esperar os oito segundos de verdade.
        await Task.Delay(1, cancelamento);

        return resposta;
    }

    public static ParceiroFalso ComJson(string corpo, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(corpo, Encoding.UTF8, "application/json"),
        });

    public static ParceiroFalso ComStatus(HttpStatusCode status) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("{\"message\":\"...\"}", Encoding.UTF8, "application/json"),
        });

    /// <summary>Responde um redirect para o destino informado, uma unica vez.</summary>
    public static ParceiroFalso QueRedirecionaPara(string destino)
    {
        var jaRedirecionou = false;

        return new ParceiroFalso(_ =>
        {
            if (jaRedirecionou)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(RespostaValida, Encoding.UTF8, "application/json"),
                };
            }

            jaRedirecionou = true;

            var resposta = new HttpResponseMessage(HttpStatusCode.Found);
            resposta.Headers.Location = new Uri(destino, UriKind.RelativeOrAbsolute);

            return resposta;
        });
    }

    /// <summary>
    /// Uma resposta parecida com a de verdade - inclusive com os campos que o
    /// Prisma RH **nao** pode aproveitar: quadro societario com nome e CPF
    /// parcial de pessoa fisica, e-mail e telefone.
    /// </summary>
    public const string RespostaValida = """
        {
          "cnpj": "11222333000181",
          "razao_social": "INDUSTRIA EXEMPLO S.A.",
          "nome_fantasia": "EXEMPLO",
          "descricao_situacao_cadastral": "ATIVA",
          "email": "contato@exemplo.com.br",
          "ddd_telefone_1": "1130000000",
          "logradouro": "RUA DE EXEMPLO",
          "municipio": "SAO PAULO",
          "capital_social": 1000000,
          "qsa": [
            {
              "nome_socio": "FULANO DE TAL",
              "cnpj_cpf_do_socio": "***123456**",
              "faixa_etaria": "Entre 41 a 50 anos",
              "qualificacao_socio": "Diretor"
            }
          ]
        }
        """;
}
