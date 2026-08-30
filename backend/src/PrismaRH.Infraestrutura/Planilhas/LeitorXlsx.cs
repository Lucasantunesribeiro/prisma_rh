using System.Globalization;
using ClosedXML.Excel;
using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Infraestrutura.Planilhas;

/// <summary>
/// Leitura de XLSX com **ClosedXML**, aprovada pelo responsavel em 29/08/2026.
///
/// ## Por que biblioteca aqui e implementacao propria no CSV
///
/// CSV e texto delimitado e o parser cabe num arquivo. XLSX e um ZIP de XML com
/// esquema OOXML, tabela de strings compartilhadas, formatos de numero e datas
/// contadas em dias desde 1900 - com o bug do ano bissexto de 1900 preservado
/// de proposito pela Microsoft. Escrever isso a mao daria mais codigo, menos
/// revisado, e sem nada em troca.
///
/// ## Por que em `PrismaRH.Infraestrutura` e nao no dominio
///
/// `LeitorCsv` mora no dominio porque nao depende de nada. Este aqui depende de
/// um pacote de terceiro para ler um formato de arquivo - e o `CLAUDE.md secao
/// 18` e explicito: o dominio nao depende de detalhe de infraestrutura. Os dois
/// leitores produzem o MESMO <see cref="ResultadoLeitura"/>, e e esse tipo do
/// dominio que mantem o pipeline unico: a partir dele, XLSX e CSV sao
/// indistinguiveis para o resto do sistema.
///
/// ## Seguranca
///
/// - **A <see cref="GuardaXlsx"/> roda antes.** Assinatura de ZIP, partes
///   obrigatorias, macro, numero de entradas e tamanho REAL descomprimido. A
///   biblioteca so ve arquivo que ja provou ser inofensivo em tamanho.
/// - **Formula nao e avaliada, e tambem nao e aceita.** Ver
///   <see cref="TextoDaCelula"/> - a decisao vai alem do que o gate pedia.
/// - **Nada e extraido para disco.** Nao recebe caminho, so bytes.
/// - **Os limites de <see cref="LimitesImportacao"/> valem igual ao CSV.**
/// - **Erro vira relatorio, nunca excecao.**
/// </summary>
public static class LeitorXlsx
{
    /// <summary>
    /// Le a primeira planilha visivel e devolve cabecalho, linhas e erros.
    ///
    /// Nunca lanca por causa do CONTEUDO.
    /// </summary>
    public static ResultadoLeitura Ler(Stream conteudo, LimitesImportacao? limites = null)
    {
        ArgumentNullException.ThrowIfNull(conteudo);

        limites ??= LimitesImportacao.Padrao;

        if (!FluxoComTeto.Ler(conteudo, limites.TamanhoMaximoBytes, out var bytes))
        {
            var megabytes = limites.TamanhoMaximoBytes / (1024d * 1024d);

            return ResultadoLeitura.Falha(new ErroImportacao(
                0, null, $"Arquivo maior que o limite de {megabytes:N1} MB."));
        }

        return Ler(bytes, limites);
    }

    /// <summary>Le a partir dos bytes ja em memoria.</summary>
    public static ResultadoLeitura Ler(byte[] bytes, LimitesImportacao? limites = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        limites ??= LimitesImportacao.Padrao;

        if (bytes.Length == 0)
        {
            return ResultadoLeitura.Falha(new ErroImportacao(0, null, "Arquivo vazio."));
        }

        if (GuardaXlsx.Conferir(bytes) is { } bloqueio)
        {
            return ResultadoLeitura.Falha(bloqueio);
        }

        try
        {
            using var memoria = new MemoryStream(bytes, writable: false);

            // RecalculateAllFormulas explicitamente FALSO. E o padrao da
            // biblioteca, mas escrever aqui torna a intencao revisavel: mudar
            // esta linha e mudar a postura de seguranca, e vai aparecer no
            // diff.
            using var pasta = new XLWorkbook(memoria, new LoadOptions
            {
                RecalculateAllFormulas = false,
            });

            return LerPasta(pasta, limites);
        }
        catch (Exception excecao) when (excecao is not OutOfMemoryException)
        {
            // A ClosedXML lanca varios tipos diferentes para arquivo
            // malformado, e a lista muda entre versoes. Capturar por tipo
            // seria uma lista que envelhece em silencio e volta a devolver
            // 500 - o defeito que o `CLAUDE.md secao 24.19 item 4` registra.
            //
            // OutOfMemoryException fica de fora de proposito: ela nao e "o
            // arquivo esta ruim", e engoli-la mascararia um problema do
            // processo inteiro.
            return ResultadoLeitura.Falha(new ErroImportacao(
                0, null, "Nao foi possivel ler a planilha. O arquivo parece corrompido."));
        }
    }

