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
