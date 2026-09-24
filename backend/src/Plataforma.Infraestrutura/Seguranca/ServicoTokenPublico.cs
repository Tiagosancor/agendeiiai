using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.Seguranca;

/// <summary>
/// JWT HMAC-SHA256, mesma chave do painel mas audiências próprias (<see cref="AudienciaVerificacao"/>
/// / <see cref="AudienciaAgendamento"/>) — garante que um token de um tipo nunca valida como
/// outro nem como o access token do painel (seção 4/8.1.1.c).
/// </summary>
public sealed class ServicoTokenPublico : IServicoTokenPublico
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly OpcoesJwt _opcoes;

    public ServicoTokenPublico(IOptions<OpcoesJwt> opcoes)
    {
        _opcoes = opcoes.Value;
    }

    public string GerarTokenVerificacao(Guid negocioId, TelefoneE164 telefone) =>
        Gerar(AudienciaVerificacao(_opcoes.Audiencia), TimeSpan.FromMinutes(15), new Dictionary<string, object>
        {
            ["negocio_id"] = negocioId.ToString(),
            ["telefone"] = telefone.Valor,
        });

    public async Task<bool> ValidarTokenVerificacaoAsync(string token, Guid negocioId, TelefoneE164 telefone)
    {
        var resultado = await ValidarAsync(token, AudienciaVerificacao(_opcoes.Audiencia));
        if (!resultado.IsValid)
            return false;

        return ObterClaim(resultado, "negocio_id") == negocioId.ToString()
            && ObterClaim(resultado, "telefone") == telefone.Valor;
    }

    public string GerarTokenAgendamento(Guid negocioId, Guid agendamentoId) =>
        Gerar(AudienciaAgendamento(_opcoes.Audiencia), TimeSpan.FromDays(30), new Dictionary<string, object>
        {
            ["negocio_id"] = negocioId.ToString(),
            ["agendamento_id"] = agendamentoId.ToString(),
        });

    public async Task<Guid?> ValidarTokenAgendamentoAsync(string token, Guid negocioId)
    {
        var resultado = await ValidarAsync(token, AudienciaAgendamento(_opcoes.Audiencia));
        if (!resultado.IsValid)
            return null;

        if (ObterClaim(resultado, "negocio_id") != negocioId.ToString())
            return null;

        return Guid.TryParse(ObterClaim(resultado, "agendamento_id"), out var agendamentoId) ? agendamentoId : null;
    }

    public string GerarTokenCadastro(string email) =>
        Gerar(AudienciaCadastro(_opcoes.Audiencia), TimeSpan.FromMinutes(30), new Dictionary<string, object>
        {
            ["email"] = email,
        });

    public async Task<string?> ValidarTokenCadastroAsync(string token)
    {
        var resultado = await ValidarAsync(token, AudienciaCadastro(_opcoes.Audiencia));
        return resultado.IsValid ? ObterClaim(resultado, "email") : null;
    }

    private string Gerar(string audiencia, TimeSpan validade, Dictionary<string, object> claims)
    {
        var agora = DateTime.UtcNow;
        var chaveSimetrica = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opcoes.ChaveSecreta));

        var descritor = new SecurityTokenDescriptor
        {
            Issuer = _opcoes.Emissor,
            Audience = audiencia,
            IssuedAt = agora,
            NotBefore = agora,
            Expires = agora + validade,
            SigningCredentials = new SigningCredentials(chaveSimetrica, SecurityAlgorithms.HmacSha256),
            Claims = claims,
        };

        return Handler.CreateToken(descritor);
    }

    private Task<TokenValidationResult> ValidarAsync(string token, string audiencia) => Handler.ValidateTokenAsync(token, new TokenValidationParameters
    {
        ValidIssuer = _opcoes.Emissor,
        ValidAudience = audiencia,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opcoes.ChaveSecreta)),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
    });

    private static string? ObterClaim(TokenValidationResult resultado, string nome) =>
        resultado.ClaimsIdentity.Claims.FirstOrDefault(c => c.Type == nome)?.Value;

    private static string AudienciaVerificacao(string audienciaBase) => $"{audienciaBase}:verificacao";

    private static string AudienciaAgendamento(string audienciaBase) => $"{audienciaBase}:agendamento-publico";

    private static string AudienciaCadastro(string audienciaBase) => $"{audienciaBase}:cadastro";
}
