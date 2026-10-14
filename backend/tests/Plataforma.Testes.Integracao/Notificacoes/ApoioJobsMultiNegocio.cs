using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.MultiTenant;
using Plataforma.Infraestrutura.Seguranca;

namespace Plataforma.Testes.Integracao.Notificacoes;

/// <summary>Guarda cada chamada a <c>Definir</c> com a instância (escopo) que a recebeu: prova que nenhum escopo troca de negócio.</summary>
public sealed class RegistroDeContextos
{
    public ConcurrentBag<(Guid Instancia, Guid Negocio)> Definicoes { get; } = [];
}

public sealed class ContextoNegocioEspiao(RegistroDeContextos registro) : IContextoNegocio
{
    private readonly ContextoNegocio _interno = new();
    private readonly Guid _instancia = Guid.NewGuid();

    public Guid? NegocioId => _interno.NegocioId;

    public bool TemNegocio => _interno.TemNegocio;

    public void Definir(Guid negocioId)
    {
        _interno.Definir(negocioId);
        registro.Definicoes.Add((_instancia, negocioId));
    }
}

/// <summary>Falha (com uma mensagem cheia de dado pessoal) só para o negócio-alvo, antes de qualquer envio.</summary>
public sealed class TokenQueFalhaParaNegocio(IServicoTokenPublico interno) : IServicoTokenPublico
{
    public Guid? NegocioAlvo { get; set; }

    public string Mensagem { get; set; } = "falha";

    public string GerarTokenAgendamento(Guid negocioId, Guid agendamentoId) =>
        negocioId == NegocioAlvo ? throw new InvalidOperationException(Mensagem) : interno.GerarTokenAgendamento(negocioId, agendamentoId);

    public string GerarTokenVerificacao(Guid negocioId, TelefoneE164 telefone) => interno.GerarTokenVerificacao(negocioId, telefone);

    public Task<bool> ValidarTokenVerificacaoAsync(string token, Guid negocioId, TelefoneE164 telefone) =>
        interno.ValidarTokenVerificacaoAsync(token, negocioId, telefone);

    public Task<Guid?> ValidarTokenAgendamentoAsync(string token, Guid negocioId) => interno.ValidarTokenAgendamentoAsync(token, negocioId);

    public string GerarTokenCadastro(string email) => interno.GerarTokenCadastro(email);

    public Task<string?> ValidarTokenCadastroAsync(string token) => interno.ValidarTokenCadastroAsync(token);
}

/// <summary>Falha (com uma mensagem cheia de dado pessoal) só para o negócio-alvo, na consulta de assinatura do lembrete de retorno.</summary>
public sealed class AssinaturaQueFalhaParaNegocio(IConsultaSituacaoAssinatura interno) : IConsultaSituacaoAssinatura
{
    public Guid? NegocioAlvo { get; set; }

    public string Mensagem { get; set; } = "falha";

    public Task<SituacaoAssinatura?> ObterAsync(Guid negocioId, CancellationToken cancellationToken = default) =>
        negocioId == NegocioAlvo ? throw new InvalidOperationException(Mensagem) : interno.ObterAsync(negocioId, cancellationToken);
}

public sealed class CapturaDeLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Linhas { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Registrador(this);

    public void Dispose()
    {
    }

    private sealed class Registrador(CapturaDeLogs captura) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            captura.Linhas.Enqueue($"{logLevel}: {formatter(state, exception)} {exception}");
    }
}

public static class ApoioJobsMultiNegocioExtensoes
{
    public static void AdicionarEspioes(this IServiceCollection servicos, RegistroDeContextos registro, CapturaDeLogs logs)
    {
        servicos.AddSingleton(registro);
        servicos.AddScoped<IContextoNegocio>(sp => new ContextoNegocioEspiao(sp.GetRequiredService<RegistroDeContextos>()));
        servicos.AddSingleton(logs);
        servicos.AddSingleton(typeof(ILogger<>), typeof(LoggerCapturado<>));
    }
}

/// <summary>O host usa Serilog, que ignora os <c>ILoggerProvider</c> do DI: o teste troca o <c>ILogger&lt;T&gt;</c> para capturar o que os serviços logam.</summary>
public sealed class LoggerCapturado<T>(CapturaDeLogs captura) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        captura.Linhas.Enqueue($"{logLevel}: {formatter(state, exception)} {exception}");
}
