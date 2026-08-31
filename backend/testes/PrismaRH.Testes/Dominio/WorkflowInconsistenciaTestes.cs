using PrismaRH.Dominio.Analises;
using PrismaRH.Dominio.Auditoria;
using PrismaRH.Dominio.Folha;
using PrismaRH.Dominio.Workflow;

namespace PrismaRH.Testes.Dominio;

/// <summary>
/// A maquina de estados do tratamento e a linha do tempo.
///
/// O Security Gate da Fase 7 nomeia a ameaca que estes testes cobrem:
/// **transicao de status pulando etapas para esconder pendencia**.
/// </summary>
public class WorkflowInconsistenciaTestes
{
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly Guid Ana = Guid.CreateVersion7();
    private static readonly Guid Bruno = Guid.CreateVersion7();
    private static readonly DateTimeOffset Agora = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static ResultadoAnalise Nova()
    {
        var execucao = new ExecucaoAnalise(
            Org, Guid.CreateVersion7(), new Competencia(2026, 8), 1, Ana, Agora);

        return execucao.Registrar(
            CatalogoRegras.De(CodigoRegra.LiquidoNegativo)!,
            Severidade.Alta,
            new Achado("Liquido negativo.", Matricula: "001", NomeFuncionario: "Quem Deve"));
    }

    /// <summary>Leva a inconsistencia ate o status pedido pelo caminho valido.</summary>
    private static ResultadoAnalise Em(StatusInconsistencia destino)
    {
        var r = Nova();

        if (destino == StatusInconsistencia.Detectada)
        {
            return r;
        }

        Assert.Null(r.Transitar(StatusInconsistencia.EmAnalise, Ana, null, Agora));

        if (destino == StatusInconsistencia.EmAnalise)
        {
            return r;
        }

        if (destino == StatusInconsistencia.Corrigida)
        {
            Assert.Null(r.Transitar(StatusInconsistencia.Corrigida, Ana, null, Agora));
            return r;
        }

        Assert.Null(r.Transitar(StatusInconsistencia.Justificada, Ana, "Adiantamento combinado.", Agora));

        if (destino == StatusInconsistencia.Justificada)
        {
            return r;
        }

        Assert.Null(r.Transitar(StatusInconsistencia.Resolvida, Ana, null, Agora));

        return r;
    }

    // ------------------------------------------------------------ o comeco

    [Fact]
    public void NasceDetectadaESemResponsavel()
    {
        var r = Nova();

        Assert.Equal(StatusInconsistencia.Detectada, r.Status);
        Assert.Null(r.IdResponsavel);
        Assert.Null(r.Justificativa);
        Assert.Null(r.ConcluidaEm);
        Assert.True(r.Pendente);
        Assert.Empty(r.Andamentos);
    }

    // ------------------------------------------------- transicoes validas

    [Theory]
    [InlineData(StatusInconsistencia.Detectada, StatusInconsistencia.EmAnalise)]
    [InlineData(StatusInconsistencia.EmAnalise, StatusInconsistencia.Corrigida)]
    [InlineData(StatusInconsistencia.Corrigida, StatusInconsistencia.Resolvida)]
    [InlineData(StatusInconsistencia.Justificada, StatusInconsistencia.Resolvida)]
    [InlineData(StatusInconsistencia.Justificada, StatusInconsistencia.EmAnalise)]
    [InlineData(StatusInconsistencia.Corrigida, StatusInconsistencia.EmAnalise)]
    [InlineData(StatusInconsistencia.Resolvida, StatusInconsistencia.EmAnalise)]
    public void TransicoesVALIDAS_Passam(StatusInconsistencia de, StatusInconsistencia para)
    {
        var r = Em(de);

        Assert.Null(r.Transitar(para, Ana, null, Agora));
        Assert.Equal(para, r.Status);
    }

    // ------------------------------------------------ transicoes invalidas

    /// <summary>
    /// Pular de Detectada direto para Resolvida e exatamente a ameaca que o
    /// Security Gate nomeia: fechar a pendencia sem ninguem ter olhado.
    /// </summary>
    [Theory]
    [InlineData(StatusInconsistencia.Detectada, StatusInconsistencia.Resolvida)]
    [InlineData(StatusInconsistencia.Detectada, StatusInconsistencia.Justificada)]
    [InlineData(StatusInconsistencia.Detectada, StatusInconsistencia.Corrigida)]
    [InlineData(StatusInconsistencia.EmAnalise, StatusInconsistencia.Resolvida)]
    [InlineData(StatusInconsistencia.EmAnalise, StatusInconsistencia.Detectada)]
    [InlineData(StatusInconsistencia.Resolvida, StatusInconsistencia.Justificada)]
    [InlineData(StatusInconsistencia.Resolvida, StatusInconsistencia.Detectada)]
    public void TransicoesINVALIDAS_SaoRecusadas(StatusInconsistencia de, StatusInconsistencia para)
    {
        var r = Em(de);

        var recusa = r.Transitar(para, Ana, "tentando", Agora);

        Assert.NotNull(recusa);
        Assert.Equal(de, r.Status);
    }