    private static ResultadoLeitura LerPasta(XLWorkbook pasta, LimitesImportacao limites)
    {
        // So a PRIMEIRA planilha visivel.
        //
        // Ler todas juntaria dados de abas diferentes sem que ninguem pedisse.
        // Ler a "selecionada" faria o resultado depender de onde o cursor
        // estava quando o arquivo foi salvo. E ler aba oculta seria importar o
        // que a pessoa escondeu de proposito - o rascunho, a copia velha.
        var planilha = pasta.Worksheets
            .FirstOrDefault(p => p.Visibility == XLWorksheetVisibility.Visible);

        if (planilha is null)
        {
            return ResultadoLeitura.Falha(new ErroImportacao(
                0, null, "A planilha nao tem nenhuma aba visivel."));
        }

        var primeira = planilha.FirstRowUsed();

        if (primeira is null)
        {
            return ResultadoLeitura.Falha(new ErroImportacao(0, null, "Arquivo sem cabecalho."));
        }

        var ultimaColuna = primeira.LastCellUsed()?.Address.ColumnNumber ?? 0;

        if (ultimaColuna == 0)
        {
            return ResultadoLeitura.Falha(new ErroImportacao(0, null, "Arquivo sem cabecalho."));
        }

        if (ultimaColuna > limites.MaximoColunas)
        {
            return ResultadoLeitura.Falha(new ErroImportacao(
                primeira.RowNumber(), null,
                $"Cabecalho com mais de {limites.MaximoColunas} colunas."));
        }

        var (cabecalho, problemas) = LerCabecalho(primeira, ultimaColuna, limites);

        if (problemas.Count > 0)
        {
            return new ResultadoLeitura([], [], problemas);
        }

        return LerLinhas(planilha, cabecalho, ultimaColuna, primeira.RowNumber(), limites);
    }

    private static (IReadOnlyList<string> Cabecalho, List<ErroImportacao> Problemas) LerCabecalho(
        IXLRow primeira, int ultimaColuna, LimitesImportacao limites)
    {
        var numero = primeira.RowNumber();
        var problemas = new List<ErroImportacao>();
        var cabecalho = new List<string>(ultimaColuna);

        for (var coluna = 1; coluna <= ultimaColuna; coluna++)
        {
            var (texto, erro) = TextoDaCelula(primeira.Worksheet.Cell(numero, coluna), limites);

            if (erro is not null)
            {
                problemas.Add(new ErroImportacao(numero, null, $"Coluna {coluna}: {erro}"));
                cabecalho.Add(string.Empty);
                continue;
            }

            cabecalho.Add(texto);
        }

        for (var i = 0; i < cabecalho.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(cabecalho[i]))
            {
                problemas.Add(new ErroImportacao(
                    numero, null, $"A coluna {i + 1} do cabecalho esta sem nome."));

                continue;
            }

            for (var j = 0; j < i; j++)
            {
                if (ResultadoLeitura.NomesDeColunaIguais(cabecalho[i], cabecalho[j]))
                {
                    problemas.Add(new ErroImportacao(
                        numero, cabecalho[i],
                        $"A coluna '{cabecalho[i]}' aparece mais de uma vez."));

                    break;
                }
            }
        }

