namespace PrismaRH.Dominio.Importacao;

/// <summary>
/// Os tetos que mantem a Fase 9 em **custo previsto de US$ 0,00**.
///
/// ## A decisao, registrada em 31/08/2026
///
/// A conta AWS do portfolio saiu do plano gratuito para o plano pago - habilitar
/// o IAM Identity Center criou uma AWS Organizations, e entrar numa organizacao
/// e um dos gatilhos documentados de upgrade automatico. Os US$ 100 de credito
/// viraram US$ 0,00.
///
/// O responsavel determinou: **zero custo AWS e requisito arquitetural do
/// portfolio, e servico que cobra por existir nao entra**. Isso nao inviabiliza
/// a fase - exige escolher os servicos certos e dimensionar dentro deles.
///
/// ## Franquia permanente nao e o mesmo que "provavelmente cabe"
///
/// Free Tier **nao e teto de gasto**: passar da franquia nao bloqueia nada,
/// so passa a cobrar. Por isso cada numero abaixo e um **limite tecnico** que
/// torna a ultrapassagem improvavel, e nao uma expectativa otimista.
///
/// | Servico | Franquia permanente | Guardrail aplicado |
/// |---|---|---|
/// | Lambda | 1 M req + 400.000 GB-s/mes | 128 MB, 60 s, concorrencia reservada 1 |
/// | SQS | 1 M requisicoes/mes | long polling de 20 s, sem `ScalingConfig` |
/// | CloudWatch Logs | 5 GB ingestao + 5 GB armazenados | retencao de 7 dias, log minimo |
///
/// ⚠️ **O `ScalingConfig` do event source mapping fica DESLIGADO de proposito.**
/// A documentacao da AWS e explicita: com a fila parada a Lambda reduz os
/// pollers "a ate 2, para reduzir as chamadas ao SQS e o custo correspondente"
/// - *"porem essa otimizacao nao esta disponivel quando voce habilita o ajuste
/// de concorrencia maxima"*. Ligar o `ScalingConfig` prenderia em 5 pollers e
/// quase dobraria o consumo ocioso da franquia de SQS, sem nenhum ganho aqui.
/// O limite de concorrencia vem da **funcao** (`reserved concurrency`), que e
/// gratuito e nao mexe nos pollers.
/// </summary>
public static class OrcamentoSemCusto
{
    // ------------------------------------------------------- blobs temporarios

    /// <summary>
    /// **5 MB por arquivo.**
    ///
    /// Mesmo teto da Fase 5 - uma planilha de RH com 10.000 linhas nao chega
    /// perto disso.
    /// </summary>
    public const int TamanhoMaximoArquivoBytes = 5 * 1024 * 1024;

    /// <summary>
    /// **50 MB de blobs temporarios somados em TODA a aplicacao.**
    ///
    /// ⚠️ **Global, e nao por organizacao** - e a distincao e a correcao mais
    /// importante deste arquivo.
    ///
    /// O limite do Neon gratuito e **por projeto**, e nao por tenant: 0,5 GB
    /// para o banco inteiro. Um teto "de 50 MB por organizacao" seria ilusao
    /// aritmetica - dez organizacoes o multiplicariam por dez e estourariam o
    /// projeto, cada uma respeitando o proprio limite.
    ///
    /// 50 MB e 10% do projeto. Os outros 90% sao do produto: funcionarios,
    /// contratos, folhas, holerites, lancamentos e a trilha de auditoria, que
    /// **so cresce**.
    ///
    /// **O isolamento entre organizacoes nao muda por causa disso.** O
    /// orcamento e compartilhado; os dados nao. Uma organizacao pode ocupar o
    /// espaco que impede a outra de importar naquele instante - e vai ver uma
    /// mensagem dizendo isso -, mas nunca **le** um byte da vizinha: os blobs
    /// continuam sob o filtro global.
    /// </summary>
    public const long ArmazenamentoGlobalMaximoBytes = 50L * 1024 * 1024;

    /// <summary>
    /// **7 dias, e antes disso a conclusao.**
    ///
    /// Os bytes sao apagados **assim que o processamento termina** - concluido
    /// ou falho. Os 7 dias sao a rede de seguranca para o que ficou preso no
    /// meio do caminho: worker morto, mensagem perdida, trabalho orfao.
    ///
    /// Nao e so espaco. A planilha tem **CPF e salario** de gente de verdade
    /// (`CLAUDE.md secao 24.13`, altamente sensivel), e dado que nao precisa
    /// mais existir nao deve continuar existindo. Custo e minimizacao apontam
    /// para a mesma decisao.
    /// </summary>
    public static readonly TimeSpan RetencaoBlob = TimeSpan.FromDays(7);

    // ------------------------------------------------------------------- fila

    /// <summary>
    /// **8 KB de mensagem.**
    ///
    /// O limite duro da SQS e 256 KB; este e trinta vezes menor de proposito.
    /// A mensagem carrega **identificadores**, nunca o arquivo.
    ///
    /// Se o conteudo coubesse, alguem acabaria colocando - e o dado pessoal
    /// passaria a existir tambem na fila e na DLQ, que tem retencao e controle
    /// de acesso proprios. Um teto pequeno torna a tentacao impossivel.
    /// </summary>
    public const int TamanhoMaximoMensagemBytes = 8 * 1024;

