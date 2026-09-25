using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Aplicacao.Verificacao;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Verificacao;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Verificacao;

/// <summary>
/// Código de confirmação do cliente final (seção 8.1). Nunca consulta a tabela de
/// clientes — a resposta não pode variar com o telefone existir ou não (anti-enumeração,
/// seção 8.1.3), então nem checar isso é necessário até a identificação na confirmação do
/// agendamento (seção 8.1.4).
/// </summary>
public sealed class ServicoVerificacao : IServicoVerificacao
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly INotificador _notificador;
    private readonly IServicoTokenPublico _servicoToken;
    private readonly OpcoesVerificacao _opcoes;

    public ServicoVerificacao(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, INotificador notificador,
        IServicoTokenPublico servicoToken, IOptions<OpcoesVerificacao> opcoes)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _notificador = notificador;
        _servicoToken = servicoToken;
        _opcoes = opcoes.Value;
    }

    public async Task<ResultadoSolicitarCodigo> SolicitarCodigoAsync(
        TelefoneE164 telefone, string? email, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        var agora = DateTimeOffset.UtcNow;

        var codigosUltimaHora = await _dbContext.CodigosVerificacao.CountAsync(
            c => c.NegocioId == negocioId && c.Telefone == telefone && c.CriadoEm >= agora.AddHours(-1), cancellationToken);

        if (codigosUltimaHora >= _opcoes.MaximoCodigosPorTelefonePorHora)
            return new ResultadoSolicitarCodigo(false, LimiteExcedido: true);

        var codigosUltimoDia = await _dbContext.CodigosVerificacao.CountAsync(
            c => c.NegocioId == negocioId && c.Telefone == telefone && c.CriadoEm >= agora.AddDays(-1), cancellationToken);

        if (codigosUltimoDia >= _opcoes.MaximoCodigosPorTelefonePorDia)
            return new ResultadoSolicitarCodigo(false, LimiteExcedido: true);

        // Pedir um novo código invalida o anterior (seção 8.1.2).
        await _dbContext.CodigosVerificacao
            .Where(c => c.NegocioId == negocioId && c.Telefone == telefone && !c.Usado && !c.Invalidado)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Invalidado, true), cancellationToken);

        var codigoGerado = GerarCodigoAleatorio();
        var registro = CodigoVerificacao.Criar(negocioId, telefone, CalcularHash(codigoGerado), agora);

        _dbContext.CodigosVerificacao.Add(registro);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _notificador.EnviarCodigoVerificacaoAsync(telefone, email, codigoGerado, cancellationToken);

        return new ResultadoSolicitarCodigo(true);
    }

    public async Task<ResultadoValidarCodigo> ValidarCodigoAsync(
        TelefoneE164 telefone, string codigo, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        var agora = DateTimeOffset.UtcNow;

        var registro = await _dbContext.CodigosVerificacao
            .Where(c => c.NegocioId == negocioId && c.Telefone == telefone)
            .OrderByDescending(c => c.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);

        if (registro is null || !registro.ConferirEMarcar(CalcularHash(codigo), agora))
        {
            if (registro is not null)
                await _dbContext.SaveChangesAsync(cancellationToken); // persiste a tentativa consumida

            return new ResultadoValidarCodigo(false, MensagemErro: "Código inválido ou expirado.");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var token = _servicoToken.GerarTokenVerificacao(negocioId, telefone);
        return new ResultadoValidarCodigo(true, token);
    }

    private static string GerarCodigoAleatorio() => CodigoConfirmacao.Gerar();

    private string CalcularHash(string codigo) => CodigoConfirmacao.Hash(_opcoes.ChaveHmac, codigo);
}
