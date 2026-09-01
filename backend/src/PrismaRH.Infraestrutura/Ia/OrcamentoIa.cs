namespace PrismaRH.Infraestrutura.Ia;

/// <summary>
/// Os tetos que mantêm a camada de IA barata **por construção**.
///
/// ## Por que isto existe, e por que é mais rígido que os outros orçamentos
///
/// `CLAUDE.md §37.7`: nenhuma implementação de IA pode gerar custo recorrente
/// sem análise e autorização explícitas. E `§37.9` nomeia a ameaça própria
/// desta camada:
///
/// > *"Custo abusivo — cobrança por token torna o abuso lucrativo para quem
/// > ataca e caro para quem mantém."*
///
/// Um endpoint de IA sem teto é diferente de um endpoint lento sem teto: o
/// segundo consome CPU que já foi paga; o primeiro **imprime fatura**. Cada
/// requisição extra tem preço marginal real.
///
/// ⚠️ **Não foi possível confirmar se o projeto do Gemini tem faturamento
/// ativado** — a API não informa, e doze chamadas seguidas não bateram no
/// limite gratuito de 10/min, o que sugere faturamento ligado. Por isso os
/// números abaixo são dimensionados para o **pior caso**: mesmo com cobrança
/// por token, o gasto mensal fica em centavos.
/// </summary>
public static class OrcamentoIa
{
    /// <summary>
    /// `gemini-3.5-flash-lite` — o mais barato da família.
    ///
    /// A tarefa desta fase é **explicar um resultado que o C# já produziu**, em
    /// linguagem simples. Não há raciocínio complexo, não há cálculo, não há
    /// decisão. Um modelo maior custaria mais para fazer a mesma coisa pior:
    /// texto mais longo é texto que o analista não lê.
    ///
    /// ⚠️ **A versão 2.5-lite foi escolhida primeiro e não funciona**: o
    /// provedor devolve `404` com *"no longer available to new users"* e indica
    /// este substituto. O modelo aparecia normalmente na listagem de modelos —
    /// só a chamada real revelou a aposentadoria, e é por isso que a fase tem
    /// verificação contra o provedor de verdade, e não apenas contra dublês.
    ///
    /// Consequência prática: **o nome do modelo é um parâmetro que envelhece
    /// sozinho**, sem ninguém mexer no código. Quando ele cair, o produto
    /// continua de pé — a resposta vira `Indisponivel`, a tela mostra o aviso e
    /// o achado do motor determinístico permanece legível.
    /// </summary>
    public const string Modelo = "gemini-3.5-flash-lite";

    /// <summary>
    /// **300 tokens de saída.** Uma explicação de inconsistência cabe em três
    /// frases; o resumo de folha, em um parágrafo.
    ///
    /// O teto de saída é o controle de custo mais direto que existe: a entrada
    /// é montada por nós e tem tamanho conhecido, mas a saída é o modelo que
    /// decide — e sem teto ele decide por um ensaio.
    /// </summary>
    public const int MaximoTokensSaida = 300;

    /// <summary>
    /// **4.000 caracteres de entrada.** O contexto é montado pelo servidor a
    /// partir de campos escolhidos a dedo, então na prática fica bem abaixo.
    /// O teto existe para o caso em que um dado inesperadamente longo — uma
    /// justificativa de mil palavras — inflaria a conta sem ninguém notar.
    /// </summary>
    public const int MaximoCaracteresEntrada = 4_000;

    /// <summary>
    /// **20 chamadas por hora, por organização.**
    ///
    /// Por organização, e não por IP: aqui o usuário **já está autenticado**, e
    /// o recurso protegido é uma cota compartilhada — mesma lógica da consulta
    /// de CNPJ da Fase 8, e o oposto do login, onde ainda não há usuário.
    ///
    /// Vinte por hora cobre um analista trabalhando; não cobre um script. E
    /// como a explicação é **guardada em cache**, reabrir a mesma inconsistência
    /// não consome nada.
    /// </summary>
    public const int MaximoChamadasPorHora = 20;

    /// <summary>
    /// **12 segundos.** Uma explicação curta volta em 1 a 3 s. O prazo existe
    /// para o caso que não volta: sem ele, o provedor lento segura a requisição
    /// do usuário e a conexão junto.
    /// </summary>
    public static readonly TimeSpan Prazo = TimeSpan.FromSeconds(12);

    /// <summary>
    /// **24 horas de cache.**
    ///
    /// A explicação depende do resultado da análise, que **não muda** — um
    /// achado é registro do que foi visto naquele momento (`§4.3`). Gerar de
    /// novo custaria dinheiro para produzir texto equivalente.
    ///
    /// A chave inclui a organização: aqui o valor **é derivado de dado do
    /// tenant**, ao contrário do cache de CNPJ, cujo conteúdo é registro
    /// público. Compartilhar entre organizações seria vazamento
    /// (`CLAUDE.md §24.5`).
    /// </summary>
    public static readonly TimeSpan ValidadeCache = TimeSpan.FromHours(24);

    /// <summary>Teto de entradas do cache, para ele não virar vetor de memória.</summary>
    public const int MaximoEntradasCache = 500;

    /// <summary>
    /// Custo estimado no pior caso, para o relatório e para quem revisar.
    ///
    /// Com o teto de 20 chamadas/hora por organização, duas organizações e uso
    /// de portfólio (dezenas de chamadas por mês), o gasto fica na casa de
    /// **centavos de dólar por mês** mesmo com faturamento ativo.
    /// </summary>
    public const string CustoEstimado =
        "< US$ 0,05/mes em uso de portfolio, mesmo com faturamento ativo no projeto Google.";
}
