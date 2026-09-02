using Npgsql;

namespace PrismaRH.Infraestrutura.Persistencia;

/// <summary>
/// Converte a connection string do Neon para o formato que o Npgsql entende.
///
/// ## Por que a conversao existe
///
/// O Neon entrega a conexao como **URI**:
///
/// <code>postgresql://usuario:senha@host/banco?sslmode=require&amp;channel_binding=require</code>
///
/// O Npgsql nao le URI - ele espera chave-valor (`Host=...;Database=...`).
/// Colar o URI direto no `UseNpgsql` produz um erro de formato que so aparece
/// na primeira conexao, e nao no build.
///
/// ## O endpoint do Neon gratuito e um PgBouncer
///
/// O host termina em `-pooler`, e isso significa **modo de pooling por
/// transacao**. Duas consequencias que mudam configuracao:
///
/// 1. **`Max Auto Prepare = 0`** (o padrao do Npgsql, preservado aqui de
///    proposito). Prepared statement do lado do servidor vive na conexao de
///    backend, e no modo transacao a proxima transacao pode cair noutra
///    conexao - o statement preparado "some" e a consulta falha, de forma
///    intermitente e dificil de reproduzir.
/// 2. **Lock consultivo precisa ser o de transacao**, nao o de sessao. Ver
///    <see cref="OrcamentoBlobs"/>.
///
/// ## Nada aqui imprime a string
///
/// Nenhum metodo devolve a senha, e nenhum log recebe a connection string
/// (`CLAUDE.md secao 24.15` e secao 33). O que se pode saber de fora e a
/// **forma**: se tem host, se exige SSL. O valor, nunca.
/// </summary>
public static class ConexaoNeon
{
    /// <summary>Nome da variavel de ambiente. O valor vive no `.env`, fora do Git.</summary>
    public const string Variavel = "PRISMARH_NEON_CONNECTION";

    /// <summary>
    /// Aceita URI ou chave-valor e devolve sempre chave-valor.
    ///
    /// Aceitar os dois formatos e proposital: quem configurar o ambiente pode
    /// colar o que o Neon deu, sem precisar saber que o .NET quer outra coisa.
    /// </summary>
    public static string Converter(string? entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada))
        {
            throw new ArgumentException("Connection string vazia.", nameof(entrada));
        }

        var texto = entrada.Trim();

        // Ja esta no formato do Npgsql: devolve como veio.
        if (!texto.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !texto.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return texto;
        }

        var uri = new Uri(texto);

        var partes = uri.UserInfo.Split(':', 2);

        if (partes.Length != 2 || string.IsNullOrEmpty(partes[0]))
        {
            throw new ArgumentException("URI do Neon sem usuario e senha.", nameof(entrada));
        }

        var construtor = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
            Username = Uri.UnescapeDataString(partes[0]),
            Password = Uri.UnescapeDataString(partes[1]),

            // O Neon so aceita TLS, e exigir aqui evita depender de o parametro
            // vir na query string. `VerifyFull` confere o certificado E o nome
            // do host - o modo que realmente protege contra man-in-the-middle;
            // `Require` sozinho criptografa sem verificar quem esta do outro lado.
            SslMode = SslMode.VerifyFull,

            // Timeouts curtos: uma Lambda com 60 s de teto nao pode gastar 15 s
            // esperando conexao, que e o padrao do Npgsql.
            Timeout = 10,
            CommandTimeout = 30,

            // ⚠️ Pool pequeno de proposito. O Neon gratuito tem limite de
            // conexoes, e a Lambda roda com concorrencia reservada 1 - abrir
            // dez conexoes por instancia desperdicaria a cota do banco sem
            // ganho nenhum.
            MaxPoolSize = 5,

            // ⚠️ Conexao ociosa e DESCARTADA rapido. Acrescentado em 02/09/2026,
            // depois de um 500 real em producao:
            //
            //   EndOfStreamException: Attempted to read past the end of the
            //   stream
            //
            // Nao era defeito da consulta. E o **Neon suspendendo a computacao
            // ociosa**: a conexao continua no pool parecendo viva do lado do
            // cliente e ja morreu do lado do servidor. A requisicao seguinte a
            // reusa e quebra. Repetir a mesma chamada devolveu 200 duas vezes -
            // falha transitoria, e nao erro de codigo.
            //
            // A defesa e nao guardar conexao velha: o pool poda o que passou de
            // 30 segundos parado, e a proxima requisicao abre uma nova. Custa
            // uma reconexao ocasional (~50 ms) e evita usar uma conexao morta.
            //
            // ⚠️ Nao resolve com `EnableRetryOnFailure`. A retentativa do EF
            // exige que TODA transacao explicita seja idempotente sob
            // reexecucao, e as tres deste projeto criam entidades - repeti-las
            // criaria **funcionario duplicado**. Trocar um 500 ocasional por
            // duplicata silenciosa em folha seria um pessimo negocio.
            ConnectionIdleLifetime = 30,
            ConnectionPruningInterval = 10,

            // Detecta cedo o outro lado que sumiu, em vez de esperar o TCP.
            KeepAlive = 15,
        };

        return construtor.ConnectionString;
    }

    /// <summary>
    /// Le a variavel de ambiente, ja convertida. Devolve `null` quando ausente
    /// - quem chama decide se isso e erro ou apenas "sem Neon configurado".
    /// </summary>
    public static string? DoAmbiente()
    {
        // ⚠️ O SSM tem PRECEDENCIA sobre a variavel direta.
        //
        // Em producao a variavel com o segredo deixou de existir - ela era
        // legivel por `lambda:GetFunctionConfiguration` (`CLAUDE.md secao 24.19
        // item 9`). A variavel direta sobrevive para desenvolvimento, testes e
        // Docker local, onde nao ha AWS nenhuma para consultar.
        var bruta = Producao.SegredosSsm.Ler(Producao.SegredosSsm.VariavelParametroBanco)
            ?? Environment.GetEnvironmentVariable(Variavel);

        return string.IsNullOrWhiteSpace(bruta) ? null : Converter(bruta);
    }

    /// <summary>
    /// Descreve a conexao **sem revelar nada**: host, banco e se exige TLS.
    ///
    /// Existe para o log e para a mensagem de diagnostico. Sem isto, alguem
    /// acabaria imprimindo a string inteira para "so conferir se esta certa" -
    /// e a senha iria junto para o CloudWatch, que tem retencao e acesso
    /// diferentes dos do cofre.
    /// </summary>
    public static string Descrever(string conexaoNpgsql)
    {
        var b = new NpgsqlConnectionStringBuilder(conexaoNpgsql);

        return $"host={b.Host} banco={b.Database} ssl={b.SslMode}";
    }
}
