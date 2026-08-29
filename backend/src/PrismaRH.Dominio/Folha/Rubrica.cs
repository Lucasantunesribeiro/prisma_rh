using PrismaRH.Dominio.Contratos;

namespace PrismaRH.Dominio.Folha;

/// <summary>
/// Um evento de folha do catalogo da organizacao: salario, comissao,
/// vale-transporte, adiantamento.
///
/// E catalogo, e nao texto livre no lancamento, pelo mesmo motivo de Cargo:
/// "Vale transporte", "vale-transporte" e "VT" digitados a mao viram tres
/// rubricas diferentes, e nenhum relatorio por rubrica volta a fechar.
/// </summary>
public sealed class Rubrica
{
    public const int TamanhoMaximoCodigo = 20;
    public const int TamanhoMaximoNome = 120;

    private Rubrica()
    {
    }

    public Rubrica(
        Guid idOrganizacao,
        string codigo,
        string nome,
        TipoRubrica tipo,
        EstrategiaRubrica estrategia,
        BaseCalculo basesIncidentes,
        DateTimeOffset criadoEm)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Rubrica precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        if (estrategia == EstrategiaRubrica.FgtsMensal && tipo != TipoRubrica.Informativo)
        {
            // FGTS nao e desconto: e deposito do empregador, e nao sai do
            // salario de ninguem. Como provento seria pior ainda - a empresa
            // pagaria ao funcionario o que deveria depositar na conta
            // vinculada. Informativo e o unico tipo correto.
            throw new ArgumentException(
                "A rubrica de FGTS precisa ser informativa: e deposito do empregador, "
                + "nao desconto nem provento.", nameof(tipo));
        }

        if (estrategia == EstrategiaRubrica.IrrfMensal && tipo != TipoRubrica.Desconto)
        {
            // IRRF sai do salario do funcionario - e a diferenca exata em
            // relacao ao FGTS. Como informativo ele nao reduziria o liquido, e
            // a pessoa receberia dinheiro que a empresa ja recolheu.
            throw new ArgumentException(
                "A rubrica de IRRF precisa ser desconto: ela sai do salario do funcionario.",
                nameof(tipo));
        }

        if (estrategia == EstrategiaRubrica.VerbaRescisoria && tipo != TipoRubrica.Provento)
        {
            // Todas as nove verbas rescisorias sao dinheiro que a pessoa
            // RECEBE. Descontos da rescisao - INSS, IRRF, adiantamentos - sao
            // apurados pelas rubricas proprias deles, como em qualquer folha.
            throw new ArgumentException(
                "As rubricas de rescisao precisam ser provento: sao valores pagos ao funcionario.",
                nameof(tipo));
        }

        if (EstrategiasDeFerias.Contains(estrategia) && tipo != TipoRubrica.Provento)
        {
            // As quatro rubricas de ferias sao dinheiro que a pessoa RECEBE:
            // remuneracao dos dias, terco constitucional, abono e o terco
            // sobre ele. Como desconto elas inverteriam o sinal do holerite;
            // como informativo, a pessoa sairia de ferias sem receber.
            throw new ArgumentException(
                "As rubricas de ferias precisam ser provento: sao valores pagos ao funcionario.",
                nameof(tipo));
        }

        if (EstrategiasDe13ProventoObrigatorio.Contains(estrategia) && tipo != TipoRubrica.Provento)
        {
            // O adiantamento e o total do 13o sao dinheiro que a pessoa RECEBE.
            throw new ArgumentException(
                "O adiantamento e o total do 13o precisam ser provento: sao valores pagos ao funcionario.",
                nameof(tipo));
        }

        if (estrategia == EstrategiaRubrica.DecimoTerceiroAdiantamentoDescontado
            && tipo != TipoRubrica.Desconto)
        {
            // Compensar o adiantamento na folha anual e desconto, por
            // definicao. Como provento, a folha pagaria o 13o duas vezes.
            throw new ArgumentException(
                "A rubrica de compensacao do adiantamento de 13o precisa ser um desconto.",
                nameof(tipo));
        }

