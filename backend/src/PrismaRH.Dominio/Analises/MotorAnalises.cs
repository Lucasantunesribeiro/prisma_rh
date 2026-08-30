namespace PrismaRH.Dominio.Analises;

/// <summary>
/// Roda o catalogo sobre uma folha e produz a execucao com os achados.
///
/// ## Deterministico, e por construcao
///
/// Mesmo retrato + mesma configuracao = mesma execucao, sempre. E o criterio de
/// aceite "execucao reproduzivel" da Fase 6, e ele nao depende de disciplina de
/// quem escreve regra: nada aqui consulta banco, le relogio por conta propria
/// ou depende de ordem de reflexao. A ordem e a do
/// <see cref="CatalogoRegras.Todas"/>, que e uma lista escrita a mao.
///
/// ## Onde mora o isolamento entre organizacoes
///
/// **Nao mora aqui, e isso e proposital.** Este motor recebe um retrato pronto,
/// e nao tem por onde perguntar nada ao banco. Quem monta o retrato consulta
/// sob o filtro global, entao uma regra nao consegue enxergar fora da
/// organizacao nem se a configuracao dela pedisse - ela nao tem a quem pedir.
///
/// E a resposta ao item 2 do Security Gate da Fase 6: o isolamento e
/// arquitetural, e nao uma conferencia que alguem precisa lembrar de fazer.
///
/// ## Uma regra que estoura nao derruba a execucao
///
/// Regra e codigo do sistema, e codigo do sistema tem defeito. Uma excecao numa
/// regra vira **um achado dizendo que ela falhou**, e as outras continuam. A
/// alternativa - deixar subir - transformaria um defeito numa regra em "a folha
/// nao pode ser analisada", que e uma indisponibilidade desproporcional ao
/// problema.
/// </summary>
public static class MotorAnalises
{
    /// <summary>
    /// Executa as regras ativas.
    /// </summary>
    /// <param name="configuracoes">
    /// A configuracao da organizacao, por codigo. Regra **sem** configuracao
    /// roda ativa, no padrao: organizacao nova nasce conferida
    /// (`CLAUDE.md secao 24.2`, secure by default).
    /// </param>
    public static ExecucaoAnalise Executar(
        Guid idOrganizacao,
        ContextoAnalise contexto,
        IReadOnlyDictionary<CodigoRegra, RegraAnalise> configuracoes,
        int versaoCalculoDaFolha,
        Guid idUsuario,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(configuracoes);

        var execucao = new ExecucaoAnalise(
            idOrganizacao,
            contexto.IdFolha,
            contexto.Competencia,
            versaoCalculoDaFolha,
            idUsuario,
            agora);

        foreach (var regra in CatalogoRegras.Todas)
        {
            configuracoes.TryGetValue(regra.Codigo, out var configuracao);

            if (configuracao is { Ativa: false })
            {
                continue;
            }

            var severidade = configuracao?.Severidade ?? regra.SeveridadePadrao;
            var valores = Interpretar(regra, configuracao);

            execucao.RegistrarExecucaoDe(regra);

            foreach (var achado in Rodar(regra, contexto, valores))
            {
                execucao.Registrar(regra, severidade, achado);
            }
        }

        return execucao;
    }

    /// <summary>
    /// Os parametros da regra, relidos e revalidados a cada execucao.
    ///
    /// Revalidar parece redundante - a gravacao ja validou -, mas o valor
    /// gravado por uma versao antiga do sistema pode estar fora da faixa
    /// declarada pela versao atual. Nesse caso ele cai no **padrao**, que e um
    /// numero conhecido, em vez de virar comportamento que ninguem consegue
    /// explicar.
    /// </summary>
    private static ValoresParametros Interpretar(IRegraAnalise regra, RegraAnalise? configuracao)
    {
        if (configuracao is null || regra.Parametros.Count == 0)
        {
            return ValoresParametros.Padrao(regra.Parametros);
        }

        var (valores, _) = ValoresParametros.Interpretar(
            regra.Parametros, configuracao.ValoresGravados());

        return valores;
    }

    /// <summary>
    /// Roda uma regra e embrulha a falha dela num achado.
    ///
    /// `internal` para que a suite consiga exercitar o caminho da excecao: o
    /// catalogo e fechado, entao nao ha como injetar uma regra defeituosa por
    /// caminho publico - e defesa sem teste e hipotese.
    /// </summary>
    internal static IReadOnlyList<Achado> Rodar(
        IRegraAnalise regra, ContextoAnalise contexto, ValoresParametros valores)
    {
        try
        {
            // ToList AQUI, dentro do try: as regras devolvem IEnumerable com
            // yield, e uma excecao so aconteceria na enumeracao - ou seja,
            // fora deste bloco, onde o catch nao alcanca.
            return [.. regra.Executar(contexto, valores)];
        }
        catch (Exception excecao)
        {
            return
            [
                new Achado(
                    $"A regra '{regra.Nome}' falhou e nao pode conferir esta folha. "
                    + "As demais regras foram executadas normalmente.",
                    Contexto: $"falha={excecao.GetType().Name}"),
            ];
        }
    }
}
