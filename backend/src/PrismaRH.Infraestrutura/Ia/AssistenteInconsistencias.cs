using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PrismaRH.Dominio.Analises;
using PrismaRH.Infraestrutura.Persistencia;

namespace PrismaRH.Infraestrutura.Ia;

public sealed record ExplicacaoIa(
    SituacaoIa Situacao,
    string Texto,
    bool DoCache,
    int TokensUsados);

/// <summary>
/// Explica, em linguagem simples, uma inconsistência que o motor determinístico
/// já detectou (Fase 11).
///
/// ## O que entra no prompt, e o que fica de fora
///
/// `CLAUDE.md §37.6` — enviar dado pessoal a um provedor externo é decisão de
/// privacidade, não detalhe de implementação. Vale a **minimização**: só os
/// campos de que a explicação depende.
///
/// | Vai | Não vai |
/// |---|---|
/// | nome da regra, categoria, severidade | **CPF** |
/// | descrição que o motor gerou | data de nascimento, endereço |
/// | valores esperado/encontrado | matrícula, e-mail |
/// | competência e tipo de folha | nome do funcionário |
///
/// ⚠️ **O nome da pessoa não é enviado**, e a diferença é concreta: a
/// explicação de *"desligado em 20/07 e mesmo assim tem holerite"* não fica
/// pior sem o nome, e mandá-lo transformaria cada chamada numa transferência de
/// dado pessoal identificável para fora.
///
/// ## O cache tem a organização na chave, e o de CNPJ não tinha
///
/// Não é inconsistência. Lá o valor era **registro público da Receita**, igual
/// para quem perguntasse. Aqui o texto é derivado de dado do tenant — e cache
/// sem tenant na chave é vazamento com desempenho (`§24.5`).
/// </summary>
public sealed class AssistenteInconsistencias(ClienteGemini cliente, CacheExplicacoes cache)
{

    public bool Disponivel => cliente.Configurada;

    /// <summary>
    /// Explica um resultado de análise.
    ///
    /// O resultado é buscado **sob o filtro global** por quem chama: se o id
    /// for de outra organização, ele não é encontrado e a IA nunca é acionada.
    /// O isolamento é arquitetural, e não depende de o modelo se comportar
    /// (`§37.5`).
    /// </summary>
    public async Task<ExplicacaoIa> ExplicarAsync(
        ResultadoAnalise resultado,
        string nomeRegra,
        Guid idOrganizacao,
        Guid correlacao,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        if (!cliente.Configurada)
        {
            return new ExplicacaoIa(SituacaoIa.NaoConfigurada, string.Empty, false, 0);
        }

        // A chave inclui a organizacao E a versao da regra: se a regra mudar, a
        // explicacao velha deixa de valer.
        var chave = $"{idOrganizacao:N}:{resultado.Id:N}:{resultado.VersaoRegra}";

        if (cache.Buscar(chave) is { } guardado)
        {
            return new ExplicacaoIa(SituacaoIa.Respondeu, guardado, true, 0);
        }

        var resposta = await cliente.ExplicarAsync(
            "Voce ajuda um analista de RH brasileiro a entender uma divergencia "
            + "que o sistema de folha ja detectou. Explique em linguagem simples "
            + "o que a divergencia significa e o que conferir primeiro.",
            MontarDados(resultado, nomeRegra),
            correlacao,
            ct);

        if (resposta.Situacao == SituacaoIa.Respondeu)
        {
            cache.Guardar(chave, resposta.Texto);
        }

        return new ExplicacaoIa(resposta.Situacao, resposta.Texto, false, resposta.TokensUsados);
    }

    /// <summary>
    /// Monta o bloco de dados. **Campo a campo, escolhido a dedo** — nunca a
    /// entidade serializada inteira.
    ///
    /// Serializar a entidade seria mais curto de escrever e mandaria junto tudo
    /// o que ela ganhar no futuro: um campo novo com dado pessoal passaria a
    /// vazar sem ninguém decidir isso.
    /// </summary>
    private static string MontarDados(ResultadoAnalise r, string nomeRegra)
    {
        var linhas = new List<string>
        {
            $"Regra: {nomeRegra}",
            $"Categoria: {r.Categoria}",
            $"Severidade: {r.Severidade}",
            $"O que o sistema detectou: {r.Descricao}",
        };

        if (r.ValorEsperado is { } esperado)
        {
            linhas.Add($"Valor esperado: R$ {esperado:N2}");
        }

        if (r.ValorEncontrado is { } encontrado)
        {
            linhas.Add($"Valor encontrado: R$ {encontrado:N2}");
        }

        if (r.Diferenca is { } diferenca)
        {
            linhas.Add($"Diferenca: R$ {diferenca:N2}");
        }

        // Nome, CPF e matricula NAO entram. Ver o quadro na documentacao da
        // classe: a explicacao nao fica pior sem eles, e manda-los transformaria
        // cada chamada numa transferencia de dado pessoal para fora.
        return string.Join("\n", linhas);
    }

}

/// <summary>
/// Guarda as explicacoes ja geradas.
///
/// Classe propria e **singleton** porque cache por requisicao nao guarda nada.
/// O assistente, que depende do `HttpClient` tipado, continua transitorio - um
/// singleton segurando `HttpClient` tipado e dependencia cativa: ele nunca
/// receberia o handler renovado, e a conexao envelheceria sem ninguem notar.
/// </summary>
public sealed class CacheExplicacoes : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions
    {
        SizeLimit = OrcamentoIa.MaximoEntradasCache,
    });

    public string? Buscar(string chave) =>
        _cache.TryGetValue(chave, out string? texto) ? texto : null;

    public void Guardar(string chave, string texto) =>
        _cache.Set(chave, texto, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = OrcamentoIa.ValidadeCache,
        });

    public void Dispose() => _cache.Dispose();
}
