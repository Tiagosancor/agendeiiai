using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// E-mail ao dono quando o agente chama um atendente (G13). Quem decidiu se PODE avisar (1 por conversa a cada N horas, teto por
/// negócio/dia) foi o <see cref="GerenciadorConversasWhatsApp"/>; aqui só se acha quem recebe (usuários ativos com
/// <see cref="Permissao.AtenderConversasWhatsApp"/>) e se envia. O e-mail leva o telefone mascarado, o motivo (código fechado) e o link
/// da conversa — nunca texto da conversa. Falha de envio nunca derruba a rodada.
/// </summary>
public sealed class AvisadorAtendimentoHumano
{
    private readonly PlataformaDbContext _dbContext;
    private readonly INotificador _notificador;
    private readonly OpcoesMarca _marca;
    private readonly ILogger<AvisadorAtendimentoHumano> _logger;

    public AvisadorAtendimentoHumano(
        PlataformaDbContext dbContext, INotificador notificador, IOptions<OpcoesMarca> marca, ILogger<AvisadorAtendimentoHumano> logger)
    {
        _dbContext = dbContext;
        _notificador = notificador;
        _marca = marca.Value;
        _logger = logger;
    }

    public async Task AvisarAsync(Guid negocioId, Guid conversaId, string telefoneConversa, string? motivo, CancellationToken cancellationToken)
    {
        try
        {
            var emails = await _dbContext.Usuarios.AsNoTracking()
                .Where(u => u.NegocioId == negocioId && u.Ativo && !u.Excluido && u.Email != ""
                    && u.Permissoes.Any(p => p.Permissao == Permissao.AtenderConversasWhatsApp))
                .Select(u => u.Email).ToListAsync(cancellationToken);
            if (emails.Count == 0)
                return;

            var nome = await _dbContext.Negocios.AsNoTracking().Where(n => n.Id == negocioId).Select(n => n.NomeExibido)
                .FirstOrDefaultAsync(cancellationToken);
            if (nome is null)
                return;

            var mascarado = TelefoneE164.TentarCriar(telefoneConversa, out var telefone) && telefone is not null ? telefone.Mascarado() : "+****";
            await _notificador.EnviarAvisoAtendimentoHumanoAsync(new DadosAvisoAtendimentoHumano(
                emails, nome, mascarado, TextosHumanoAgente.MotivoParaEmail(motivo),
                ConstrutorUrlPublica.ConstruirPainel(_marca, $"/painel/whatsapp/conversas/{conversaId}")), cancellationToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            _logger.LogWarning("Agente: não foi possível avisar o atendente (conversa {ConversaId}): {Tipo}.", conversaId, excecao.GetType().Name);
        }
    }
}
