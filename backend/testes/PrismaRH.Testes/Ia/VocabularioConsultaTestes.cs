using PrismaRH.Infraestrutura.Ia;

namespace PrismaRH.Testes.Ia;

/// <summary>
/// O vocabulário fechado da consulta em linguagem natural (Fase 11C).
///
/// ## Por que estes testes são os mais importantes da subfase
///
/// Esta classe é **a única coisa entre a saída de um modelo de linguagem e uma
/// consulta ao banco**. Se ela deixar passar, não há segunda barreira.
///
/// O critério de aceite da Fase 11 no `ROADMAP.md` pede exatamente isto: *"a
/// consulta em linguagem natural não executa SQL arbitrário, e existe teste
/// provando que um filtro fora do vocabulário permitido é recusado"*.
/// </summary>
public sealed class VocabularioConsultaTestes
{
    private static RecusaFiltro Conferir(string campo, string operador, string valor) =>
        VocabularioConsulta.Conferir(campo, operador, valor, out _);

    // ------------------------------------------------------------- aceita

    [Theory]
    [InlineData("Severidade", "Igual", "Alta")]
    [InlineData("Status", "Diferente", "Resolvida")]
    [InlineData("Categoria", "Igual", "Contrato")]
    [InlineData("Regra", "Igual", "DesligadoNaFolha")]
    [InlineData("Competencia", "MaiorOuIgual", "2026-08")]
    [InlineData("ValorEncontrado", "Maior", "1500.00")]
    [InlineData("Diferenca", "Menor", "-250.50")]
    public void OQueEstaNoCatalogoPassa(string campo, string operador, string valor)
    {
        Assert.Equal(RecusaFiltro.Aceito, Conferir(campo, operador, valor));
    }

    /// <summary>O modelo escreve como quiser; o filtro guardado é canônico.</summary>
    [Theory]
    [InlineData("severidade", "igual", "alta", "Alta")]
    [InlineData("SEVERIDADE", "IGUAL", "ALTA", "Alta")]
    [InlineData("ValorEncontrado", "Maior", " 1500 ", "1500")]
    public void OValorEGuardadoNaFormaCanonica(
        string campo, string operador, string valor, string esperado)
    {
        Assert.Equal(RecusaFiltro.Aceito, VocabularioConsulta.Conferir(campo, operador, valor, out var f));
        Assert.Equal(esperado, f!.Valor);
    }

    // ------------------------------------------------------------- recusa

    /// <summary>
    /// ⚠️ **O teste que o critério de aceite exige.**
    ///
    /// Campo fora da lista é recusado — inclusive os que existem na entidade e
    /// **de propósito** não estão no vocabulário.
    /// </summary>
    [Theory]
    [InlineData("IdOrganizacao")]   // o que a IA jamais deve alcancar
    [InlineData("IdResponsavel")]
    [InlineData("NomeFuncionario")]
    [InlineData("Matricula")]
    [InlineData("Justificativa")]
    [InlineData("id")]
    [InlineData("")]
    [InlineData("'; DROP TABLE folhas; --")]
    public void CampoForaDoVocabularioERecusado(string campo)
    {
        Assert.Equal(RecusaFiltro.CampoDesconhecido, Conferir(campo, "Igual", "x"));
    }

