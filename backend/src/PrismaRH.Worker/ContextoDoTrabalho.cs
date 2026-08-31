using PrismaRH.Aplicacao.Identidade;
using PrismaRH.Dominio.Identidade;

namespace PrismaRH.Worker;

/// <summary>
/// O tenant sob o qual o worker esta trabalhando **agora**.
///
/// ## Por que isto precisa existir
///
/// O `PrismaRhDbContext` deriva o filtro global de `IContextoUsuario`, que na
/// API vem do JWT da requisicao. **O worker nao tem requisicao e nao tem JWT.**
///
/// Sem alguem no lugar dessa interface, o filtro leria `Guid.Empty` e o worker
/// nao encontraria nada - falharia fechado, o que e seguro, mas nao funciona.
///
/// ## Nasce vazio, e isso e a defesa
///
/// A instancia comeca em `Guid.Empty` de proposito: se alguem esquecer de
/// chamar <see cref="Abrir"/>, o worker nao vaza nada - ele simplesmente nao
/// acha o que procura, e a mensagem falha de forma visivel.
///
/// O caminho perigoso seria o contrario: nascer com "alguma" organizacao, ou
/// guardar a ultima usada. Como a Lambda **reaproveita o processo entre
/// invocacoes**, uma instancia global mutavel que sobrevivesse entre mensagens
/// faria a mensagem seguinte herdar o tenant da anterior - e esse e exatamente
/// o vazamento que o Security Gate da Fase 9 chama de mais provavel do produto.
///
/// Por isso ele e registrado como **scoped**, e o worker abre um escopo novo
/// **por mensagem**.
/// </summary>
public sealed class ContextoDoTrabalho : IContextoUsuario
{
    public bool EstaAutenticado => IdOrganizacao != Guid.Empty;

    public Guid IdUsuario { get; private set; }

    public Guid IdOrganizacao { get; private set; }

    /// <summary>
    /// O worker roda com o perfil de quem pediu o trabalho, e nao com um
    /// perfil de servico privilegiado. Importar funcionario e
    /// `AdministrarPessoas`, e quem enfileirou ja passou por essa porta na API.
    /// </summary>
    public Perfil Perfil { get; private set; } = Perfil.Visualizador;

    /// <summary>
    /// Estabelece o tenant a partir do **trabalho gravado no banco** - nunca
    /// direto da mensagem.
    ///
    /// A distincao e a defesa inteira: a mensagem e dado nao confiavel, e o
    /// trabalho e o fato. Quem chama so chega aqui depois de
    /// `MensagemTrabalho.Conferir` aprovar.
    /// </summary>
    public void Abrir(Guid idOrganizacao, Guid idUsuario)
    {
        if (idOrganizacao == Guid.Empty)
        {
            throw new ArgumentException("Worker nao abre contexto sem organizacao.", nameof(idOrganizacao));
        }

        IdOrganizacao = idOrganizacao;
        IdUsuario = idUsuario;
        Perfil = Perfil.AnalistaRh;
    }
}
