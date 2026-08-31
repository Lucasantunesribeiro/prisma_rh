using Microsoft.Extensions.Caching.Memory;
using PrismaRH.Dominio.Empresas;

namespace PrismaRH.Infraestrutura.Integracoes;

/// <summary>
/// Guarda por pouco tempo o que a Receita respondeu sobre um CNPJ.
///
/// ## Por que a chave NAO tem organizacao
///
/// O `CLAUDE.md secao 24.5` manda por o tenant na chave de cache, e a regra
/// esta certa: cache sem tenant na chave e vazamento com desempenho.
///
/// Ela vale para dado **do tenant**. Aqui nao ha dado de tenant nenhum:
///
/// - a **chave** e um CNPJ que a pessoa digitou, e nao um identificador do
///   sistema;
/// - o **valor** e registro publico da Receita Federal, igual para quem
///   perguntar;
/// - o cache guarda o que a Receita respondeu, e nao o que alguma organizacao
///   fez com a resposta.
///
/// Por em a organizacao na chave nao protegeria nada e desligaria o cache na
/// pratica - cada organizacao buscaria de novo o mesmo registro publico.
///
/// ⚠️ **O que isso expoe, dito sem enfeite:** um acerto de cache responde mais
/// rapido que uma busca. Quem medir o tempo com precisao consegue supor que
/// alguem consultou aquele CNPJ ha pouco. Nao diz **quem** - nem a organizacao,
/// nem o usuario -, e o mesmo registro esta publicamente disponivel na
/// BrasilAPI para qualquer um. O risco residual foi aceito, e esta escrito aqui
/// em vez de descoberto depois.
///
/// ## Fracasso nao entra
///
/// So `Encontrada` e `NaoEncontrada` sao guardados. Guardar `Indisponivel`
/// faria a queda do parceiro sobreviver ao proprio fim: ele voltaria ao ar e o
/// Prisma RH continuaria dizendo que esta fora pelos dez minutos seguintes.
/// </summary>
public sealed class CacheConsultaCnpj : IDisposable
{
    /// <summary>
    /// Curto de proposito. O dado da Receita muda devagar, mas o cadastro que
    /// depende dele e feito uma vez e vale por anos - dez minutos absorvem o
    /// clique duplo e a tentativa repetida sem que alguem cadastre empresa a
    /// partir de uma resposta velha.
    /// </summary>
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Teto de entradas. Sem ele, consultar muitos CNPJs distintos e um jeito
    /// educado de encher a memoria do servidor - o proprio cache viraria o
    /// vetor de exaustao que o item 11 do Security Gate manda fechar.
    /// </summary>
    public const int MaximoEntradas = 500;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaximoEntradas });

    /// <summary>
    /// Devolve o resultado e **se ele veio do cache**.
    ///
    /// O segundo valor nao e curiosidade: a auditoria da Fase 8 responde "o que
    /// este sistema ja contou a terceiros?", e num acerto de cache **nada saiu
    /// da nossa rede**. Registrar as duas situacoes com a mesma frase faria a
    /// trilha afirmar um envio que nao houve.
    /// </summary>
    public async Task<(ResultadoConsultaCnpj Resultado, bool DoCache)> ObterAsync(
        Cnpj cnpj,
        Func<Task<ResultadoConsultaCnpj>> buscar)
    {
        ArgumentNullException.ThrowIfNull(buscar);

        if (_cache.TryGetValue(cnpj.Valor, out ResultadoConsultaCnpj? guardado) && guardado is not null)
        {
            return (guardado, true);
        }

        var resultado = await buscar();

        if (resultado.Situacao is SituacaoConsulta.Encontrada or SituacaoConsulta.NaoEncontrada)
        {
            _cache.Set(
                cnpj.Valor,
                resultado,
                new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Validade });
        }

        return (resultado, false);
    }

    public void Dispose() => _cache.Dispose();
}
