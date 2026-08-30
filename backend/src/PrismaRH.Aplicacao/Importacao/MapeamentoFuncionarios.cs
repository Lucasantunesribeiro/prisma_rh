using PrismaRH.Dominio.Importacao;

namespace PrismaRH.Aplicacao.Importacao;

/// <summary>
/// De qual coluna do arquivo sai cada campo do funcionario.
///
/// ## Por que isto existe
///
/// Ate a etapa 3 os nomes eram fixos: `nome`, `cpf`, `data de nascimento`. Isso
/// obriga quem importa a renomear a planilha que ja tem - e a planilha que ja
/// tem costuma dizer "Nome Completo" e "Documento". Renomear a mao antes de
/// cada importacao e o tipo de trabalho que o sistema deveria evitar.
///
/// ## Por que continua seguro
///
/// O mapeamento **vem do cliente**, e por isso ele nao e crido: e conferido
/// contra o cabecalho do arquivo RELIDO na confirmacao. So passa nome de coluna
/// que existe naquele arquivo, naquele momento.
///
/// E vocabulario fechado no sentido do `CLAUDE.md secao 24.7`: o cliente
/// escolhe DENTRO de um conjunto que o servidor acabou de ler do arquivo, e nao
/// digita um seletor livre. Nao ha caminho daqui para consulta, para SQL nem
/// para nada alem de um indice de coluna.
///
/// O que o mapeamento NAO faz: escolher quais campos existem, dispensar
/// validacao de conteudo, ou permitir importar coluna que o dominio nao pede.
/// Os tres campos continuam obrigatorios porque <see cref="Dominio.Pessoas.Funcionario"/>
/// exige os tres.
/// </summary>
public sealed record MapeamentoFuncionarios(string Nome, string Cpf, string DataNascimento)
{
    /// <summary>
    /// Teto do nome de uma coluna.
    ///
    /// Nao e sobre memoria - o corpo da requisicao ja tem teto proprio. E sobre
    /// o que **sai daqui**: o nome escolhido volta na resposta e aparece na
    /// mensagem de erro, e um nome de 5 mil caracteres viraria uma resposta de
    /// 5 mil caracteres.
    ///
    /// ⚠️ O teto e aplicado por CORTE em <see cref="De"/>, e nao por recusa.
    /// A diferenca importa: cortando na entrada, nenhum nome longo demais chega
    /// a existir dentro do processo, e nao ha um segundo lugar - resposta, log,
    /// relatorio - onde alguem precise lembrar de conferir. Um teste provou a
    /// necessidade: a versao que so validava deixava o nome gigante voltar
    /// intacto no campo `mapeamento` da resposta.
    /// </summary>
    public const int TamanhoMaximoNome = 200;

    /// <summary>Os nomes que o modelo de arquivo do Prisma RH usa.</summary>
    public static readonly MapeamentoFuncionarios Padrao = new(
        ImportadorFuncionarios.ColunaNome,
        ImportadorFuncionarios.ColunaCpf,
        ImportadorFuncionarios.ColunaDataNascimento);

    /// <summary>
    /// Monta um mapeamento a partir do que o cliente enviou, caindo no padrao
    /// campo a campo.
    ///
    /// Campo em branco vira o padrao, e nao erro: a tela envia so o que a
    /// pessoa mudou, e exigir os tres sempre transformaria "nao mexi em nada"
    /// numa requisicao invalida.
    /// </summary>
    public static MapeamentoFuncionarios De(string? nome, string? cpf, string? dataNascimento) =>
        new(
            Ou(nome, Padrao.Nome),
            Ou(cpf, Padrao.Cpf),
            Ou(dataNascimento, Padrao.DataNascimento));

    /// <summary>Os nomes de coluna que este mapeamento aponta.</summary>
    public IReadOnlyList<string> Colunas => [Nome, Cpf, DataNascimento];

    /// <summary>
    /// Confere o mapeamento contra o cabecalho de um arquivo ja lido.
    ///
    /// Devolve lista vazia quando esta tudo certo.
    /// </summary>
    public IReadOnlyList<ErroImportacao> Conferir(ResultadoLeitura leitura)
    {
        ArgumentNullException.ThrowIfNull(leitura);

        var problemas = new List<ErroImportacao>();

        foreach (var (campo, coluna) in Campos())
        {
            if (leitura.Coluna(coluna) is null)
            {
                // A frase muda conforme o mapeamento tenha sido escolhido ou
                // nao. No caso padrao, "a coluna 'nome', escolhida para 'nome'"
                // e uma tautologia que so confunde quem esta lendo o erro.
                var mensagem = ResultadoLeitura.NomesDeColunaIguais(coluna, campo)
                    ? $"A coluna obrigatoria '{campo}' nao existe no arquivo."
                    : $"A coluna '{coluna}', escolhida para '{campo}', nao existe no arquivo.";

                problemas.Add(new ErroImportacao(1, coluna, mensagem));
            }
        }

        if (problemas.Count > 0)
        {
            return problemas;
        }

        // Duas colunas apontando para o mesmo lugar importaria o CPF como nome
        // - e o arquivo pareceria valido, porque cada campo isolado esta
        // preenchido.
        var campos = Campos().ToList();

        for (var i = 0; i < campos.Count; i++)
        {
            for (var j = i + 1; j < campos.Count; j++)
            {
                if (ResultadoLeitura.NomesDeColunaIguais(campos[i].Coluna, campos[j].Coluna))
                {
                    problemas.Add(new ErroImportacao(
                        1, campos[i].Coluna,
                        $"As colunas de '{campos[i].Campo}' e '{campos[j].Campo}' apontam "
                        + "para a mesma coluna do arquivo."));
                }
            }
        }

        return problemas;
    }

    private IEnumerable<(string Campo, string Coluna)> Campos()
    {
        yield return (ImportadorFuncionarios.ColunaNome, Nome);
        yield return (ImportadorFuncionarios.ColunaCpf, Cpf);
        yield return (ImportadorFuncionarios.ColunaDataNascimento, DataNascimento);
    }

    private static string Ou(string? valor, string padrao)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return padrao;
        }

        var limpo = valor.Trim();

        return limpo.Length > TamanhoMaximoNome ? limpo[..TamanhoMaximoNome] : limpo;
    }
}
