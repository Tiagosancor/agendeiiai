namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Marcadores das mensagens <c>Sistema</c> que o servidor grava (<c>MensagemWhatsApp.NomeFerramenta</c>). Os de agendamento entram
/// no histórico da IA como turnos do modelo (sem token); o aviso de limite e o aviso automatizado não.
/// </summary>
public static class MarcadoresAgente
{
    public const string LimiteAviso = "limite_aviso";
    public const string Resumo = "agendamento_resumo";
    public const string Confirmado = "agendamento_confirmado";
    public const string NaoConfirmado = "agendamento_nao_confirmado";
    public const string PrazoPassou = "agendamento_prazo_passou";

    public static readonly string[] DoHistorico = [Resumo, Confirmado, NaoConfirmado, PrazoPassou];
}
