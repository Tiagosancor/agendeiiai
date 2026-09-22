using System.ComponentModel.DataAnnotations;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>Provedor de e-mail (seção 4) — <c>Fake</c> em dev/testes, <c>Resend</c> decidido para produção.</summary>
public sealed class OpcoesEmail : IValidatableObject
{
    public const string Secao = "Email";

    public ProvedorEmail Provedor { get; set; } = ProvedorEmail.Fake;

    public OpcoesResend Resend { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provedor == ProvedorEmail.Resend && string.IsNullOrWhiteSpace(Resend.ApiKey))
        {
            yield return new ValidationResult(
                "Email__Resend__ApiKey é obrigatório quando Email__Provedor=Resend.", [nameof(Resend)]);
        }
    }
}

public enum ProvedorEmail
{
    Fake = 1,
    Resend = 2,
}

public sealed class OpcoesResend
{
    public string? ApiKey { get; set; }
}
