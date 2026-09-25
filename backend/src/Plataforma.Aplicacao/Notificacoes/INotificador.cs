using Plataforma.Dominio.Comum;

namespace Plataforma.Aplicacao.Notificacoes;

/// <summary>
/// Orquestra os canais de notificação por evento (seção 9) — decide, a partir da
/// configuração do negócio atual (<c>IContextoNegocio</c>), quais canais usar para cada
/// evento; quem chama nunca escolhe o canal diretamente. Eventos de profissional
/// (novo/remarcado/cancelado) e lembretes por job entram na Sprint 4; aqui só o que o
/// assistente público precisa: código, confirmação ao cliente e Fale Conosco.
/// </summary>
public interface INotificador
{
    /// <summary>Código de confirmação (seção 8.1.1.b) — sempre pelos dois canais, WhatsApp e e-mail, nunca configurável por negócio.</summary>
    Task EnviarCodigoVerificacaoAsync(TelefoneE164 telefone, string? email, string codigo, CancellationToken cancellationToken = default);

    /// <summary>Confirmação do agendamento ao cliente (seção 6.3) — e-mail sempre; WhatsApp só se o negócio ativar.</summary>
    Task EnviarConfirmacaoAgendamentoAsync(DadosNotificacaoAgendamento dados, CancellationToken cancellationToken = default);

    /// <summary>Mensagem do formulário "Fale Conosco" (seção 6.1.6) ao e-mail de contato do negócio.</summary>
    Task EnviarMensagemContatoAsync(DadosNotificacaoContato dados, CancellationToken cancellationToken = default);

    /// <summary>Novo/remarcado/cancelado ao profissional (seção 9, Sprint 4) — só e-mail no MVP; nunca falha se o profissional não tiver e-mail cadastrado.</summary>
    Task EnviarNotificacaoProfissionalAsync(DadosNotificacaoProfissional dados, CancellationToken cancellationToken = default);

    /// <summary>Lembrete 24h/2h antes (seção 9, Sprint 4) — antecedência configurável, ver <c>OpcoesLembretes</c>.</summary>
    Task EnviarLembreteAsync(DadosNotificacaoAgendamento dados, CancellationToken cancellationToken = default);

    /// <summary>Fim do teste ou vencimento chegando (seção 7) — aos administradores do negócio, do produto para o negócio.</summary>
    Task EnviarAvisoAssinaturaAsync(DadosAvisoAssinatura dados, CancellationToken cancellationToken = default);

    /// <summary>Código que confirma o e-mail no cadastro de um negócio novo (seção 6.5).</summary>
    Task EnviarCodigoCadastroAsync(string email, string codigo, CancellationToken cancellationToken = default);

    /// <summary>
    /// No lugar do código, quando o e-mail já tem conta (seção 8.6.2): a tela responde igual,
    /// e só o dono do e-mail fica sabendo que a conta existe.
    /// </summary>
    Task EnviarAvisoContaExistenteAsync(string email, string linkLogin, CancellationToken cancellationToken = default);

    Task EnviarBoasVindasAsync(DadosBoasVindas dados, CancellationToken cancellationToken = default);
}

public sealed record DadosBoasVindas(
    string Email, string NomeUsuario, string NomeNegocio, string LinkPainel, string LinkPublico, DateTimeOffset FimTeste);

public sealed record DadosAvisoAssinatura(
    IReadOnlyList<string> EmailsAdministradores, string NomeNegocio, bool EmTeste, int DiasRestantes,
    DateTimeOffset Prazo, string NomePlano, decimal ValorDoPeriodo, string LinkAssinatura);

public enum EventoAgendamentoProfissional
{
    Novo,
    Remarcado,
    Cancelado,
}

public sealed record DadosNotificacaoProfissional(
    EventoAgendamentoProfissional Evento, string? EmailProfissional, string NomeCliente,
    DateTimeOffset Inicio, DateTimeOffset Fim, IReadOnlyList<string> Servicos, string? Observacoes);

public sealed record DadosNotificacaoAgendamento(
    string NomeCliente, string? EmailCliente, TelefoneE164 TelefoneCliente,
    DateTimeOffset Inicio, DateTimeOffset Fim, IReadOnlyList<string> Servicos, decimal Total,
    string LinkCancelar, string LinkRemarcar);

public sealed record DadosNotificacaoContato(string NomeRemetente, string? TelefoneRemetente, string? EmailRemetente, string Mensagem);
