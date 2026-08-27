using PrismaRH.Dominio.Folha;

namespace PrismaRH.Dominio.Pessoas;

/// <summary>
/// Quem depende do funcionario. Pertence a PESSOA, nao ao contrato: um filho
/// continua sendo filho se a pessoa for readmitida com contrato novo.
///
/// Existe para o IRRF (Fase 4D), que e a unica regra do produto que usa
/// dependente. Por isso o cadastro guarda o minimo que o calculo precisa
/// (CLAUDE.md secao 25: minimizacao de dado pessoal de TERCEIRO - o dependente
/// nao e usuario do sistema e nao consentiu com nada).
///
/// Nao ha CPF do dependente aqui. Ele existe na obrigacao acessoria real, mas
/// o calculo mensal nao precisa dele, e guardar documento de terceiro sem uso
/// seria coletar por precaucao - exatamente o que a minimizacao proibe.
/// </summary>
public sealed class Dependente
{
    public const int TamanhoMaximoNome = 200;

    private Dependente()
    {
    }

    public Dependente(
        Guid idOrganizacao,
        Guid idFuncionario,
        string nome,
        DateOnly dataNascimento,
        RelacaoDependente relacao,
        DateOnly? inicioDeducaoIrrf,
        DateOnly? fimDeducaoIrrf,
        DateTimeOffset criadoEm)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Dependente precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        if (idFuncionario == Guid.Empty)
        {
            throw new ArgumentException("Dependente precisa pertencer a um funcionario.", nameof(idFuncionario));
        }

        if (!Enum.IsDefined(relacao))
        {
            throw new ArgumentException("Relacao de dependencia desconhecida.", nameof(relacao));
        }

        var hoje = DateOnly.FromDateTime(criadoEm.UtcDateTime);

        if (dataNascimento > hoje)
        {
            throw new ArgumentException("Data de nascimento nao pode ser no futuro.", nameof(dataNascimento));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdFuncionario = idFuncionario;
        Nome = ValidarNome(nome);
        DataNascimento = dataNascimento;
        Relacao = relacao;
        CriadoEm = criadoEm;

        DefinirDeducaoIrrf(inicioDeducaoIrrf, fimDeducaoIrrf);
    }

    public Guid Id { get; private set; }
    public Guid IdOrganizacao { get; private set; }
    public Guid IdFuncionario { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public DateOnly DataNascimento { get; private set; }
    public RelacaoDependente Relacao { get; private set; }

    /// <summary>
    /// A partir de quando este dependente abate IRRF. <c>null</c> significa
    /// "nao abate".
    ///
    /// E DECLARADO, nao derivado da idade nem da relacao. Derivar exigiria
    /// codificar os limites legais - 21 anos, 24 se estudante, condicoes
    /// diferentes por categoria -, e cada um deles precisa de fonte oficial
    /// registrada (CLAUDE.md secao 29). Enquanto essa fonte nao existir no
    /// projeto, quem declara e o analista, e a declaracao fica auditavel.
    /// </summary>
    public DateOnly? InicioDeducaoIrrf { get; private set; }

    /// <summary>Ate quando abate. <c>null</c> com inicio preenchido = sem fim previsto.</summary>
    public DateOnly? FimDeducaoIrrf { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    /// <summary>Derivado, para nao existir estado invalido: flag ligada sem periodo.</summary>
    public bool DedutivelIrrf => InicioDeducaoIrrf is not null;

    /// <summary>
    /// Este dependente conta na competencia?
    ///
    /// A pergunta e do MES inteiro, nao de um dia: a dedução do IRRF e mensal.
    /// Basta o periodo tocar a competencia - quem passa a contar no dia 20
    /// conta o mes todo, e o mesmo vale para quem deixa de contar.
    /// </summary>
    public bool DedutivelEm(Competencia competencia)
    {
        if (InicioDeducaoIrrf is not { } inicio)
        {
            return false;
        }

        if (inicio > competencia.UltimoDia)
        {
            return false;
        }

        return FimDeducaoIrrf is not { } fim || fim >= competencia.PrimeiroDia;
    }

    public void Atualizar(string nome, DateOnly dataNascimento, RelacaoDependente relacao)
    {
        if (!Enum.IsDefined(relacao))
        {
            throw new ArgumentException("Relacao de dependencia desconhecida.", nameof(relacao));
        }

        Nome = ValidarNome(nome);
        DataNascimento = dataNascimento;
        Relacao = relacao;
    }

    /// <summary>
    /// Define ou remove o periodo de deducao.
    ///
    /// Alterar aqui NAO mexe em folha ja calculada (CLAUDE.md secao 4.3): o
    /// holerite guarda a quantidade que valeu no seu proprio calculo. A
    /// mudanca vale do proximo calculo em diante.
    /// </summary>
    public void DefinirDeducaoIrrf(DateOnly? inicio, DateOnly? fim)
    {
        if (inicio is null && fim is not null)
        {
            throw new ArgumentException(
                "Fim da deducao sem inicio: se o dependente nao abate IRRF, nao ha periodo.", nameof(fim));
        }

        if (inicio is { } i && fim is { } f && f < i)
        {
            throw new ArgumentException("Fim da deducao nao pode ser anterior ao inicio.", nameof(fim));
        }

        InicioDeducaoIrrf = inicio;
        FimDeducaoIrrf = fim;
    }

    private static string ValidarNome(string nome)
    {
        var limpo = (nome ?? string.Empty).Trim();

        if (limpo.Length == 0)
        {
            throw new ArgumentException("Nome do dependente e obrigatorio.", nameof(nome));
        }

        if (limpo.Length > TamanhoMaximoNome)
        {
            throw new ArgumentException(
                $"Nome pode ter no maximo {TamanhoMaximoNome} caracteres.", nameof(nome));
        }

        return limpo;
    }
}
