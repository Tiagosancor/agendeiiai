using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Monta, no SERVIDOR, o texto com o link de gestão que a ferramenta <c>enviar_link_gestao</c> pediu (o texto do modelo é descartado).
/// Relê a elegibilidade do agendamento para o número da conversa (o estado pode ter mudado desde o pedido do modelo) e só então gera o
/// token (<see cref="IServicoTokenPublico"/>). O token existe só no texto enviado ao cliente: o que vai ao banco e ao histórico da IA é
/// o marcador <see cref="TextosAgendamentoAgente.LinkDeGestaoOculto"/>. Nunca loga o token nem dado do cliente.
/// </summary>
public sealed class EmissorLinkGestaoAgente
{
    private readonly ConsultaAgendamentosDoClienteAgente _consulta;
    private readonly IGestaoPublicaAgendamento _gestao;
    private readonly IServicoTokenPublico _servicoToken;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly OpcoesMarca _marca;

    public EmissorLinkGestaoAgente(
        ConsultaAgendamentosDoClienteAgente consulta, IGestaoPublicaAgendamento gestao, IServicoTokenPublico servicoToken,
        IContextoNegocio contextoNegocio, IOptions<OpcoesMarca> marca)
    {
        _consulta = consulta;
        _gestao = gestao;
        _servicoToken = servicoToken;
        _contextoNegocio = contextoNegocio;
        _marca = marca.Value;
    }

    /// <param name="slug">Slug do negócio (monta o link público).</param>
    /// <param name="telefoneInstancia">Telefone conectado da instância (o único número que pode aparecer no texto).</param>
    public async Task<MensagemDoServidor> MontarAsync(
        Guid conversaId, Guid agendamentoId, string slug, string? telefoneInstancia, CancellationToken cancellationToken)
    {
        var linkPublico = ConstrutorUrlPublica.Construir(_marca, slug, string.Empty);
        var textos = new TextosAgendamentoAgente(linkPublico, telefoneInstancia);

        // G1 refeito no envio: lê o detalhe e RECONFERE o vínculo (telefone, cliente não excluído, negócio, origem, status) numa única
        // consulta imediatamente antes de gerar o token, sem I/O externo entre a conferência e a emissão.
        var detalhe = await _gestao.ObterAsync(agendamentoId, cancellationToken);
        if (detalhe is null || !await _consulta.ElegivelAsync(conversaId, agendamentoId, cancellationToken))
        {
            var texto = textos.LinkIndisponivel();
            return new MensagemDoServidor(texto, texto, MarcadoresAgente.GestaoNaoEnviada);
        }

        var dados = new DadosDoAgendamentoParaTexto(
            string.Empty, string.Join(", ", detalhe.Servicos), 0m, detalhe.DuracaoMinutos, detalhe.Profissional.Nome, detalhe.Inicio, detalhe.Fuso);
        var token = _servicoToken.GerarTokenAgendamento(_contextoNegocio.NegocioId!.Value, agendamentoId);
        var link = ConstrutorUrlPublica.Construir(_marca, slug, $"/agendamentos/{token}");
        return new MensagemDoServidor(textos.LinkGestao(dados, link), textos.LinkGestao(dados, null), MarcadoresAgente.GestaoLink);
    }
}
