namespace PrismaRH.Dominio.Identidade;

/// <summary>
/// O bloqueio progressivo **por conta**.
///
/// ## Por que ele existe, se já há limite por IP
///
/// O limite por IP da Fase 10 corta *um IP tentando muitas contas*. Ele não vê
/// o ataque inverso — *muitos IPs tentando uma conta* —, que é a forma do
/// **credential stuffing distribuído**: mil máquinas, dez tentativas cada,
/// nenhuma delas perto do limite de 10/min por IP.
///
/// O `CLAUDE.md §24.18` pede os dois lados de propósito: *"Só por IP não protege
/// contra credential stuffing distribuído; só por usuário não protege contra
/// tentativa espalhada por muitos e-mails."*
///
/// ## ⚠️ O bloqueio NUNCA é permanente, e isso é o desenho
///
/// Bloqueio que exige alguém destravar transforma a defesa numa **arma do
/// atacante**: sabendo o e-mail de alguém, ele erra a senha cinco vezes e tranca
/// a pessoa fora do sistema. A defesa vira negação de serviço.
///
/// Por isso aqui:
///
/// - o bloqueio **expira sozinho**, sem intervenção de ninguém;
/// - ele é **progressivo** — cresce a cada falha e para de crescer num teto;
/// - **um acerto zera tudo**, então a vítima recupera o acesso sabendo a senha.
///
/// O efeito prático é o que se quer: um humano que errou a senha três vezes
/// quase não percebe; um script que tenta mil senhas leva anos.
///
/// ## Por que a espera dobra
///
/// Progressão geométrica cresce rápido o bastante para inviabilizar automação
/// nas primeiras dezenas de tentativas, e o teto impede que ela vire bloqueio
/// eterno na prática. Sem o teto, a décima falha já daria mais de quatro horas.
/// </summary>
public static class PoliticaBloqueioConta
{
    /// <summary>
    /// Quantas falhas passam sem espera nenhuma.
    ///
    /// Três é a folga de quem digitou errado, trocou o layout do teclado ou
    /// tentou a senha antiga. Menos que isso castiga o usuário legítimo; muito
    /// mais dá corda ao script.
    /// </summary>
    public const int FalhasToleradas = 3;

    /// <summary>A primeira espera, depois das toleradas.</summary>
    public static readonly TimeSpan EsperaInicial = TimeSpan.FromSeconds(30);

    /// <summary>
    /// O teto da espera.
    ///
    /// ⚠️ Existe para o bloqueio **não** virar permanente na prática. Quinze
    /// minutos reduzem um ataque a algumas dezenas de tentativas por dia — e
    /// devolvem o acesso a quem só errou a senha.
    /// </summary>
    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Quanto tempo sem falhar faz o contador ser esquecido.
    ///
    /// Sem isso, três erros espalhados por seis meses somariam com o quarto e
    /// bloqueariam alguém que nunca foi atacado.
    /// </summary>
    public static readonly TimeSpan JanelaDeEsquecimento = TimeSpan.FromHours(1);

    /// <summary>
    /// A espera depois de <paramref name="falhas"/> falhas consecutivas.
    ///
    /// Devolve <see cref="TimeSpan.Zero"/> enquanto estiver dentro das
    /// toleradas.
    /// </summary>
    public static TimeSpan EsperaApos(int falhas)
    {
        if (falhas <= FalhasToleradas)
        {
            return TimeSpan.Zero;
        }

        var dobras = falhas - FalhasToleradas - 1;

        // Teto na potencia ANTES de multiplicar: `2^60` estoura o `double` e
        // vira infinito, e `TimeSpan.FromSeconds(infinito)` lanca excecao. O
        // limite de 20 dobras ja passa de qualquer teto razoavel.
        var fator = Math.Pow(2, Math.Min(dobras, 20));

        var espera = TimeSpan.FromSeconds(EsperaInicial.TotalSeconds * fator);

        return espera > EsperaMaxima ? EsperaMaxima : espera;
    }
}
