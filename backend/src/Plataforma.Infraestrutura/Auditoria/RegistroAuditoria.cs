using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Dominio.Auditoria;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Auditoria;

public sealed class RegistroAuditoria : IRegistroAuditoria
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IUsuarioAtual _usuarioAtual;

    public RegistroAuditoria(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IUsuarioAtual usuarioAtual)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _usuarioAtual = usuarioAtual;
    }

    public void Registrar(string acao, string entidade, Guid entidadeId, string? detalhes = null) =>
        _dbContext.LogsAuditoriaNegocio.Add(new LogAuditoriaNegocio(
            _contextoNegocio.NegocioId!.Value, _usuarioAtual.UsuarioId, _usuarioAtual.Email ?? "sistema",
            acao, entidade, entidadeId, detalhes));
}
