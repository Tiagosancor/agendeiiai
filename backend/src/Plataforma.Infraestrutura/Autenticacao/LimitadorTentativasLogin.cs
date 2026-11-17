using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Autenticacao;

/// <summary>
/// Contadores de abuso em Postgres (tabela <c>tentativas_login</c>), não em Redis: o Redis do agente funciona em modo "falha =
/// segue", inaceitável para controle de segurança. A chave é o HMAC-SHA256 (pimenta <c>Verificacao__ChaveHmac</c>) de um texto
/// com prefixo de domínio — nenhum identificador, e-mail ou id em claro no banco. Cada incremento é um
/// <c>INSERT ... ON CONFLICT DO UPDATE</c> atômico (sem corrida entre requisições simultâneas).
/// Usado pelo login (por identificador), por "Minha conta" (<c>conta:{id}</c>, senha atual) e pelos limites de pedido de código.
/// </summary>
public sealed class LimitadorTentativasLogin
{
    private readonly PlataformaDbContext _dbContext;
    private readonly OpcoesVerificacao _opcoesVerificacao;
    private readonly OpcoesAutenticacao _opcoes;

    public LimitadorTentativasLogin(
        PlataformaDbContext dbContext, IOptions<OpcoesVerificacao> opcoesVerificacao, IOptions<OpcoesAutenticacao> opcoes)
    {
        _dbContext = dbContext;
        _opcoesVerificacao = opcoesVerificacao.Value;
        _opcoes = opcoes.Value;
    }

    /// <summary>HMAC-SHA256 em hexadecimal de <c>{prefixo}:{valor}</c>.</summary>
    public string Chave(string prefixo, string valor)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_opcoesVerificacao.ChaveHmac));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{prefixo}:{valor}")));
    }

    public string ChaveDoIdentificador(string identificadorNormalizado) => Chave("login", identificadorNormalizado);

    public string ChaveDaConta(Guid usuarioId) => Chave("conta", usuarioId.ToString("N"));

    public Task<bool> BloqueadaAsync(string chave, CancellationToken cancellationToken)
    {
        var agora = DateTimeOffset.UtcNow;
        return _dbContext.TentativasLogin.AsNoTracking()
            .AnyAsync(t => t.Chave == chave && t.BloqueadoAte != null && t.BloqueadoAte > agora, cancellationToken);
    }

    /// <summary>Soma uma falha; na que atinge o máximo dentro da janela, bloqueia a chave pela duração da janela.</summary>
    public Task RegistrarFalhaAsync(string chave, CancellationToken cancellationToken) =>
        IncrementarAsync(chave, _opcoes.JanelaBloqueioLoginMinutos, _opcoes.MaximoFalhasLoginPorIdentificador, cancellationToken);

    /// <summary>
    /// Conta um uso da chave numa janela de <paramref name="janelaMinutos"/> e devolve quantos já houve (inclusive este).
    /// Não bloqueia sozinho: quem chama compara com o seu limite.
    /// </summary>
    public Task<int> ConsumirAsync(string chave, int janelaMinutos, CancellationToken cancellationToken) =>
        IncrementarAsync(chave, janelaMinutos, maximoParaBloquear: 0, cancellationToken);

    public Task ZerarAsync(string chave, CancellationToken cancellationToken) =>
        _dbContext.TentativasLogin.Where(t => t.Chave == chave).ExecuteDeleteAsync(cancellationToken);

    private async Task<int> IncrementarAsync(string chave, int janelaMinutos, int maximoParaBloquear, CancellationToken cancellationToken)
    {
        var agora = DateTimeOffset.UtcNow;
        var inicioDaJanela = agora.AddMinutes(-janelaMinutos);
        var bloqueioAte = agora.AddMinutes(janelaMinutos);

        // Todas as expressões do SET enxergam a linha ANTIGA (semântica do Postgres), então a janela vencida zera tudo junto.
        const string sql = """
            INSERT INTO tentativas_login (chave, janela_inicio, falhas, bloqueado_ate, atualizado_em)
            VALUES (@chave, @agora, 1, CASE WHEN @maximo > 0 AND 1 >= @maximo THEN @bloqueio_ate ELSE NULL END, @agora)
            ON CONFLICT (chave) DO UPDATE SET
                falhas = CASE WHEN tentativas_login.janela_inicio < @inicio_janela THEN 1 ELSE tentativas_login.falhas + 1 END,
                bloqueado_ate = CASE
                    WHEN tentativas_login.janela_inicio < @inicio_janela
                        THEN (CASE WHEN @maximo > 0 AND 1 >= @maximo THEN @bloqueio_ate ELSE NULL END)
                    ELSE (CASE WHEN @maximo > 0 AND tentativas_login.falhas + 1 >= @maximo THEN @bloqueio_ate ELSE tentativas_login.bloqueado_ate END)
                END,
                janela_inicio = CASE WHEN tentativas_login.janela_inicio < @inicio_janela THEN @agora ELSE tentativas_login.janela_inicio END,
                atualizado_em = @agora
            RETURNING falhas;
            """;

        var conexao = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var comando = conexao.CreateCommand();
            comando.CommandText = sql;
            comando.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
            comando.Parameters.AddWithValue("chave", chave);
            comando.Parameters.AddWithValue("agora", agora);
            comando.Parameters.AddWithValue("inicio_janela", inicioDaJanela);
            comando.Parameters.AddWithValue("bloqueio_ate", bloqueioAte);
            comando.Parameters.AddWithValue("maximo", maximoParaBloquear);

            return Convert.ToInt32(await comando.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            await _dbContext.Database.CloseConnectionAsync();
        }
    }
}
