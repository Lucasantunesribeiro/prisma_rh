using ClosedXML.Excel;
using PrismaRH.Aplicacao.Importacao;
using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Infraestrutura.Planilhas;

/// <summary>
/// Os arquivos de exemplo que a tela oferece para baixar.
///
/// O `ROADMAP.md` lista **modelos de arquivo** entre as entregas da Fase 5, e a
/// razao e pratica: sem um modelo, a primeira importacao de qualquer pessoa
/// falha por nome de coluna, e o relatorio de erro vira o manual de instrucoes.
///
/// ## O exemplo usa dados ficticios de proposito
///
/// `CLAUDE.md secao 24` proibe CPF real em demonstracao. Os dois CPFs daqui sao
/// validos no digito verificador - precisam ser, senao o modelo baixado nao
/// passa na propria validacao do sistema - e nao pertencem a ninguem.
///
/// ## Primeiro uso real da <see cref="ProtecaoCsv"/>
///
/// Ela existe desde a etapa 1 e ate agora nao tinha chamador: a defesa contra
/// *CSV injection* e de ESCRITA, e ate a etapa 4 o sistema so lia. Este e o
/// primeiro arquivo que o Prisma RH entrega para alguem abrir no Excel, entao e
/// aqui que ela passa a valer.
/// </summary>
public static class ModeloFuncionarios
{
    public const string TipoCsv = "text/csv";

    public const string TipoXlsx =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string NomeCsv = "modelo-funcionarios.csv";
    public const string NomeXlsx = "modelo-funcionarios.xlsx";

    private static readonly string[] Cabecalho =
    [
        ImportadorFuncionarios.ColunaNome,
        ImportadorFuncionarios.ColunaCpf,
        ImportadorFuncionarios.ColunaDataNascimento,
    ];

    private static readonly string[][] Exemplos =
    [
        ["Ana Paula Ribeiro", "111.444.777-35", "14/03/1991"],
        ["Bruno Carvalho Lima", "529.982.247-25", "02/11/1985"],
    ];

    /// <summary>CSV com BOM, ponto e virgula, e as celulas ja protegidas.</summary>
    public static byte[] Csv() => ProtecaoCsv.Arquivo(Cabecalho, Exemplos);

    /// <summary>
    /// XLSX com as tres colunas como **texto**.
    ///
    /// O tipo da coluna nao e detalhe estetico. Um CPF que comece com zero,
    /// digitado numa celula de formato geral, e guardado como numero - e o zero
    /// da frente some. A pessoa ve "12345678909" na tela, o arquivo guarda
    /// 12345678909, e o CPF de onze digitos vira dez sem que nada avise.
    ///
    /// Marcar a coluna como texto no modelo faz o Excel preservar o que for
    /// digitado.
    /// </summary>
    public static byte[] Xlsx()
    {
        using var pasta = new XLWorkbook();
        var planilha = pasta.AddWorksheet("Funcionarios");

        for (var coluna = 0; coluna < Cabecalho.Length; coluna++)
        {
            planilha.Cell(1, coluna + 1).Value = Cabecalho[coluna];
            planilha.Cell(1, coluna + 1).Style.Font.Bold = true;
            planilha.Column(coluna + 1).Style.NumberFormat.Format = "@";
        }

        for (var linha = 0; linha < Exemplos.Length; linha++)
        {
            for (var coluna = 0; coluna < Cabecalho.Length; coluna++)
            {
                // SetValue<string> e nao Value: evita que a biblioteca infira
                // "isto parece uma data" e converta o exemplo em serial do
                // Excel - o mesmo problema do CPF, na outra coluna.
                planilha.Cell(linha + 2, coluna + 1).SetValue(Exemplos[linha][coluna]);
            }
        }

        planilha.Columns().AdjustToContents();

        using var memoria = new MemoryStream();
        pasta.SaveAs(memoria);

        return memoria.ToArray();
    }
}
