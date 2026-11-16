using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Usuarios;

/// <summary>
/// Conta de respostas 409 de identificador indisponível por autor (<c>sub</c> do token), em memória (por instância da API),
/// janela de 1 hora. Acima do limite, quem tenta recebe 429: o painel não vira um enumerador barato de telefones e nomes de
/// usuário. Singleton.
/// </summary>
public sealed class LimitadorConflitosIdentificador
{
    private readonly ConcurrentDictionary<Guid, (DateTimeOffset Inicio, int Quantidade)> _contagens = new();
    private readonly OpcoesAutenticacao _opcoes;

    public LimitadorConflitosIdentificador(IOptions<OpcoesAutenticacao> opcoes) => _opcoes = opcoes.Value;

    /// <summary>Lança <see cref="MuitasTentativasException"/> se o autor já estourou o limite na última hora.</summary>
    public void Garantir(Guid? autor)
    {
        if (autor is null || !_contagens.TryGetValue(autor.Value, out var atual))
            return;

        if (DateTimeOffset.UtcNow - atual.Inicio < TimeSpan.FromHours(1) && atual.Quantidade >= _opcoes.LimiteConflitosIdentificadorPorHora)
            throw new MuitasTentativasException();
    }

    public void Registrar(Guid? autor)
    {
        if (autor is null)
            return;

        var agora = DateTimeOffset.UtcNow;
        _contagens.AddOrUpdate(
            autor.Value,
            _ => (agora, 1),
            (_, atual) => agora - atual.Inicio >= TimeSpan.FromHours(1) ? (agora, 1) : (atual.Inicio, atual.Quantidade + 1));
    }
}

/// <summary>
/// Regras de e-mail, nome de usuário e telefone de login que valem em todos os caminhos que gravam identificadores (criar usuário,
/// editar acesso, dar acesso ao profissional, "Minha conta"): formato, reservados (inclusive o nome do produto), unicidade global
/// entre os não excluídos (com <c>IgnoreQueryFilters</c> — só procura o identificador, nunca devolve dado de outro negócio) e o
/// mapeamento do índice único que perdeu uma corrida para o campo certo. Nunca devolve nem registra o valor.
/// </summary>
public sealed class ServicoIdentificadoresAcesso
{
    public const string IndiceEmail = "ix_usuarios_email";
    public const string IndiceNomeUsuario = "ix_usuarios_nome_usuario";
    public const string IndiceTelefoneLogin = "ix_usuarios_telefone_login";

    private const int TentativasDeSugestao = 30;

    private readonly PlataformaDbContext _dbContext;
    private readonly OpcoesMarca _marca;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly LimitadorConflitosIdentificador _conflitos;

    public ServicoIdentificadoresAcesso(
        PlataformaDbContext dbContext, IOptions<OpcoesMarca> marca, IUsuarioAtual usuarioAtual, LimitadorConflitosIdentificador conflitos)
    {
        _dbContext = dbContext;
        _marca = marca.Value;
        _usuarioAtual = usuarioAtual;
        _conflitos = conflitos;
    }

    public static string? ParseEmail(string? texto)
    {
        try
        {
            return Usuario.NormalizarEmail(texto);
        }
        catch (ArgumentException)
        {
            throw new ErroDeAcessoException("email_invalido", "Informe um e-mail válido.");
        }
    }

    public NomeUsuario? ParseNomeUsuario(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        switch (NomeUsuario.Avaliar(texto, ReservadosDaMarca()))
        {
            case ProblemaNomeUsuario.Nenhum:
                return NomeUsuario.Criar(texto, ReservadosDaMarca());
            case ProblemaNomeUsuario.Reservado:
                throw new ErroDeAcessoException("nome_usuario_reservado", "Esse nome de usuário não pode ser usado. Escolha outro.");
            default:
                throw new ErroDeAcessoException("nome_usuario_invalido",
                    "O nome de usuário deve ter de 3 a 30 caracteres (letras, números, ponto, hífen e sublinhado), com pelo menos uma letra.");
        }
    }

    public static TelefoneE164? ParseTelefoneLogin(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        if (!TelefoneE164.TentarCriar(texto, out var telefone) || telefone is null)
            throw new ErroDeAcessoException("telefone_invalido", "Informe um telefone válido, com DDD.");

        return telefone.FormaCanonica();
    }

