namespace Plataforma.Aplicacao.Clientes;

/// <summary>
/// Lembrete de retorno por WhatsApp ("faz N dias desde o seu último atendimento", com o link público): configuração do
/// negócio atual, opt-out do cliente e relatório. O envio em si é o job <c>JobLembreteRetorno</c>.
/// </summary>
public interface IServicoLembreteRetorno
{
    Task<ConfiguracaoLembreteRetorno?> ObterConfiguracaoAsync(CancellationToken cancellationToken = default);

    /// <summary>Configuração já validada (ver <c>LembreteRetorno.Validar</c>). Falso = negócio não encontrado.</summary>
    Task<bool> AtualizarConfiguracaoAsync(bool ativo, int dias, TimeOnly horario, string mensagem, CancellationToken cancellationToken = default);

    /// <summary>Envios com sucesso nos últimos <paramref name="dias"/> dias e quantos viraram agendamento em até 7 dias.</summary>
    Task<RelatorioLembreteRetorno> ObterRelatorioAsync(int dias, CancellationToken cancellationToken = default);

    /// <summary>Falso = cliente não encontrado neste negócio (ou anonimizado).</summary>
    Task<bool> DefinirAceiteClienteAsync(Guid clienteId, bool aceita, CancellationToken cancellationToken = default);
}

/// <param name="Horario">Sempre <c>HH:mm</c>, no fuso do negócio.</param>
public sealed record ConfiguracaoLembreteRetorno(bool Ativo, int Dias, string Horario, string Mensagem);

public sealed record RelatorioLembreteRetorno(int Dias, int Enviados, int GeraramAgendamento);