        return (cabecalho, problemas);
    }

    private static ResultadoLeitura LerLinhas(
        IXLWorksheet planilha,
        IReadOnlyList<string> cabecalho,
        int ultimaColuna,
        int linhaDoCabecalho,
        LimitesImportacao limites)
    {
        var linhas = new List<LinhaCsv>();
        var erros = new List<ErroImportacao>();

        // RowsUsed percorre as linhas que EXISTEM, e nao o retangulo declarado
        // na dimensao da planilha. A diferenca importa: a dimensao e um numero
        // escrito no XML, e um arquivo montado a mao pode declarar um milhao de
        // linhas que nao existem.
        foreach (var linha in planilha.RowsUsed())
        {
            var numero = linha.RowNumber();

            if (numero <= linhaDoCabecalho)
            {
                continue;
            }

            var campos = new List<string>(ultimaColuna);
            var problemaNaLinha = (string?)null;
            var vazia = true;

            for (var coluna = 1; coluna <= ultimaColuna; coluna++)
            {
                var (texto, erro) = TextoDaCelula(planilha.Cell(numero, coluna), limites);

                if (erro is not null && problemaNaLinha is null)
                {
                    problemaNaLinha = $"Coluna {coluna}: {erro}";
                }

                campos.Add(texto);

                if (texto.Length > 0)
                {
                    vazia = false;
                }
            }

            // O problema vem ANTES do teste de linha vazia, e a ordem importa.
            //
            // Uma celula recusada devolve texto vazio - e uma linha cujas
            // celulas sao todas formula pareceria vazia e seria descartada em
            // silencio. Era assim ate um teste apontar: a linha sumia do
            // relatorio e o arquivo virava "cabecalho sem dados", que nao
            // explica nada a quem precisa corrigir.
            if (problemaNaLinha is not null)
            {
                erros.Add(new ErroImportacao(numero, null, problemaNaLinha));
                continue;
            }

            // Linha formatada mas sem conteudo e o caso mais comum de todos
            // numa planilha - basta alguem ter pintado a borda ate a linha 200.
            // Recusa-la seria recusar o arquivo que o Excel acabou de salvar.
            if (vazia)
            {
                continue;
            }

            if (linhas.Count >= limites.MaximoRegistros)
            {
                erros.Add(new ErroImportacao(
                    numero, null,
                    $"Arquivo com mais de {limites.MaximoRegistros:N0} registros."));

                break;
            }

            linhas.Add(new LinhaCsv(numero, campos));
        }

        if (linhas.Count == 0 && erros.Count == 0)
        {
            erros.Add(new ErroImportacao(
                linhaDoCabecalho, null, "Arquivo tem cabecalho, mas nenhuma linha de dados."));
        }

        return new ResultadoLeitura(cabecalho, linhas, erros);
    }

    /// <summary>
    /// O texto de uma celula - ou o motivo pelo qual ela nao vira texto.
    ///
    /// ## Formula e recusada, nao avaliada e nem aproveitada
    ///
    /// O requisito era "nao avaliar formula". Esta classe vai um passo alem e
    /// **recusa a celula**, sem sequer tocar em `Value`.
    ///
    /// A razao e de correcao, nao de seguranca: a alternativa seria ler o valor
    /// em cache que o Excel gravou junto da formula, e esse valor pode estar
    /// **velho** - basta a planilha ter sido salva por um programa que nao
    /// recalcula. Importar um numero velho sem que ninguem consiga perceber e
    /// pior que recusar o arquivo, porque folha de pagamento nao tem como
    /// conferir depois.
    ///
    /// Nao tocar em `Value` tambem fecha a porta pela qual a avaliacao
    /// aconteceria: a biblioteca so calcula se alguem pedir o valor.
    ///
    /// A mensagem diz o que fazer - colar como valor - porque um erro que nao
    /// ensina a saida so transfere o problema.
    /// </summary>
    private static (string Texto, string? Erro) TextoDaCelula(
        IXLCell celula, LimitesImportacao limites)
    {
        if (celula.HasFormula)
        {
            return (string.Empty, "a celula contem formula. Copie e cole como valor.");
        }

        var valor = celula.Value;

        var texto = valor.Type switch
        {
            XLDataType.Blank => string.Empty,
            XLDataType.Text => valor.GetText(),

            // Formato invariante e sem notacao cientifica. Sem isso um CPF
            // digitado como numero viraria "1.1144477735E+10" e o erro
            // resultante nao teria nada a ver com a causa.
            XLDataType.Number => valor.GetNumber().ToString("0.##############", CultureInfo.InvariantCulture),

            // ISO, que e uma das duas formas que o importador aceita. Converter
            // aqui faz a data do Excel atravessar a MESMA validacao da data
            // digitada no CSV - e nao uma segunda, parecida.
            XLDataType.DateTime => Data(valor.GetDateTime()),

            XLDataType.TimeSpan => valor.GetTimeSpan().ToString(),
            XLDataType.Boolean => valor.GetBoolean() ? "VERDADEIRO" : "FALSO",

            // #DIV/0!, #REF!, #N/A. Nao e valor: e a planilha dizendo que nao
            // tem o valor. Aceitar o texto do erro como dado seria gravar
            // "#N/A" no nome de alguem.
            XLDataType.Error => string.Empty,

            _ => string.Empty,
        };

        if (valor.Type == XLDataType.Error)
        {
            return (string.Empty, "a celula esta com erro de formula.");
        }

        texto = texto.Trim();

        if (texto.Length > limites.TamanhoMaximoCampo)
        {
            // Marcado, como no CSV. Truncar em silencio gravaria meio nome como
            // se fosse o nome inteiro.
            return (texto[..limites.TamanhoMaximoCampo] + "[TRUNCADO]", null);
        }

        return (texto, null);
    }

    private static string Data(DateTime valor) => valor.TimeOfDay == TimeSpan.Zero
        ? valor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : valor.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
