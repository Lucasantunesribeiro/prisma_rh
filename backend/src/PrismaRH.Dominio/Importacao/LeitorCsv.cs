using System.Text;

namespace PrismaRH.Dominio.Importacao;

/// <summary>
/// Leitura de CSV, escrita a mao e sem dependencia externa.
///
/// Segue a **RFC 4180** no que importa: campo entre aspas pode conter o
/// delimitador, quebra de linha e aspas duplicadas (`""`).
///
/// ## Por que sem biblioteca
///
/// Decisao registrada: CSV e texto delimitado, e o parser inteiro cabe num
/// arquivo que se le numa sentada. O `CLAUDE.md secao 24.25` manda nao instalar
/// biblioteca para funcionalidade trivial - cada dependencia e superficie de
/// ataque, e a maior parte dos incidentes de supply chain entra por pacote
/// pequeno que ninguem revisa. XLSX e outra historia: e ZIP com XML e esquema
/// proprio, e ali a biblioteca (ClosedXML) se justifica.
///
/// ## Seguranca
///
/// - **Nao recebe caminho de arquivo, so <see cref="Stream"/>.** Path traversal
///   nao e mitigado aqui: ele e IMPOSSIVEL por construcao, porque esta classe
///   nao sabe o que e um caminho.
/// - **Os limites valem durante a leitura**, nao depois. Ver
///   <see cref="LimitesImportacao"/>.
/// - **Nada e avaliado.** Um campo com `=SOMA(A1:A9)` e a string
///   `"=SOMA(A1:A9)"`, e nada mais. O perigo de formula nao esta em ler: esta
///   em ESCREVER um arquivo que alguem vai abrir no Excel - e isso e tratado em
///   <see cref="ProtecaoCsv"/>, na exportacao.
/// - **Erro vira relatorio, nao excecao.** Arquivo de usuario e a entrada menos
///   confiavel que existe; estourar excecao em cada aspas desbalanceada
///   transformaria conteudo malformado em 500.
///
/// Funcao pura: sem banco, sem relogio, sem HTTP (`CLAUDE.md secao 10`).
/// </summary>
public static class LeitorCsv
{
    /// <summary>
    /// Ponto e virgula, e nao virgula.
    ///
    /// O Excel em portugues do Brasil usa a virgula como separador DECIMAL, e
    /// por isso exporta CSV com ponto e virgula. Adotar a virgula como padrao
    /// quebraria todo arquivo gerado pelo caminho mais comum - "Salvar como
    /// CSV" numa maquina brasileira.
    /// </summary>
    public const char DelimitadorPadrao = ';';

    /// <summary>
    /// Le o conteudo e devolve cabecalho, linhas e erros.
    ///
    /// Nunca lanca por causa do CONTEUDO. Lanca apenas por argumento nulo, que
    /// e defeito de quem chama, e nao dado de usuario.
    /// </summary>
    public static ResultadoLeitura Ler(
        Stream conteudo,
        LimitesImportacao? limites = null,
        char delimitador = DelimitadorPadrao)
    {
        ArgumentNullException.ThrowIfNull(conteudo);

        limites ??= LimitesImportacao.Padrao;

        if (delimitador is '"' or '\r' or '\n')
        {
            throw new ArgumentException(
                "Delimitador nao pode ser aspas nem quebra de linha.", nameof(delimitador));
        }

        if (!LerComTeto(conteudo, limites.TamanhoMaximoBytes, out var bytes))
        {
            var megabytes = limites.TamanhoMaximoBytes / (1024d * 1024d);

            return ResultadoLeitura.Falha(new ErroImportacao(
                0, null,
                $"Arquivo maior que o limite de {megabytes:N1} MB."));
        }

        if (bytes.Length == 0)
        {
            return ResultadoLeitura.Falha(new ErroImportacao(0, null, "Arquivo vazio."));
        }

        return Analisar(Decodificar(bytes), limites, delimitador);
    }

    /// <summary>Atalho para teste e para conteudo que ja esta em memoria.</summary>
    public static ResultadoLeitura Ler(
        string conteudo,
        LimitesImportacao? limites = null,
        char delimitador = DelimitadorPadrao)
    {
        ArgumentNullException.ThrowIfNull(conteudo);

        using var fluxo = new MemoryStream(new UTF8Encoding(false).GetBytes(conteudo));

        return Ler(fluxo, limites, delimitador);
    }

