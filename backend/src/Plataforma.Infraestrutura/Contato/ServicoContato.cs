using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Contato;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Contato;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Contato;

public sealed class ServicoContato : IServicoContato
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly INotificador _notificador;

    public ServicoContato(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, INotificador notificador)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _notificador = notificador;
    }

    public async Task EnviarAsync(EnviarMensagemContato dados, CancellationToken cancellationToken = default)
    {
        var mensagem = MensagemContato.Criar(
            _contextoNegocio.NegocioId!.Value, dados.Nome, dados.Telefone, dados.Email, dados.Mensagem, DateTimeOffset.UtcNow);

        _dbContext.MensagensContato.Add(mensagem);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _notificador.EnviarMensagemContatoAsync(
            new DadosNotificacaoContato(dados.Nome, dados.Telefone, dados.Email, dados.Mensagem), cancellationToken);
    }
}
