using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Cadastro;

/// <summary>
/// Um teste grátis por e-mail e por telefone (seção 7/8.6.4). Tabela própria, com índice
/// único em cada um, gravada na mesma transação do cadastro: vale mesmo se o dono trocar o
/// e-mail de login depois, e o banco recusa a corrida de dois cadastros iguais.
/// </summary>
public class RegistroTesteGratis : EntidadeBase
{
    public string Email { get; private set; } = string.Empty;

    public string Telefone { get; private set; } = string.Empty;

    public Guid NegocioId { get; private set; }

    protected RegistroTesteGratis()
    {
    }

    public RegistroTesteGratis(string email, TelefoneE164 telefone, Guid negocioId)
    {
        Email = CodigoCadastro.NormalizarEmail(email);
        Telefone = telefone.Valor;
        NegocioId = negocioId;
    }
}
