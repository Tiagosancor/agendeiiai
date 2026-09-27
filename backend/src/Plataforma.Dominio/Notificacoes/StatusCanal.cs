namespace Plataforma.Dominio.Notificacoes;

/// <summary>
/// Resultado de um canal (WhatsApp ou e-mail) de uma notificação (seção 10). Nulo na entidade
/// = o canal não foi usado (ex.: cliente sem e-mail, aviso por WhatsApp desligado no negócio).
/// </summary>
public enum StatusCanal
{
    /// <summary>Aceito pelo provedor, esperando a confirmação (webhook) de que saiu.</summary>
    Pendente = 1,
    Enviado = 2,
    Falhou = 3,
}

public static class TransicaoStatusCanal
{
    /// <summary>
    /// Só sai de <see cref="StatusCanal.Pendente"/>: um evento atrasado ou repetido do webhook
    /// nunca desfaz um resultado já registrado (ex.: "falhou" chegando depois de "entregue").
    /// </summary>
    public static bool PodeMudar(StatusCanal? atual, StatusCanal novo) =>
        atual == StatusCanal.Pendente && novo != StatusCanal.Pendente;
}
