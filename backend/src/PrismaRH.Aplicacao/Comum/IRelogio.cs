namespace PrismaRH.Aplicacao.Comum;

/// <summary>
/// Fonte do "agora". Existe para que regra que depende de tempo - expiracao de
/// token, vigencia, competencia - seja testavel sem esperar o relogio andar.
///
/// Sempre em UTC: o Npgsql recusa DateTimeOffset com offset diferente de zero
/// em coluna timestamptz, entao converter na borda evita erro em producao.
/// </summary>
public interface IRelogio
{
    DateTimeOffset Agora { get; }
}