    /// <summary>
    /// Le no maximo <paramref name="teto"/> bytes e diz se o arquivo coube.
    ///
    /// Delega para <see cref="FluxoComTeto"/>: o controle nasceu aqui na etapa
    /// 1 e saiu daqui na etapa 4, quando o leitor de XLSX passou a precisar do
    /// mesmo teto. Duas copias de um controle de seguranca sao duas copias que
    /// um dia divergem.
    /// </summary>
    private static bool LerComTeto(Stream origem, int teto, out byte[] bytes) =>
        FluxoComTeto.Ler(origem, teto, out bytes);

    /// <summary>
    /// Transforma bytes em texto, adivinhando a codificacao quando preciso.
    ///
    /// Ordem: BOM manda; sem BOM, tenta UTF-8 ESTRITO; se falhar, Latin-1.
    ///
    /// A tentativa estrita e o que faz isso funcionar. UTF-8 tem estrutura
    /// rigida - nem toda sequencia de bytes e UTF-8 valido -, entao decodificar
    /// com `throwOnInvalidBytes` e um teste confiavel, e nao um chute.
    ///
    /// A alternativa seria decodificar sempre como UTF-8 tolerante, e ai
    /// "Jose" vindo de um Excel brasileiro viraria "Jos?" em silencio: o nome
    /// entraria corrompido no banco sem erro nenhum.
    ///
    /// Latin-1 e nao Windows-1252 porque Latin-1 e nativa do .NET e as duas so
    /// diferem na faixa 0x80-0x9F (aspas curvas, travessao). Nenhum caractere
    /// de nome proprio brasileiro mora nessa faixa, e evitar o pacote
    /// System.Text.Encoding.CodePages vale mais que aquelas aspas.
    /// </summary>
    private static string Decodificar(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static ResultadoLeitura Analisar(
        string texto, LimitesImportacao limites, char delimitador)
    {
        var erros = new List<ErroImportacao>();
        var linhas = new List<LinhaCsv>();

        var cabecalho = Array.Empty<string>() as IReadOnlyList<string>;
        var quantidadeDeColunas = 0;
        var numero = 0;

        foreach (var (campos, numeroDaLinha, erroDaLinha) in Dividir(texto, delimitador, limites))
        {
            numero = numeroDaLinha;

            if (erroDaLinha is not null)
            {
                erros.Add(erroDaLinha);
                continue;
            }

            if (cabecalho.Count == 0)
            {
                var problemas = ValidarCabecalho(campos, limites);

                if (problemas.Count > 0)
                {
                    return new ResultadoLeitura([], [], problemas);
                }

                cabecalho = campos;
                quantidadeDeColunas = campos.Count;
                continue;
            }

            // Linha em branco no fim do arquivo e o caso mais comum de todos -
            // quase todo editor deixa uma. Recusa-la seria recusar o arquivo
            // que o Excel acabou de salvar.
            if (campos.Count == 1 && campos[0].Length == 0)
            {
                continue;
            }

            if (campos.Count != quantidadeDeColunas)
            {
                erros.Add(new ErroImportacao(
                    numeroDaLinha, null,
                    $"A linha tem {campos.Count} campos, e o cabecalho tem {quantidadeDeColunas}."));

                continue;
            }

            if (linhas.Count >= limites.MaximoRegistros)
            {
                erros.Add(new ErroImportacao(
                    numeroDaLinha, null,
                    $"Arquivo com mais de {limites.MaximoRegistros:N0} registros."));

                break;
            }

            linhas.Add(new LinhaCsv(numeroDaLinha, campos));
        }

        if (cabecalho.Count == 0 && erros.Count == 0)
        {
            erros.Add(new ErroImportacao(0, null, "Arquivo sem cabecalho."));
        }

        // Um arquivo so com cabecalho nao e erro de forma, mas nao e importacao
        // nenhuma - e devolver "0 registros importados com sucesso" seria pior
        // que dizer o que aconteceu.
        if (cabecalho.Count > 0 && linhas.Count == 0 && erros.Count == 0)
        {
            erros.Add(new ErroImportacao(
                numero, null, "Arquivo tem cabecalho, mas nenhuma linha de dados."));
        }

        return new ResultadoLeitura(cabecalho, linhas, erros);
    }

    private static List<ErroImportacao> ValidarCabecalho(
        IReadOnlyList<string> campos, LimitesImportacao limites)
    {
        var problemas = new List<ErroImportacao>();

        if (campos.Count > limites.MaximoColunas)
        {
            problemas.Add(new ErroImportacao(
                1, null, $"Cabecalho com mais de {limites.MaximoColunas} colunas."));

            return problemas;
        }

        for (var i = 0; i < campos.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(campos[i]))
            {
                problemas.Add(new ErroImportacao(
                    1, null, $"A coluna {i + 1} do cabecalho esta sem nome."));

                continue;
            }

            for (var j = 0; j < i; j++)
            {
                if (ResultadoLeitura.NomesDeColunaIguais(campos[i], campos[j]))
                {
                    // Duas colunas com o mesmo nome tornariam a busca por nome
                    // ambigua, e o valor usado dependeria da ordem - que e o
                    // tipo de defeito que so aparece em producao.
                    problemas.Add(new ErroImportacao(
                        1, campos[i], $"A coluna '{campos[i]}' aparece mais de uma vez."));

                    break;
                }
            }
        }

        return problemas;
    }