    [Fact]
    public void ARecusaDIZParaOndeEPossivelIr()
    {
        var r = Nova();

        var recusa = r.Transitar(StatusInconsistencia.Resolvida, Ana, null, Agora);

        // Erro que so diz "invalido" transfere o problema para quem leu.
        Assert.Contains("EmAnalise", recusa!, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusFORADOENUM_ERecusado()
    {
        var r = Nova();

        Assert.NotNull(r.Transitar((StatusInconsistencia)99, Ana, null, Agora));
        Assert.Equal(StatusInconsistencia.Detectada, r.Status);
    }

    [Fact]
    public void TransitarParaOMESMOStatus_ERecusado()
    {
        var r = Nova();

        Assert.NotNull(r.Transitar(StatusInconsistencia.Detectada, Ana, null, Agora));
    }

    // ----------------------------------------------------- a justificativa

    /// <summary>
    /// Justificar sem escrever o motivo e so fechar a pendencia com outro nome
    /// - e o relatorio de conformidade passaria a mentir.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void JustificarSemMOTIVO_ERecusado(string? texto)
    {
        var r = Em(StatusInconsistencia.EmAnalise);

        var recusa = r.Transitar(StatusInconsistencia.Justificada, Ana, texto, Agora);

        Assert.NotNull(recusa);
        Assert.Contains("motivo", recusa, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StatusInconsistencia.EmAnalise, r.Status);
    }

    [Fact]
    public void CorrigirNAOExigeTexto()
    {
        // Corrigir e um fato verificavel na folha: o numero mudou. Justificar e
        // uma afirmacao de quem escreveu, e por isso precisa do motivo.
        var r = Em(StatusInconsistencia.EmAnalise);

        Assert.Null(r.Transitar(StatusInconsistencia.Corrigida, Ana, null, Agora));
    }

    [Fact]
    public void ReabrirNAOApagaAJustificativa()
    {
        var r = Em(StatusInconsistencia.Justificada);

        Assert.Equal("Adiantamento combinado.", r.Justificativa);

        Assert.Null(r.Transitar(StatusInconsistencia.EmAnalise, Bruno, "Nao convenceu.", Agora));

        // A justificativa e parte do historico: apaga-la esconderia o que se
        // concluiu antes de a conclusao ser derrubada.
        Assert.Equal("Adiantamento combinado.", r.Justificativa);
        Assert.Equal(StatusInconsistencia.EmAnalise, r.Status);
    }

    [Fact]
    public void ResolverMarcaAData_EReabrirALimpa()
    {
        var r = Em(StatusInconsistencia.Resolvida);

        Assert.NotNull(r.ConcluidaEm);
        Assert.False(r.Pendente);

        Assert.Null(r.Transitar(StatusInconsistencia.EmAnalise, Ana, null, Agora));

        Assert.Null(r.ConcluidaEm);
        Assert.True(r.Pendente);
    }

    // ------------------------------------------------------ linha do tempo

    [Fact]
    public void TodaTransicaoDeixaUmaLINHANOHISTORICO()
    {
        var r = Em(StatusInconsistencia.Resolvida);

        var transicoes = r.Andamentos.Where(a => a.Tipo == TipoAndamento.Transicao).ToList();

        Assert.Equal(3, transicoes.Count);
        Assert.Equal(StatusInconsistencia.Detectada, transicoes[0].StatusAnterior);
        Assert.Equal(StatusInconsistencia.EmAnalise, transicoes[0].StatusNovo);
        Assert.Equal(StatusInconsistencia.Resolvida, transicoes[^1].StatusNovo);
    }

    [Fact]
    public void ATRANSICAORECUSADANaoDeixaLinha()
    {
        var r = Nova();

        r.Transitar(StatusInconsistencia.Resolvida, Ana, "tentando pular", Agora);

        // Historico de tentativa invalida seria ruido: nada aconteceu.
        Assert.Empty(r.Andamentos);
    }

    [Fact]
    public void AtribuirDeixaLinhaComOANTESEODEPOIS()
    {
        var r = Nova();

        r.Atribuir(Ana, Bruno, Agora);

        var linha = Assert.Single(r.Andamentos);

        Assert.Equal(TipoAndamento.Atribuicao, linha.Tipo);
        Assert.Null(linha.ResponsavelAnterior);
        Assert.Equal(Ana, linha.ResponsavelNovo);
        Assert.Equal(Bruno, linha.IdAutor);
        Assert.Equal(Ana, r.IdResponsavel);
    }

    [Fact]
    public void AtribuirOMESMORESPONSAVEL_NaoDeixaLinha()
    {
        var r = Nova();

        r.Atribuir(Ana, Bruno, Agora);
        r.Atribuir(Ana, Bruno, Agora);

        // Salvar a tela sem mexer no responsavel nao e um evento.
        Assert.Single(r.Andamentos);
    }

    [Fact]
    public void ComentarioVAZIO_ERecusado()
    {
        var r = Nova();

        Assert.NotNull(r.Comentar(Ana, "   ", Agora));
        Assert.Empty(r.Andamentos);
    }

    [Fact]
    public void EvidenciaVAZIA_ERecusada()
    {
        var r = Nova();

        Assert.NotNull(r.RegistrarEvidencia(Ana, "", Agora));
        Assert.Empty(r.Andamentos);
    }

    [Fact]
    public void TextoLongoDEMAISECortado()
    {
        var r = Nova();

        Assert.Null(r.Comentar(Ana, new string('x', 5_000), Agora));

        Assert.Equal(
            AndamentoInconsistencia.TamanhoMaximoTexto,
            Assert.Single(r.Andamentos).Texto!.Length);
    }

    /// <summary>
    /// Texto com HTML e guardado como TEXTO, e nao interpretado.
    ///
    /// O dominio nao escapa nada - quem escapa e o React, por padrao. O que se
    /// prova aqui e que o dominio tambem nao ATRAPALHA: ele nao remove, nao
    /// reescreve e nao interpreta. O que entrou sai igual, e a fronteira de
    /// renderizacao decide (`CLAUDE.md secao 24.9`).
    /// </summary>
    [Fact]
    public void TextoComHtmlEGuardadoLITERALMENTE()
    {
        var r = Nova();
        const string malicioso = "<script>alert('xss')</script>";

        Assert.Null(r.Comentar(Ana, malicioso, Agora));

        Assert.Equal(malicioso, Assert.Single(r.Andamentos).Texto);
    }

    [Fact]
    public void ALinhaDoTempoVemEmORDEMCRONOLOGICA()
    {
        var r = Nova();

        r.Atribuir(Ana, Ana, Agora);
        r.Transitar(StatusInconsistencia.EmAnalise, Ana, null, Agora.AddMinutes(1));
        r.Comentar(Ana, "Conferindo o adiantamento.", Agora.AddMinutes(2));
        r.RegistrarEvidencia(Ana, "Recibo assinado, arquivo 2026/08.", Agora.AddMinutes(3));

        Assert.Equal(
            [TipoAndamento.Atribuicao, TipoAndamento.Transicao, TipoAndamento.Comentario,
             TipoAndamento.Evidencia],
            r.Andamentos.Select(a => a.Tipo));
    }

    // -------------------------------------------- a tabela de transicoes

    [Fact]
    public void TodoStatusTemDestinoDECLARADO()
    {
        foreach (var status in Enum.GetValues<StatusInconsistencia>())
        {
            var destinos = TransicoesInconsistencia.A_partir_de(status);

            // Nenhum status e beco sem saida: sempre ha para onde ir, inclusive
            // de Resolvida (reabertura).
            Assert.NotEmpty(destinos);

            // Nenhum destino aponta para o proprio status.
            Assert.DoesNotContain(status, destinos);
        }
    }

    [Fact]
    public void SOResolvidaNaoEPendente()
    {
        foreach (var status in Enum.GetValues<StatusInconsistencia>())
        {
            Assert.Equal(
                status != StatusInconsistencia.Resolvida,
                TransicoesInconsistencia.Pendente(status));
        }
    }
}

/// <summary>A entidade da trilha de auditoria.</summary>
public class EventoAuditoriaTestes
{
    private static readonly Guid Org = Guid.CreateVersion7();
    private static readonly DateTimeOffset Agora = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SemOrganizacao_ERecusado()
    {
        // Auditoria SEMPRE registra a organizacao (Security Gate, item 4). Sem
        // ela o evento nao pertence a ninguem e some no filtro global - uma
        // trilha invisivel e pior que nenhuma.
        Assert.Throws<ArgumentException>(() => new EventoAuditoria(
            Guid.Empty, Guid.CreateVersion7(),
            AcaoAuditada.FolhaFechada, EntidadeAuditada.FolhaPagamento,
            Guid.CreateVersion7(), "Folha fechada.", Agora));
    }

    [Fact]
    public void NaoTemMetodoDeAlteracaoNemDeRemocao()
    {
        var metodos = typeof(EventoAuditoria)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(EventoAuditoria))
            .Select(m => m.Name)
            .ToList();

        // Somente-insercao no lugar mais forte possivel: nao existe caminho
        // para alterar, entao nao ha o que um endpoint pudesse expor.
        Assert.Empty(metodos);

        var escritores = typeof(EventoAuditoria)
            .GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .ToList();

        Assert.Empty(escritores);
    }

    [Fact]
    public void TextoLongoDemaisECortado()
    {
        var evento = new EventoAuditoria(
            Org, null, AcaoAuditada.FolhaCalculada, EntidadeAuditada.FolhaPagamento,
            Guid.CreateVersion7(), new string('d', 900), Agora, new string('c', 900));

        Assert.Equal(EventoAuditoria.TamanhoMaximoDescricao, evento.Descricao.Length);
        Assert.Equal(EventoAuditoria.TamanhoMaximoContexto, evento.Contexto!.Length);
    }
}
