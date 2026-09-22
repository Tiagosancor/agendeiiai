using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Autenticacao;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Autenticacao;

/// <summary>
/// Login e renovação. Usa <c>IgnoreQueryFilters()</c> propositalmente: antes de existir um
/// token não há negócio resolvido (o painel inteiro fica em <c>app.{dominio}</c> — seção 5),
/// então é o e-mail (ou o refresh token) que precisa ser buscado através de todos os
/// negócios. Esta é exatamente a "rotina administrativa explícita e revisada" citada em
/// docs/decisoes.md como a única desculpa aceitável para ignorar o filtro multi-tenant.
/// </summary>
public sealed class ServicoAutenticacao : IServicoAutenticacao
{
    private readonly PlataformaDbContext _dbContext;
    private readonly ISenhaHasher _senhaHasher;
    private readonly IGeradorTokenAcesso _geradorToken;
    private readonly OpcoesJwt _opcoesJwt;

    public ServicoAutenticacao(
        PlataformaDbContext dbContext, ISenhaHasher senhaHasher, IGeradorTokenAcesso geradorToken,
        IOptions<OpcoesJwt> opcoesJwt)
    {
        _dbContext = dbContext;
        _senhaHasher = senhaHasher;
        _geradorToken = geradorToken;
        _opcoesJwt = opcoesJwt.Value;
    }

    public async Task<ResultadoLogin> LoginAsync(string email, string senha, CancellationToken cancellationToken = default)
    {
        var emailNormalizado = email.Trim().ToLowerInvariant();

        var usuario = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .Include(u => u.Permissoes)
            .FirstOrDefaultAsync(u => u.Email == emailNormalizado, cancellationToken);

        // Resultado idêntico para e-mail inexistente e senha errada — não dá pista
        // de qual das duas coisas falhou (mesmo espírito anti-enumeração da seção 8.1.3,
        // ainda que este não seja um endpoint público).
        if (usuario is null || !usuario.Ativo || !_senhaHasher.Verificar(senha, usuario.SenhaHash))
            return ResultadoLogin.Falha;

        return await EmitirParDeTokensAsync(usuario, cancellationToken);
    }

    public async Task<ResultadoLogin> RenovarAsync(string refreshTokenBruto, CancellationToken cancellationToken = default)
    {
        var hash = HashDoToken(refreshTokenBruto);

        var tokenAtual = await _dbContext.TokensAtualizacao
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Hash == hash, cancellationToken);

        if (tokenAtual is null || !tokenAtual.Valido)
            return ResultadoLogin.Falha;

        var usuario = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .Include(u => u.Permissoes)
            .FirstOrDefaultAsync(u => u.Id == tokenAtual.UsuarioId, cancellationToken);

        if (usuario is null || !usuario.Ativo)
            return ResultadoLogin.Falha;

        tokenAtual.Revogar();

        return await EmitirParDeTokensAsync(usuario, cancellationToken);
    }

    public async Task LogoutAsync(string refreshTokenBruto, CancellationToken cancellationToken = default)
    {
        var hash = HashDoToken(refreshTokenBruto);

        var tokenAtual = await _dbContext.TokensAtualizacao
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Hash == hash, cancellationToken);

        if (tokenAtual is null)
            return;

        tokenAtual.Revogar();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ResultadoLogin> EmitirParDeTokensAsync(Usuario usuario, CancellationToken cancellationToken)
    {
        var tokenAcesso = _geradorToken.Gerar(usuario);

        var refreshTokenBruto = GerarTokenOpaco();
        var expiraEm = DateTimeOffset.UtcNow.AddDays(_opcoesJwt.RefreshTokenDias);

        var tokenAtualizacao = TokenAtualizacao.Criar(
            usuario.NegocioId, usuario.Id, HashDoToken(refreshTokenBruto), expiraEm);

        _dbContext.Add(tokenAtualizacao);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ResultadoLogin(true, tokenAcesso.Token, tokenAcesso.ExpiraEm, refreshTokenBruto, expiraEm);
    }

    private static string GerarTokenOpaco() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string HashDoToken(string tokenBruto) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(tokenBruto)));
}