    /// <summary>
    /// O parser de verdade: percorre caractere a caractere e devolve uma linha
    /// por vez.
    ///
    /// Maquina de tres estados. Ela existe porque dentro de aspas o
    /// delimitador e a quebra de linha deixam de separar qualquer coisa - e
    /// dividir por `\n` e depois por `;`, que e a tentacao obvia, quebra em
    /// qualquer endereco com virgula ou observacao com quebra de linha.
    /// </summary>
    private static IEnumerable<(IReadOnlyList<string> Campos, int Numero, ErroImportacao? Erro)>
        Dividir(string texto, char delimitador, LimitesImportacao limites)
    {
        var campos = new List<string>();
        var campo = new StringBuilder();
        var numero = 1;
        var dentroDeAspas = false;
        var campoTruncado = false;

        for (var i = 0; i < texto.Length; i++)
        {
            var atual = texto[i];

            if (dentroDeAspas)
            {
                if (atual != '"')
                {
                    Acumular(atual);
                    continue;
                }

                // Aspas dentro de aspas: `""` e uma aspa literal (RFC 4180).
                if (i + 1 < texto.Length && texto[i + 1] == '"')
                {
                    Acumular('"');
                    i++;
                    continue;
                }

                dentroDeAspas = false;
                continue;
            }

            if (atual == '"' && campo.Length == 0)
            {
                dentroDeAspas = true;
                continue;
            }

            if (atual == delimitador)
            {
                FecharCampo();
                continue;
            }

            if (atual is '\r' or '\n')
            {
                // CRLF conta como UMA quebra. Sem isto, todo arquivo salvo no
                // Windows viria com uma linha vazia entre cada duas linhas.
                if (atual == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n')
                {
                    i++;
                }

                FecharCampo();

                yield return (campos.ToArray(), numero, null);

                campos = [];
                numero++;
                campoTruncado = false;
                continue;
            }

            Acumular(atual);
        }

        // Aspas que abriram e nunca fecharam. O resto do arquivo virou um campo
        // so - e isso e sempre defeito do arquivo, nunca intencao.
        if (dentroDeAspas)
        {
            yield return ([], numero, new ErroImportacao(
                numero, null, "Aspas abertas e nao fechadas."));

            yield break;
        }

        // Ultima linha sem quebra no fim: existe e precisa sair.
        if (campo.Length > 0 || campos.Count > 0)
        {
            FecharCampo();
            yield return (campos.ToArray(), numero, null);
        }

        void Acumular(char caractere)
        {
            if (campo.Length >= limites.TamanhoMaximoCampo)
            {
                campoTruncado = true;
                return;
            }

            campo.Append(caractere);
        }

        void FecharCampo()
        {
            // Truncar em silencio gravaria meio nome como se fosse o nome
            // inteiro. Melhor um campo visivelmente marcado, que a validacao
            // de dominio recusa em seguida.
            campos.Add(campoTruncado ? campo + "[TRUNCADO]" : campo.ToString());

            campo.Clear();
            campoTruncado = false;
        }
    }
}