        if (estrategia == EstrategiaRubrica.DecimoTerceiroBaseFgts
            && tipo != TipoRubrica.Informativo)
        {
            // Ela existe SO para carregar a base de FGTS da diferenca. Como
            // provento, pagaria o 13o duas vezes; como desconto, a invariante
            // da Fase 4A a proibiria de compor base - que e a unica coisa que
            // ela faz.
            throw new ArgumentException(
                "A rubrica da base de FGTS do 13o precisa ser informativa: ela compoe base sem pagar nada.",
                nameof(tipo));
        }

        if (estrategia == EstrategiaRubrica.FgtsMensal && basesIncidentes != BaseCalculo.Nenhuma)
        {
            // Se o FGTS compusesse a base de FGTS, cada calculo aumentaria a
            // base do calculo seguinte. Informativo PODE compor base - por isso
            // a recusa precisa ser explicita aqui.
            throw new ArgumentException(
                "A rubrica de FGTS nao compoe base alguma: ela incide sobre a base, nao a forma.",
                nameof(basesIncidentes));
        }

        if (estrategia == EstrategiaRubrica.InssProgressivo && tipo != TipoRubrica.Desconto)
        {
            // INSS que soma no liquido nao existe. Se passasse, a folha
            // pagaria a contribuicao ao funcionario em vez de reter.
            throw new ArgumentException(
                "A rubrica de INSS precisa ser um desconto.", nameof(tipo));
        }

