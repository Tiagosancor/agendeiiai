namespace Plataforma.Aplicacao.Contato;

/// <summary>Formulário "Fale Conosco" da página pública (seção 6.1.6): registra e encaminha por e-mail ao negócio.</summary>
public interface IServicoContato
{
    Task EnviarAsync(EnviarMensagemContato dados, CancellationToken cancellationToken = default);
}

public sealed record EnviarMensagemContato(string Nome, string? Telefone, string? Email, string Mensagem);
