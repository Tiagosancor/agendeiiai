using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Arquivos;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Arquivos;

/// <summary>
/// <see cref="IArmazenamentoArquivos"/> no Postgres (tabela <c>arquivos</c>, decisão do dono até existir um bucket): sem
/// conta nova, entra no backup e segue o banco numa migração. As imagens chegam aqui já reprocessadas (centenas de KB).
/// </summary>
public sealed class ArmazenamentoArquivosBanco : IArmazenamentoArquivos
{
    private readonly PlataformaDbContext _dbContext;

    public ArmazenamentoArquivosBanco(PlataformaDbContext dbContext) => _dbContext = dbContext;

    public async Task<string> SalvarAsync(Guid negocioId, string tipoConteudo, byte[] conteudo, CancellationToken cancellationToken = default)
    {
        var chave = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        _dbContext.Arquivos.Add(new ArquivoBanco(negocioId, chave, tipoConteudo, conteudo));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return chave;
    }

    public Task<ConteudoArquivo?> ObterAsync(string chave, CancellationToken cancellationToken = default) =>
        _dbContext.Arquivos.AsNoTracking()
            .Where(a => a.Chave == chave)
            .Select(a => new ConteudoArquivo(a.TipoConteudo, a.Conteudo))
            .FirstOrDefaultAsync(cancellationToken);

    public Task RemoverAsync(string chave, CancellationToken cancellationToken = default) =>
        _dbContext.Arquivos.Where(a => a.Chave == chave).ExecuteDeleteAsync(cancellationToken);

    public string UrlPublica(string chave) => $"/arquivos/{chave}";
}