    /// <summary>
    /// **20 segundos de long polling** - o maximo que a SQS aceita.
    ///
    /// Este e o guardrail de custo mais importante da fila. Com polling curto,
    /// o poller da Lambda pergunta "tem mensagem?" varias vezes por segundo, e
    /// **cada pergunta e uma requisicao cobrada** mesmo com a fila vazia. Com
    /// 20 s, uma fila parada consome cerca de 260 mil requisicoes por mes -
    /// pouco mais de um quarto da franquia permanente de 1 milhao.
    /// </summary>
    public const int EsperaLongPollingSegundos = 20;

    /// <summary>
    /// **3 tentativas, depois a DLQ.**
    ///
    /// Retry sem teto e o caminho mais curto para um defeito virar despesa: a
    /// mensagem que sempre falha volta para a fila para sempre, e cada volta
    /// consome invocacao, GB-segundo e requisicao de fila.
    /// </summary>
    public const int MaximoTentativas = 3;

    /// <summary>
    /// **14 dias na DLQ.**
    ///
    /// Tempo de sobra para alguem olhar o que falhou, e prazo definido para nao
    /// virar deposito. `CLAUDE.md secao 24.13`: dado sensivel nao fica
    /// indefinidamente em fila, DLQ, log ou arquivo temporario.
    /// </summary>
    public const int RetencaoDlqDias = 14;

    // ----------------------------------------------------------------- lambda

    /// <summary>
    /// **128 MB** - o menor tamanho que a Lambda aceita.
    ///
    /// Serve a dois propositos ao mesmo tempo. Na franquia: 400.000 GB-s
    /// dividido por 0,125 GB da **3,2 milhoes de segundos** por mes de graca.
    /// E como teto fisico: uma funcao pequena nao consegue consumir muito, mesmo
    /// se algo der errado - o guardrail nao depende de ninguem se lembrar dele.
    /// </summary>
    public const int MemoriaLambdaMb = 128;

    /// <summary>
    /// **60 segundos.**
    ///
    /// Uma importacao de 10.000 linhas leva segundos. O timeout existe para o
    /// caso que **nao termina**: sem ele a funcao roda ate o teto de 15 minutos,
    /// e e assim que uma mensagem envenenada vira conta.
    /// </summary>
    public const int TimeoutLambdaSegundos = 60;

    /// <summary>
    /// **Concorrencia reservada 1.**
    ///
    /// Uma execucao por vez, no maximo. E o guardrail que transforma "consumo
    /// provavelmente pequeno" em **teto fisico**: mesmo uma funcao
    /// continuamente ocupada gasta no maximo 1 x 0,125 GB x 60 s por minuto,
    /// e isso e previsivel de calcular em vez de torcer.
    ///
    /// Nao confundir com `provisioned concurrency`, que **e paga** e nao e
    /// usada aqui. Reserved concurrency e gratuita.
    /// </summary>
    public const int ConcorrenciaReservada = 1;

    /// <summary>
    /// **360 segundos de visibility timeout** - seis vezes o timeout da funcao.
    ///
    /// E a proporcao que a AWS recomenda. Se fosse igual ao timeout, uma
    /// execucao lenta veria a mensagem reaparecer na fila **antes de terminar**,
    /// e o mesmo arquivo seria processado duas vezes ao mesmo tempo. A
    /// idempotencia protegeria o resultado, mas o trabalho dobrado ainda
    /// consome franquia.
    /// </summary>
    public const int VisibilityTimeoutSegundos = TimeoutLambdaSegundos * 6;

    // ------------------------------------------------------------ cloudwatch

    /// <summary>
    /// **7 dias de log.**
    ///
    /// O grupo de log nasce com retencao **infinita** se ninguem definir - e e
    /// assim que a franquia de 5 GB e atravessada meses depois, sem nada ter
    /// mudado no sistema.
    ///
    /// Sete dias respondem "por que a importacao de ontem falhou", que e a
    /// unica pergunta do log tecnico. O que precisa durar anos e a **trilha de
    /// auditoria**, que vive no banco (`CLAUDE.md secao 26`).
    /// </summary>
    public const int RetencaoLogsDias = 7;

    // ------------------------------------------------------------------ contas

    /// <summary>Quantos arquivos no teto cabem no orcamento global, ao mesmo tempo.</summary>
    public static int ArquivosNoTeto =>
        (int)(ArmazenamentoGlobalMaximoBytes / TamanhoMaximoArquivoBytes);

    /// <summary>
    /// Cabe mais um arquivo deste tamanho, dado o que a aplicacao **inteira** ja
    /// guarda?
    ///
    /// Funcao pura: quem chama e responsavel por obter o total ja armazenado de
    /// forma segura contra concorrencia - duas requisicoes simultaneas que leem
    /// o mesmo total e ambas decidem que cabe estourariam o teto juntas. A
    /// serializacao mora no repositorio, com lock consultivo no PostgreSQL.
    /// </summary>
    public static bool CabeNoOrcamentoGlobal(long jaArmazenadoBytes, long novoArquivoBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(jaArmazenadoBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(novoArquivoBytes);

        if (novoArquivoBytes > TamanhoMaximoArquivoBytes)
        {
            return false;
        }

        return jaArmazenadoBytes + novoArquivoBytes <= ArmazenamentoGlobalMaximoBytes;
    }
}
