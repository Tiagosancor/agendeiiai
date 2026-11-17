using Plataforma.Aplicacao.Abstracoes;

namespace Plataforma.Infraestrutura.MultiTenant;

public sealed class UsuarioAtual : IUsuarioAtual
{
    private const int TamanhoMaximoRotulo = 320;

    public Guid? UsuarioId { get; private set; }

    public string? Email { get; private set; }

    public string? Rotulo { get; private set; }

    public void Definir(Guid usuarioId, string? email, string? nomeUsuario = null, string? nome = null)
    {
        UsuarioId = usuarioId;
        Email = string.IsNullOrWhiteSpace(email) ? null : email;
        Rotulo = MontarRotulo(usuarioId, Email, nomeUsuario, nome);
    }

    /// <summary>E-mail verificado, senão <c>@nome_usuario</c>, senão o nome, senão <c>usuario:{id}</c>. Função pura.</summary>
    public static string MontarRotulo(Guid usuarioId, string? email, string? nomeUsuario, string? nome)
    {
        string rotulo;
        if (!string.IsNullOrWhiteSpace(email))
            rotulo = email.Trim();
        else if (!string.IsNullOrWhiteSpace(nomeUsuario))
            rotulo = "@" + nomeUsuario.Trim();
        else if (!string.IsNullOrWhiteSpace(nome))
            rotulo = nome.Trim();
        else
            rotulo = $"usuario:{usuarioId}";

        return rotulo.Length > TamanhoMaximoRotulo ? rotulo[..TamanhoMaximoRotulo] : rotulo;
    }
}
