using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Negocios;

/// <summary>
/// Raiz do multi-tenant: cada negócio (barbearia, salão, clínica de estética ou
/// autônomo) é um tenant isolado, resolvido pelo <see cref="Slug"/> no subdomínio.
/// Campos de marca, endereço e horário de funcionamento entram na Sprint 1 —
/// aqui fica só o essencial para a Sprint 0 (fundação e resolução por subdomínio).
/// </summary>
public class Negocio : EntidadeBase
{
    public Slug Slug { get; private set; } = null!;

    public string NomeExibido { get; private set; } = string.Empty;

    public TipoNegocio Tipo { get; private set; }

    /// <summary>Fuso horário IANA do negócio (ex.: "America/Sao_Paulo"). Datas ficam em UTC no banco (seção 8.2.6).</summary>
    public string Fuso { get; private set; } = "America/Sao_Paulo";

    public bool Ativo { get; private set; } = true;

    protected Negocio()
    {
        // Uso exclusivo do EF Core.
    }

    private Negocio(Slug slug, string nomeExibido, TipoNegocio tipo, string fuso)
    {
        Slug = slug;
        NomeExibido = nomeExibido;
        Tipo = tipo;
        Fuso = fuso;
    }

    public static Negocio Criar(Slug slug, string nomeExibido, TipoNegocio tipo, string fuso = "America/Sao_Paulo")
    {
        ArgumentNullException.ThrowIfNull(slug);

        if (string.IsNullOrWhiteSpace(nomeExibido))
            throw new ArgumentException("O nome exibido é obrigatório.", nameof(nomeExibido));

        if (string.IsNullOrWhiteSpace(fuso))
            throw new ArgumentException("O fuso horário é obrigatório.", nameof(fuso));

        return new Negocio(slug, nomeExibido.Trim(), tipo, fuso);
    }

    public void Desativar() => Ativo = false;

    public void Ativar() => Ativo = true;
}
