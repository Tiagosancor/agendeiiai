using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Aplicacao.Verificacao;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Verificacao;
using Plataforma.Infraestrutura.Notificacoes;
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
        var registro = CodigoVerificacao.Criar(
            negocioId, telefone, CalcularHash(codigoGerado), agora, _opcoes.ValidadeCodigoMinutos, _opcoes.MaximoTentativasValidacao);

        _dbContext.CodigosVerificacao.Add(registro);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await EnviarERegistrarAsync(registro, telefone, email, codigoGerado, cancellationToken);

        return new ResultadoSolicitarCodigo(true);
    }

    public async Task<ResultadoSolicitarCodigo> ReenviarCodigoAsync(
        TelefoneE164 telefone, string? email, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;

        var registro = await _dbContext.CodigosVerificacao
            .Where(c => c.NegocioId == negocioId && c.Telefone == telefone && !c.Usado && !c.Invalidado)
            .OrderByDescending(c => c.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);

        // Nada em vigor (nunca pediu, já usou ou pediu outro): é um pedido novo, com o rate limit de sempre.
        if (registro is null)
            return await SolicitarCodigoAsync(telefone, email, cancellationToken);

        // Limite de reenvios do mesmo código. A resposta não diz qual canal falhou (seção 8.1.3).
        if (!registro.PodeReenviar(_opcoes.MaximoReenvios))
            return new ResultadoSolicitarCodigo(false, LimiteReenviosAtingido: true);

        var codigoGerado = GerarCodigoAleatorio();
        registro.Reenviar(
            CalcularHash(codigoGerado), DateTimeOffset.UtcNow, _opcoes.MaximoReenvios,
            _opcoes.ValidadeCodigoMinutos, _opcoes.MaximoTentativasValidacao);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await EnviarERegistrarAsync(registro, telefone, email, codigoGerado, cancellationToken);

        return new ResultadoSolicitarCodigo(true);
    }

    /// <summary>
    /// Envia pelos dois canais e grava o resultado de cada um no registro — é aí que o webhook de
    /// status do WhatsApp atualiza depois (seção 10). O código já está salvo antes do envio: se a
    /// gravação do resultado falhar, o cliente ainda consegue validar o que recebeu.
    /// </summary>
    private async Task EnviarERegistrarAsync(
        CodigoVerificacao registro, TelefoneE164 telefone, string? email, string codigo, CancellationToken cancellationToken)
    {
        var resultado = await _notificador.EnviarCodigoVerificacaoAsync(telefone, email, codigo, _opcoes.ValidadeCodigoMinutos, cancellationToken);

        registro.RegistrarEnvio(resultado.WhatsApp, resultado.IdMensagemWhatsApp, resultado.Email);
        foreach (var status in await EventosWhatsAppRecebidos.ListarAsync(_dbContext, resultado.IdMensagemWhatsApp, cancellationToken))
            registro.AtualizarStatusWhatsApp(status);

        await _dbContext.SaveChangesAsync(cancellationToken);
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
