using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Notificacoes;

namespace Plataforma.Dominio.Verificacao;

/// <summary>
/// Código de confirmação do cliente final (seção 8.1). Guardado só como hash — nunca em
/// texto puro (exceto o provedor <c>Fake</c> em desenvolvimento, que loga fora desta
/// classe). Válido por poucos minutos, uso único, poucas tentativas (padrões em
/// <c>OpcoesVerificacao</c>); pedir um novo código invalida o anterior (ver <see cref="Invalidar"/>,
/// chamado pela infraestrutura ao criar o próximo registro para o mesmo negócio+telefone).
/// Reenviar (<see cref="Reenviar"/>) troca o código no mesmo registro, até um limite.
/// </summary>
public class CodigoVerificacao : EntidadeBase, IEntidadeDoNegocio
{
    public const int ValidadePadraoMinutos = 5;
    public const int MaximoTentativasPadrao = 3;

    public Guid NegocioId { get; private set; }

    public TelefoneE164 Telefone { get; private set; } = null!;

    public string HashCodigo { get; private set; } = string.Empty;

    public DateTimeOffset ExpiraEm { get; private set; }

    public int TentativasRestantes { get; private set; }

    public bool Usado { get; private set; }

    /// <summary>Marcado quando um código mais novo é solicitado para o mesmo negócio+telefone.</summary>
    public bool Invalidado { get; private set; }

    /// <summary>Quantas vezes o cliente pediu "reenviar" este código (seção 8.1, limite configurável).</summary>
    public int TentativasReenvio { get; private set; }

    /// <summary>Nulo = canal não usado (ex.: teto diário de WhatsApp do negócio atingido).</summary>
    public StatusCanal? CanalWhatsAppStatus { get; private set; }

    /// <summary>Nulo = o cliente não informou e-mail.</summary>
    public StatusCanal? CanalEmailStatus { get; private set; }

    /// <summary>ID da mensagem no provedor de WhatsApp — a chave com que o webhook de status acha este registro.</summary>
    public string? IdMensagemWhatsApp { get; private set; }

    protected CodigoVerificacao()
    {
    }

    private CodigoVerificacao(Guid negocioId, TelefoneE164 telefone, string hashCodigo, DateTimeOffset expiraEm, int maximoTentativas)
    {
        NegocioId = negocioId;
        Telefone = telefone;
        HashCodigo = hashCodigo;
        ExpiraEm = expiraEm;
        TentativasRestantes = maximoTentativas;
    }

    public static CodigoVerificacao Criar(
        Guid negocioId, TelefoneE164 telefone, string hashCodigo, DateTimeOffset agora,
        int validadeMinutos = ValidadePadraoMinutos, int maximoTentativas = MaximoTentativasPadrao) =>
        new(negocioId, telefone, hashCodigo, agora.AddMinutes(validadeMinutos), maximoTentativas);

    public bool EstaValido(DateTimeOffset agora) => !Usado && !Invalidado && agora <= ExpiraEm && TentativasRestantes > 0;

    /// <summary>Confere o hash informado; consome uma tentativa em caso de erro. Uso único: marca como usado em caso de acerto.</summary>
    public bool ConferirEMarcar(string hashInformado, DateTimeOffset agora)
    {
        if (!EstaValido(agora))
            return false;

        if (!string.Equals(HashCodigo, hashInformado, StringComparison.Ordinal))
        {
            TentativasRestantes--;
            return false;
        }

        Usado = true;
        return true;
    }

    public void Invalidar() => Invalidado = true;

    public bool PodeReenviar(int maximoReenvios) => !Usado && !Invalidado && TentativasReenvio < maximoReenvios;

    /// <summary>
    /// Troca o código (o anterior deixa de valer: só o hash novo fica guardado), renova prazo e
    /// tentativas e zera o resultado dos canais — o envio novo grava o seu.
    /// </summary>
    public void Reenviar(
        string novoHash, DateTimeOffset agora, int maximoReenvios,
        int validadeMinutos = ValidadePadraoMinutos, int maximoTentativas = MaximoTentativasPadrao)
    {
        if (!PodeReenviar(maximoReenvios))
            throw new InvalidOperationException("Este código não pode mais ser reenviado.");

        TentativasReenvio++;
        HashCodigo = novoHash;
        ExpiraEm = agora.AddMinutes(validadeMinutos);
        TentativasRestantes = maximoTentativas;
        CanalWhatsAppStatus = null;
        CanalEmailStatus = null;
        IdMensagemWhatsApp = null;
    }

    public void RegistrarEnvio(StatusCanal? whatsApp, string? idMensagemWhatsApp, StatusCanal? email)
    {
        CanalWhatsAppStatus = whatsApp;
        IdMensagemWhatsApp = idMensagemWhatsApp;
        CanalEmailStatus = email;
    }

    public bool AtualizarStatusWhatsApp(StatusCanal novo)
    {
        if (!TransicaoStatusCanal.PodeMudar(CanalWhatsAppStatus, novo))
            return false;

        CanalWhatsAppStatus = novo;
        return true;
    }
}