    /// <summary>
    /// ⚠️ **`IdOrganizacao` não está no catálogo, e isso é o desenho.**
    ///
    /// Mesmo que estivesse, a consulta continuaria sob o filtro global — o
    /// isolamento não depende desta lista. Mas manter o campo fora elimina a
    /// classe inteira antes dela existir (`CLAUDE.md §37.5`).
    /// </summary>
    [Fact]
    public void OCatalogoNaoExpoeCampoDeIsolamentoNemDadoPessoal()
    {
        var campos = VocabularioConsulta.Catalogo.Select(c => c.Campo.ToString()).ToList();

        foreach (var proibido in new[]
                 {
                     "IdOrganizacao", "IdFuncionario", "IdResponsavel",
                     "NomeFuncionario", "Matricula", "Justificativa", "Contexto",
                 })
        {
            Assert.DoesNotContain(proibido, campos, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// ⚠️ `Severidade &gt; Alta` **não quer dizer nada**.
    ///
    /// Um enum tem igualdade, não ordem de negócio: `Alta` ser o valor 1 é
    /// detalhe de armazenamento. Deixar passar produziria um resultado que
    /// parece resposta e não é — o pior defeito possível num relatório de
    /// conferência.
    /// </summary>
    [Theory]
    [InlineData("Severidade", "Maior")]
    [InlineData("Severidade", "MenorOuIgual")]
    [InlineData("Status", "Maior")]
    [InlineData("Categoria", "Menor")]
    [InlineData("Regra", "MaiorOuIgual")]
    public void ComparacaoDeOrdemNaoValeParaEnum(string campo, string operador)
    {
        Assert.Equal(
            RecusaFiltro.OperadorNaoPermitidoNesteCampo,
            Conferir(campo, operador, "Alta"));
    }

    [Theory]
    [InlineData("Contem")]
    [InlineData("Like")]
    [InlineData("")]
    [InlineData("OR 1=1")]
    public void OperadorForaDoVocabularioERecusado(string operador)
    {
        Assert.Equal(
            RecusaFiltro.OperadorNaoPermitidoNesteCampo,
            Conferir("Severidade", operador, "Alta"));
    }

    [Theory]
    [InlineData("Severidade", "Critica")]        // severidade que nao existe
    [InlineData("Status", "Arquivada")]          // status que nao existe
    [InlineData("Competencia", "agosto")]
    [InlineData("Competencia", "2026-13")]       // mes 13
    [InlineData("ValorEncontrado", "muito")]
    [InlineData("ValorEncontrado", "")]
    public void ValorForaDoTipoERecusado(string campo, string valor)
    {
        Assert.Equal(RecusaFiltro.ValorForaDoTipo, Conferir(campo, "Igual", valor));
    }

    /// <summary>
    /// ⚠️ A armadilha silenciosa do `Enum.TryParse`: ele aceita **número**.
    /// `"7"` vira o enum 7 mesmo sem existir valor 7 declarado, e a consulta
    /// sairia com um valor que nenhuma linha tem — devolvendo lista vazia que
    /// parece resposta.
    /// </summary>
    [Theory]
    [InlineData("Severidade", "7")]
    [InlineData("Severidade", "1")]
    [InlineData("Status", "99")]
    [InlineData("Regra", "-3")]
    public void EnumPorNumeroERecusado(string campo, string valor)
    {
        Assert.Equal(RecusaFiltro.ValorForaDoTipo, Conferir(campo, "Igual", valor));
    }

    /// <summary>
    /// ⚠️ Vírgula decimal recusada de propósito.
    ///
    /// Ler `1.500,00` numa cultura e `1500.00` noutra é como o mesmo filtro
    /// vira mil e quinhentos num servidor e um e meio noutro, **sem erro
    /// nenhum aparecer**. O campo é técnico: cultura invariante, ponto decimal.
    /// </summary>
    [Theory]
    [InlineData("1500,00")]
    [InlineData("1.500,00")]
    [InlineData("R$ 1500")]
    public void NumeroEmFormatoBrasileiroERecusado(string valor)
    {
        Assert.Equal(RecusaFiltro.ValorForaDoTipo, Conferir("ValorEncontrado", "Maior", valor));
    }

    // ------------------------------------------------------------ coerencia

    /// <summary>
    /// O catálogo é o que vai no prompt E o que valida. Um campo declarado com
    /// operador que a validação recusaria faria o prompt oferecer ao modelo
    /// algo que sempre seria barrado — defeito invisível, que só aparece como
    /// "a IA nunca entende essa pergunta".
    /// </summary>
    [Fact]
    public void TodoOperadorAnunciadoNoCatalogoERealmenteAceito()
    {
        var exemplo = new Dictionary<CampoConsulta, string>
        {
            [CampoConsulta.Severidade] = "Alta",
            [CampoConsulta.Status] = "Detectada",
            [CampoConsulta.Categoria] = "Contrato",
            [CampoConsulta.Regra] = "DesligadoNaFolha",
            [CampoConsulta.Competencia] = "2026-08",
            [CampoConsulta.ValorEncontrado] = "10.00",
            [CampoConsulta.Diferenca] = "10.00",
        };

        foreach (var campo in VocabularioConsulta.Catalogo)
        {
            foreach (var operador in campo.Operadores)
            {
                Assert.Equal(
                    RecusaFiltro.Aceito,
                    Conferir(campo.Campo.ToString(), operador.ToString(), exemplo[campo.Campo]));
            }
        }
    }

    /// <summary>Todo campo do enum está no catálogo — e vice-versa.</summary>
    [Fact]
    public void OCatalogoCobreExatamenteOEnumDeCampos()
    {
        Assert.Equal(
            Enum.GetValues<CampoConsulta>().OrderBy(c => c),
            VocabularioConsulta.Catalogo.Select(c => c.Campo).OrderBy(c => c));
    }
}
