namespace Plataforma.Dominio.Usuarios;

/// <summary>
/// Contador de falhas de login por identificador (e por conta, na chave <c>conta:{id}</c>, para a conferência de senha
/// atual em "Minha conta"). **Não** é de negócio: existe antes do token, como <c>slugs_anteriores</c>. A chave é o
/// HMAC-SHA256 do identificador normalizado — nenhum identificador em claro. A atualização real é um
/// <c>INSERT ... ON CONFLICT DO UPDATE</c> atômico (Fatia B); esta classe só descreve a linha.
/// </summary>
public class TentativaLogin
{
    public string Chave { get; private set; } = string.Empty;

    public DateTimeOffset JanelaInicio { get; private set; }

    public int Falhas { get; private set; }

    public DateTimeOffset? BloqueadoAte { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    protected TentativaLogin()
    {
    }

    public TentativaLogin(string chave, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(chave))
            throw new ArgumentException("A chave é obrigatória.", nameof(chave));

        Chave = chave;
        JanelaInicio = agora;
        AtualizadoEm = agora;
    }

    public bool Bloqueada(DateTimeOffset agora) => BloqueadoAte is { } ate && ate > agora;
}
