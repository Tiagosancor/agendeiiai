using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.Negocios;

/// <summary>Monta URLs da página pública de um negócio (seção 6.3: links de cancelar/remarcar por e-mail) a partir da configuração de marca (seção 5).</summary>
public static class ConstrutorUrlPublica
{
    public static string Construir(OpcoesMarca opcoes, string slug, string caminho)
    {
        var porta = opcoes.PortaUrlPublica is int p ? $":{p}" : string.Empty;
        return $"{opcoes.EsquemaUrlPublica}://{slug}.{opcoes.Dominio}{porta}{caminho}";
    }
}
