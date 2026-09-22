using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.Seguranca;

/// <summary>JWT HMAC-SHA256 de curta duração (seção 4), com negócio, perfil e permissões como claims.</summary>
public sealed class GeradorTokenAcesso : IGeradorTokenAcesso
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly OpcoesJwt _opcoes;

    public GeradorTokenAcesso(IOptions<OpcoesJwt> opcoes)
    {
        _opcoes = opcoes.Value;
    }

    public ResultadoTokenAcesso Gerar(Usuario usuario)
    {
        var agora = DateTime.UtcNow;
        var expiraEm = agora.AddMinutes(_opcoes.AccessTokenMinutos);

        var claims = new Dictionary<string, object>
        {
            ["sub"] = usuario.Id.ToString(),
            ["email"] = usuario.Email,
            [ClaimsPlataforma.NegocioId] = usuario.NegocioId.ToString(),
            [ClaimsPlataforma.Perfil] = usuario.Perfil.ToString(),
            [ClaimsPlataforma.Permissao] = usuario.Permissoes.Select(p => p.Permissao.ToString()).ToArray(),
        };

        var chaveSimetrica = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opcoes.ChaveSecreta));
        var credenciais = new SigningCredentials(chaveSimetrica, SecurityAlgorithms.HmacSha256);

        var descritor = new SecurityTokenDescriptor
        {
            Issuer = _opcoes.Emissor,
            Audience = _opcoes.Audiencia,
            IssuedAt = agora,
            NotBefore = agora,
            Expires = expiraEm,
            SigningCredentials = credenciais,
            Claims = claims,
        };

        var token = Handler.CreateToken(descritor);
        return new ResultadoTokenAcesso(token, expiraEm);
    }
}
