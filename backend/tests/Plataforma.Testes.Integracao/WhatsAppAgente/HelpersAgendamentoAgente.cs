using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Aplicacao.WhatsAppAgente.Ia;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
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

    /// <summary>
    /// Faz o modelo pedir <c>preparar_agendamento</c> uma vez e depois só responder "ok". Devolve o erro que ele viu
    /// (nulo = a rodada terminou na ferramenta, sem nova chamada ao modelo).
    /// </summary>
    public static async Task<string?> PrepararAsync(
        this AmbienteAgente ambiente, CenarioAgente c, string argumentos, string telefone = Tel, string mensagem = "quero agendar")
    {
        var antes = ambiente.Provedor.Chamadas;
        ambiente.Provedor.Roteiro = (_, n) => n == antes + 1 ? ProvedorIaRoteirizado.Chama(("preparar_agendamento", argumentos)) : ProvedorIaRoteirizado.Texto("ok");
        await ambiente.EnviarAsync(c, mensagem, telefone);

        return ambiente.Provedor.Chamadas == antes + 1 ? null : Erro(ambiente.Provedor.Requisicoes[antes + 1]);
    }

    /// <summary>Roda a rodada da conversa deste telefone (para ambientes com a fila desligada, onde o webhook só grava).</summary>
    public static async Task ProcessarConversaAsync(this AmbienteAgente ambiente, string telefone = Tel)
    {
        var digitos = new string(telefone.Where(char.IsAsciiDigit).ToArray());
        var conversa = (await ambiente.ConversasAsync()).Single(x => x.TelefoneCliente.EndsWith(digitos, StringComparison.Ordinal));
        using var escopo = ambiente.Fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<IProcessadorRodadaAgente>().ProcessarAsync(conversa.Id, CancellationToken.None);
    }

    /// <summary>Cliente HTTP autenticado como Administrador do negócio do cenário (o painel enxerga o que o agente agendou).</summary>
    public static async Task<HttpClient> AdminAsync(this AmbienteAgente ambiente, CenarioAgente c)
    {
        var email = $"admin-{Guid.NewGuid():N}@teste.com";
        using (var escopo = ambiente.Fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            var hasher = escopo.ServiceProvider.GetRequiredService<ISenhaHasher>();
            db.Usuarios.Add(Usuario.Criar(c.NegocioId, "Admin", email, Perfil.Administrador, hasher.Hash(SenhaAdmin)));
            await db.SaveChangesAsync();
        }

        using var anonimo = ambiente.Fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, SenhaAdmin));
        login.EnsureSuccessStatusCode();
        var corpo = await login.Content.ReadFromJsonAsync<RespostaLogin>();
        var cliente = ambiente.Fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", corpo!.AccessToken);
        return cliente;
    }

    private const string SenhaAdmin = "SenhaForte!123";

    public static Task DefinirEmailDoProfissionalAsync(this AmbienteAgente ambiente, Guid profissionalId, string email) =>
        ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE profissionais SET email = {email} WHERE id = {profissionalId}");
            return 0;
        });

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