    /// <summary>
    /// Confere se algum dos identificadores já é de outra conta não excluída. Lança <see cref="IdentificadorIndisponivelException"/>
    /// (só o campo; para o nome de usuário, também uma sugestão) ou <see cref="MuitasTentativasException"/> se o autor passou do
    /// limite de conflitos.
    /// </summary>
    public async Task VerificarDisponibilidadeAsync(
        string? email, NomeUsuario? nomeUsuario, TelefoneE164? telefoneLogin, Guid? usuarioIgnorado, CancellationToken cancellationToken)
    {
        _conflitos.Garantir(_usuarioAtual.UsuarioId);

        var outros = _dbContext.Usuarios.IgnoreQueryFilters().AsNoTracking().Where(u => !u.Excluido && u.Id != usuarioIgnorado);

        if (email is not null && await outros.AnyAsync(u => u.Email == email, cancellationToken))
            throw Conflito(IdentificadorIndisponivelException.CampoEmail, null);

        if (nomeUsuario is not null && await outros.AnyAsync(u => u.NomeUsuario == nomeUsuario, cancellationToken))
            throw Conflito(IdentificadorIndisponivelException.CampoNomeUsuario, await SugerirNomeUsuarioAsync(nomeUsuario.Valor, cancellationToken));

        if (telefoneLogin is not null && await outros.AnyAsync(u => u.TelefoneLogin == telefoneLogin, cancellationToken))
            throw Conflito(IdentificadorIndisponivelException.CampoTelefoneLogin, null);
    }

    /// <summary>
    /// A gravação perdeu a corrida para o índice único de algum identificador? Devolve a exceção do campo certo (ou nulo se a
    /// violação é de outra coisa, ex.: o vínculo com o profissional).
    /// </summary>
    public async Task<IdentificadorIndisponivelException?> TraduzirViolacaoAsync(
        DbUpdateException excecao, NomeUsuario? nomeTentado, CancellationToken cancellationToken)
    {
        var campo = (excecao.InnerException as PostgresException)?.ConstraintName switch
        {
            IndiceEmail => IdentificadorIndisponivelException.CampoEmail,
            IndiceNomeUsuario => IdentificadorIndisponivelException.CampoNomeUsuario,
            IndiceTelefoneLogin => IdentificadorIndisponivelException.CampoTelefoneLogin,
            _ => null,
        };

        if (campo is null)
            return null;

        var sugestao = campo == IdentificadorIndisponivelException.CampoNomeUsuario && nomeTentado is not null
            ? await SugerirNomeUsuarioAsync(nomeTentado.Valor, cancellationToken)
            : null;
        return Conflito(campo, sugestao);
    }

    private IdentificadorIndisponivelException Conflito(string campo, string? sugestao)
    {
        _conflitos.Registrar(_usuarioAtual.UsuarioId);
        return new IdentificadorIndisponivelException(campo, sugestao);
    }

    /// <summary>Primeiro nome livre da forma <c>{base}2</c>, <c>{base}3</c>... (cabendo nos 30 caracteres); nulo se não achar.</summary>
    private async Task<string?> SugerirNomeUsuarioAsync(string baseDoNome, CancellationToken cancellationToken)
    {
        var reservados = ReservadosDaMarca();
        for (var n = 2; n < 2 + TentativasDeSugestao; n++)
        {
            var sufixo = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var raiz = baseDoNome.Length + sufixo.Length > NomeUsuario.TamanhoMaximo
                ? baseDoNome[..(NomeUsuario.TamanhoMaximo - sufixo.Length)].TrimEnd('.', '-', '_')
                : baseDoNome;
            var candidato = raiz + sufixo;

            if (NomeUsuario.Avaliar(candidato, reservados) != ProblemaNomeUsuario.Nenhum)
                continue;

            var nome = NomeUsuario.Criar(candidato, reservados);
            if (!await _dbContext.Usuarios.IgnoreQueryFilters().AsNoTracking().AnyAsync(u => !u.Excluido && u.NomeUsuario == nome, cancellationToken))
                return candidato;
        }

        return null;
    }

    /// <summary>Nome do produto (e a forma só com letras e números) como reservado — o domínio não conhece a marca.</summary>
    private IReadOnlyCollection<string> ReservadosDaMarca()
    {
        var produto = (_marca.NomeProduto ?? string.Empty).Trim().ToLowerInvariant();
        var compacto = new string(produto.Where(char.IsAsciiLetterOrDigit).ToArray());
        return new[] { produto, compacto }.Where(r => r.Length > 0).Distinct().ToArray();
    }
}
