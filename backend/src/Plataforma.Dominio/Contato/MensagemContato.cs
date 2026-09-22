using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Contato;

/// <summary>Mensagem do formulário "Fale Conosco" da página pública (seção 6.1.6) — só encaminhada por e-mail ao negócio, sem regra de domínio além do registro.</summary>
public class MensagemContato : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public string Nome { get; private set; } = string.Empty;

    public string? Telefone { get; private set; }

    public string? Email { get; private set; }

    public string Mensagem { get; private set; } = string.Empty;

    protected MensagemContato()
    {
    }

    private MensagemContato(Guid negocioId, string nome, string? telefone, string? email, string mensagem, DateTimeOffset agora)
    {
        NegocioId = negocioId;
        Nome = nome;
        Telefone = telefone;
        Email = email;
        Mensagem = mensagem;
        CriadoEm = agora;
    }

    public static MensagemContato Criar(Guid negocioId, string nome, string? telefone, string? email, string mensagem, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome é obrigatório.", nameof(nome));

        if (string.IsNullOrWhiteSpace(mensagem))
            throw new ArgumentException("A mensagem é obrigatória.", nameof(mensagem));

        if (string.IsNullOrWhiteSpace(telefone) && string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Informe telefone ou e-mail para contato.", nameof(telefone));

        return new MensagemContato(negocioId, nome.Trim(), telefone, email, mensagem.Trim(), agora);
    }
}