        if (estrategia == EstrategiaRubrica.SalarioBaseProporcional && tipo != TipoRubrica.Provento)
        {
            // Salario que desconta nao existe. Deixar passar produziria uma
            // folha com liquido negativo para todo mundo, e o erro so
            // apareceria depois de calcular a empresa inteira.
            throw new ArgumentException(
                "A rubrica de salario-base precisa ser um provento.", nameof(tipo));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        Codigo = Cargo.ValidarTexto(codigo, TamanhoMaximoCodigo, "Codigo da rubrica", nameof(codigo)).ToUpperInvariant();
        Nome = Cargo.ValidarTexto(nome, TamanhoMaximoNome, "Nome da rubrica", nameof(nome));
        Tipo = tipo;
        Estrategia = estrategia;
        BasesIncidentes = ValidarIncidencias(basesIncidentes, tipo, nameof(basesIncidentes));
        Ativa = true;
        CriadoEm = criadoEm;
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public string Codigo { get; private set; } = string.Empty;
    public string Nome { get; private set; } = string.Empty;
    public TipoRubrica Tipo { get; private set; }
    public EstrategiaRubrica Estrategia { get; private set; }

    /// <summary>
    /// Em quais bases de calculo esta rubrica entra. Copiado para o lancamento
    /// no momento do calculo - a apuracao le o lancamento, nunca esta
    /// propriedade, para nao reescrever folha fechada.
    /// </summary>
    public BaseCalculo BasesIncidentes { get; private set; }

    public bool Ativa { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    /// <summary>
    /// Renomear e permitido; trocar tipo ou estrategia nao.
    ///
    /// Um desconto que vira provento mudaria o significado dos lancamentos ja
    /// gravados nas folhas fechadas - inclusive as que ninguem vai recalcular.
    /// Para mudar a natureza, inative esta e crie outra.
    /// </summary>
    public void Renomear(string nome) =>
        Nome = Cargo.ValidarTexto(nome, TamanhoMaximoNome, "Nome da rubrica", nameof(nome));

    /// <summary>
    /// Alterar incidencia e permitido, ao contrario de tipo e estrategia.
    ///
    /// A diferenca nao e de gosto: o lancamento congela a incidencia no
    /// calculo, entao mudar aqui so afeta calculos futuros. E precisa ser
    /// permitido - quando a lei muda o que compoe salario-de-contribuicao, a
    /// alternativa seria inativar a rubrica e criar outra, quebrando todo
    /// relatorio historico por codigo de rubrica.
    /// </summary>
    public void AlterarIncidencias(BaseCalculo basesIncidentes) =>
        BasesIncidentes = ValidarIncidencias(basesIncidentes, Tipo, nameof(basesIncidentes));

    public void Inativar() => Ativa = false;

    public void Reativar() => Ativa = true;

    /// <summary>
    /// As quatro estrategias que so existem em folha de ferias.
    ///
    /// Elas compartilham invariantes - todas sao provento, nenhuma aceita
    /// valor digitado - mas NAO compartilham incidencia: cada uma declara a
    /// sua, porque a lei trata as quatro de forma diferente.
    /// </summary>
    /// ARRAY, e nao HashSet: esta lista e usada dentro de consulta LINQ, e o
    /// EF Core nao traduz Contains de IReadOnlySet - a consulta ia para o
    /// cliente ou estourava. Com quatro itens, a busca linear nao custa nada.
    public static readonly EstrategiaRubrica[] EstrategiasDeFerias =
    [
        EstrategiaRubrica.FeriasGozadas,
        EstrategiaRubrica.TercoFerias,
        EstrategiaRubrica.AbonoPecuniario,
        EstrategiaRubrica.TercoAbono,
    ];

    /// <summary>
    /// As duas estrategias de 13o que precisam ser PROVENTO.
    ///
    /// As outras duas da Fase 4F nao entram aqui de proposito: a compensacao do
    /// adiantamento e desconto e a base de FGTS e informativa. Cada uma tem a
    /// sua propria recusa, com o motivo escrito.
    ///
    /// ARRAY pelo mesmo motivo de EstrategiasDeFerias: o EF Core nao traduz
    /// Contains de IReadOnlySet.
    /// </summary>
    public static readonly EstrategiaRubrica[] EstrategiasDe13ProventoObrigatorio =
    [
        EstrategiaRubrica.DecimoTerceiroAdiantamento,
        EstrategiaRubrica.DecimoTerceiroTotal,
    ];

    /// <summary>
    /// As quatro estrategias que so existem em folha de 13o salario.
    ///
    /// Usada pela API para carregar o catalogo do 13o de uma vez.
    /// </summary>
    public static readonly EstrategiaRubrica[] EstrategiasDe13 =
    [
        EstrategiaRubrica.DecimoTerceiroAdiantamento,
        EstrategiaRubrica.DecimoTerceiroTotal,
        EstrategiaRubrica.DecimoTerceiroAdiantamentoDescontado,
        EstrategiaRubrica.DecimoTerceiroBaseFgts,
    ];

    private static BaseCalculo ValidarIncidencias(BaseCalculo bases, TipoRubrica tipo, string parametro)
    {
        if (!BasesDeCalculo.Conhecidas(bases))
        {
            throw new ArgumentException(
                "Base de calculo desconhecida.", parametro);
        }

        if (tipo == TipoRubrica.Desconto && bases != BaseCalculo.Nenhuma)
        {
            // Base de INSS e a soma dos proventos que integram o
            // salario-de-contribuicao; desconto nao a reduz. O que reduz base e
            // DEDUCAO - o INSS abatendo a base de IRRF, o dependente -, que e
            // outro conceito e pertence a Fase 4D.
            //
            // Sem esta recusa, alguem marcaria "vale-transporte incide em INSS"
            // achando que representa o desconto de 6%, e a base sairia menor
            // sem ninguem notar, porque o holerite continuaria fechando.
            throw new ArgumentException(
                "Rubrica de desconto nao compoe base de calculo. Desconto nao reduz base: "
                + "o que reduz e deducao, que e outro conceito.",
                parametro);
        }

        return bases;
    }
}
