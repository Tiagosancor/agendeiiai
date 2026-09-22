using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Verificacao;

/// <summary>
/// Código de confirmação do cliente final (seção 8.1). Guardado só como hash — nunca em
/// texto puro (exceto o provedor <c>Fake</c> em desenvolvimento, que loga fora desta
/// classe). Válido por 5 minutos, uso único, no máximo 5 tentativas; pedir um novo código
/// invalida o anterior (ver <see cref="Invalidar"/>, chamado pela infraestrutura ao criar
/// o próximo registro para o mesmo negócio+telefone).
/// </summary>
public class CodigoVerificacao : EntidadeBase, IEntidadeDoNegocio
{
    private const int MaximoTentativas = 5;

    public Guid NegocioId { get; private set; }

    public TelefoneE164 Telefone { get; private set; } = null!;

    public string HashCodigo { get; private set; } = string.Empty;

    public DateTimeOffset ExpiraEm { get; private set; }

    public int TentativasRestantes { get; private set; } = MaximoTentativas;

    public bool Usado { get; private set; }

    /// <summary>Marcado quando um código mais novo é solicitado para o mesmo negócio+telefone.</summary>
    public bool Invalidado { get; private set; }

    protected CodigoVerificacao()
    {
    }

    private CodigoVerificacao(Guid negocioId, TelefoneE164 telefone, string hashCodigo, DateTimeOffset expiraEm)
    {
        NegocioId = negocioId;
        Telefone = telefone;
        HashCodigo = hashCodigo;
        ExpiraEm = expiraEm;
    }

    public static CodigoVerificacao Criar(Guid negocioId, TelefoneE164 telefone, string hashCodigo, DateTimeOffset agora) =>
        new(negocioId, telefone, hashCodigo, agora.AddMinutes(5));

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
}
