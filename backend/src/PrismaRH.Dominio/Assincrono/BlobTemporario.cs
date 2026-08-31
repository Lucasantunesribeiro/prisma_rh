using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Dominio.Assincrono;

/// <summary>
/// Os bytes de um arquivo enviado, guardados **so ate o worker terminar**.
///
/// ## Por que no banco, e nao no S3
///
/// O S3 nao esta na tabela de Free Tier permanente da AWS: ele cobra desde o
/// primeiro byte. O requisito do portfolio e **custo previsto de US$ 0,00**,
/// entao o arquivo fica no PostgreSQL.
///
/// Blob em banco relacional e decisao que a maioria dos textos desaconselha, e
/// com razao - infla backup, replicacao e memoria. Tres coisas tornam o caso
/// aqui diferente:
///
/// 1. o arquivo e **pequeno** (teto de 5 MB) e o total e **limitado** (50 MB em
///    toda a aplicacao);
/// 2. ele e **temporario** - apagado ao concluir, e no maximo 7 dias depois;
/// 3. o isolamento entre organizacoes continua sendo o **mesmo filtro global**
///    de sempre, em vez de prefixo de chave em bucket - que seria um controle
///    novo, e controle novo e onde erro novo aparece.
///
/// ## Os bytes somem; o registro fica
///
/// Apagar o blob **nao** apaga a `Importacao`: quem, quando, qual arquivo (por
/// hash), quantas linhas e o que deu errado continuam la para sempre. O que se
/// perde e a capacidade de reprocessar o mesmo upload - e isso e proposital,
/// porque manter CPF e salario guardados "por precaucao" e exatamente o que a
/// minimizacao proibe.
/// </summary>
public sealed class BlobTemporario
{
    private BlobTemporario()
    {
    }

    public BlobTemporario(
        Guid idOrganizacao,
        Guid idTrabalho,
        byte[] conteudo,
        DateTimeOffset agora,
        TimeSpan retencao)
    {
        ArgumentNullException.ThrowIfNull(conteudo);

        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Blob precisa pertencer a uma organizacao.", nameof(idOrganizacao));
        }

        if (idTrabalho == Guid.Empty)
        {
            throw new ArgumentException("Blob precisa pertencer a um trabalho.", nameof(idTrabalho));
        }

        if (conteudo.Length == 0)
        {
            throw new ArgumentException("Blob vazio nao e arquivo.", nameof(conteudo));
        }

        if (conteudo.Length > OrcamentoSemCusto.TamanhoMaximoArquivoBytes)
        {
            throw new ArgumentException(
                $"Arquivo excede {OrcamentoSemCusto.TamanhoMaximoArquivoBytes} bytes.",
                nameof(conteudo));
        }

        Id = Guid.CreateVersion7();
        IdOrganizacao = idOrganizacao;
        IdTrabalho = idTrabalho;
        Conteudo = conteudo;
        TamanhoBytes = conteudo.Length;
        CriadoEm = agora;
        ExpiraEm = agora.Add(retencao);
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// O tenant dono do arquivo.
    ///
    /// ⚠️ O **orcamento** de armazenamento e global; o **dado** nao e. Esta
    /// coluna e o que mantem o filtro global valendo: uma organizacao pode
    /// ocupar o espaco que impede a outra de importar, e ainda assim nunca le
    /// um byte dela.
    /// </summary>
    public Guid IdOrganizacao { get; private set; }

    public Guid IdTrabalho { get; private set; }

    /// <summary>
    /// Os bytes. Coluna `bytea`.
    ///
    /// Carregada **so** quando o worker vai processar - as consultas de
    /// listagem e de orcamento nunca a tocam, porque trazer 5 MB para somar um
    /// inteiro seria desperdicio em cima do recurso mais escasso do projeto.
    /// </summary>
    public byte[] Conteudo { get; private set; } = [];

    /// <summary>
    /// Redundante com <c>Conteudo.Length</c>, e de proposito.
    ///
    /// E ela que permite somar o orcamento global **sem carregar um unico
    /// byte**: `SELECT SUM(tamanho_bytes)` em vez de `SELECT SUM(length(conteudo))`,
    /// que leria a tabela inteira do disco.
    /// </summary>
    public int TamanhoBytes { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    /// <summary>
    /// Quando a limpeza pode levar isto embora mesmo que ninguem tenha
    /// concluido o trabalho. A rede de seguranca contra o blob orfao.
    /// </summary>
    public DateTimeOffset ExpiraEm { get; private set; }

    public bool Expirado(DateTimeOffset agora) => agora >= ExpiraEm;
}
