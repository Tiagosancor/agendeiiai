using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Negocios;

public sealed class GerenciadorPerfilNegocio : IGerenciadorPerfilNegocio
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public GerenciadorPerfilNegocio(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<PerfilNegocio?> ObterAsync(CancellationToken cancellationToken = default)
    {
        var negocio = await BuscarNegocioAtualAsync(cancellationToken);
        return negocio is null ? null : Mapear(negocio);
    }

    public async Task<bool> AtualizarAsync(AtualizarPerfilNegocio dados, CancellationToken cancellationToken = default)
    {
        var negocio = await BuscarNegocioAtualAsync(cancellationToken);
        if (negocio is null)
            return false;

        var endereco = new Endereco(dados.Bairro, dados.Cidade, dados.Rua, dados.Numero, dados.Cep);
        var redesSociais = new RedesSociais(dados.Instagram, dados.Facebook, dados.WhatsApp);

        negocio.AtualizarPerfil(
            dados.NomeExibido, dados.LogoUrl, dados.CorPrimaria, dados.CorSecundaria,
            dados.TituloPagina, dados.SubtituloPagina, dados.TextoSobre, endereco, dados.Telefone, redesSociais);

        negocio.DefinirHorarioFuncionamento(dados.HorarioFuncionamento.Select(h =>
            new HorarioFuncionamentoDia((DiaSemana)h.DiaSemana, h.Abertura, h.Fechamento, h.Fechado)));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// O negócio não implementa <c>IEntidadeDoNegocio</c> (ele É o tenant, não pertence a
    /// um), então o filtro global não se aplica — busca pelo id vindo do contexto explicitamente.
    /// </summary>
    private Task<Negocio?> BuscarNegocioAtualAsync(CancellationToken cancellationToken)
    {
        var negocioId = _contextoNegocio.NegocioId;
        return negocioId is null
            ? Task.FromResult<Negocio?>(null)
            : _dbContext.Negocios.FirstOrDefaultAsync(n => n.Id == negocioId, cancellationToken);
    }

    private static PerfilNegocio Mapear(Negocio negocio) => new(
        negocio.Slug.Valor, negocio.NomeExibido, negocio.Tipo.ToString(), negocio.LogoUrl,
        negocio.CorPrimaria, negocio.CorSecundaria, negocio.TituloPagina, negocio.SubtituloPagina, negocio.TextoSobre,
        negocio.Endereco.Bairro, negocio.Endereco.Cidade, negocio.Endereco.Rua, negocio.Endereco.Numero, negocio.Endereco.Cep,
        negocio.Telefone, negocio.RedesSociais.Instagram, negocio.RedesSociais.Facebook, negocio.RedesSociais.WhatsApp,
        negocio.HorarioFuncionamento
            .Select(h => new HorarioFuncionamentoDiaDto((int)h.DiaSemana, h.Abertura, h.Fechamento, h.Fechado))
            .ToList());
}
