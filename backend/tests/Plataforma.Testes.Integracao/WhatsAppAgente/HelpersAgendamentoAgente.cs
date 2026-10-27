using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.WhatsAppAgente.Ia;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Testes.Integracao.Infraestrutura;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>Apoio dos testes de agendamento pelo agente (Fase 3): JSON dos argumentos e leituras do banco.</summary>
public static class HelpersAgendamentoAgente
{
    public const string Tel = AmbienteAgente.TelefoneCliente;
    public const string TelComMais = "+5571988887777";

    public static DateOnly Dia() => AmbienteAgente.ProximaSegunda();

    public static DateTimeOffset H(int hora, int minuto, DateOnly? dia = null) => SemeadorDeComissoes.Local(dia ?? Dia(), hora, minuto);

    /// <summary>Argumentos de <c>preparar_agendamento</c> como o modelo os mandaria.</summary>
    public static string Prepara(CenarioAgente c, Guid profissional, string hora, string? nome = null, DateOnly? dia = null, Guid? servico = null)
    {
        var data = (dia ?? Dia()).ToString("yyyy-MM-dd");
        var extra = nome is null ? string.Empty : ",\"nome_cliente\":\"" + nome + "\"";
        return "{\"servico_id\":\"" + (servico ?? c.Corte) + "\",\"profissional_id\":\"" + profissional + "\",\"data\":\"" + data
            + "\",\"hora\":\"" + hora + "\"" + extra + "}";
    }

    /// <summary>Código de erro do resultado de ferramenta mais recente enviado ao modelo (nulo = sem erro).</summary>
    public static string? Erro(AiRequest requisicao)
    {
        var resultado = ProvedorIaRoteirizado.Resultados(requisicao).LastOrDefault();
        return resultado.Conteudo.ValueKind == JsonValueKind.Object && resultado.Conteudo.TryGetProperty("erro", out var erro)
            ? erro.GetString()
            : null;
    }

    public static Task<List<PendenciaAgendamentoWhatsApp>> PendenciasAsync(this AmbienteAgente ambiente) =>
        ambiente.NoBancoAsync(db => db.PendenciasAgendamentoWhatsApp.IgnoreQueryFilters().AsNoTracking().OrderBy(p => p.CriadoEm).ToListAsync());

    public static Task<List<Agendamento>> AgendamentosAsync(this AmbienteAgente ambiente) =>
        ambiente.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AsNoTracking().Include(a => a.Servicos).OrderBy(a => a.CriadoEm).ToListAsync());

    public static async Task<List<Plataforma.Dominio.Clientes.Cliente>> ClientesAsync(this AmbienteAgente ambiente) =>
        await ambiente.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters().AsNoTracking().ToListAsync());

    public static Task<List<MensagemWhatsApp>> MarcadoresAsync(this AmbienteAgente ambiente, string marcador) =>
        ambiente.NoBancoAsync(db => db.MensagensWhatsApp.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.NomeFerramenta == marcador).OrderBy(m => m.CriadoEm).ToListAsync());
}
