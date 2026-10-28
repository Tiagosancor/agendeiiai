using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Plataforma.Infraestrutura.Persistencia;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Fase 4, fatia 2 (G3 e G11): anonimizar o cliente apaga as mensagens das conversas dele e anonimiza a linha (usos_ia e rodadas ficam),
/// exportação traz as conversas, "apagar conversa" sem rota, retenção por job (padrão desligado) e máscara do link de gestão no webhook.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class LgpdConversasAgenteTestes : IAsyncLifetime
{
    private const string TelefoneMais9 = "+5571988887777";
    private const string TelefoneSem9 = "5571988887777"; // o webhook entrega sem "+"
    private const string OutroTelefone = "5571977776666";

    private readonly PostgresContainerFixture _postgres;
    private readonly GanchoAposLerConversa _gancho = new();
    private AmbienteAgente _ambiente = null!;

    public LgpdConversasAgenteTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => IniciarAsync();

    public async Task DisposeAsync() => await _ambiente.DisposeAsync();

    private async Task IniciarAsync(Dictionary<string, string?>? extra = null)
    {
        if (_ambiente is not null)
            await _ambiente.DisposeAsync();

        _ambiente = new AmbienteAgente(_postgres);
        var config = new Dictionary<string, string?>
        {
            ["Ai:LimiteMensagensPorTelefonePorHora"] = "1000",
            ["Ai:LimiteMensagensPorTelefonePorDia"] = "5000",
        };
        foreach (var (chave, valor) in extra ?? new Dictionary<string, string?>())
            config[chave] = valor;
        await _ambiente.IniciarAsync(config, ajustarServicos: servicos =>
            servicos.ConfigureDbContext<PlataformaDbContext>(o => o.AddInterceptors(_gancho)));
    }

    private IServiceScope Escopo(Guid negocioId)
    {
        var escopo = _ambiente.Fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(negocioId);
        return escopo;
    }

    private Task<Guid> CriarClienteAsync(Guid negocioId, string nome, string telefone) => _ambiente.NoBancoAsync(async db =>
    {
        var cliente = Cliente.Criar(negocioId, nome, TelefoneE164.Criar(telefone));
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return cliente.Id;
    });

    private async Task<ConversaWhatsApp> ConversaDoAsync(string telefoneSemMais) =>
        (await _ambiente.ConversasAsync()).Single(c => c.TelefoneCliente == "+" + telefoneSemMais);

    // ---------------------------------------------------------------- G11

    [Fact]
    public async Task Anonimizar_o_cliente_apaga_as_mensagens_das_conversas_dele_e_mantem_usos_e_a_conversa_de_outro()
    {
        var c = await _ambiente.SemearAsync();
        (await _ambiente.EnviarAsync(c, "Oi, quero cortar o cabelo", TelefoneSem9)).EnsureSuccessStatusCode();
        (await _ambiente.EnviarAsync(c, "Bom dia", OutroTelefone)).EnsureSuccessStatusCode();

        var conversa = await ConversaDoAsync(TelefoneSem9);
        var conversaOutra = await ConversaDoAsync(OutroTelefone);
        var clienteId = await CriarClienteAsync(c.NegocioId, "Maria da Silva", TelefoneMais9);
        var mensagens = await _ambiente.MensagensAsync();
        mensagens.Where(m => m.ConversaId == conversa.Id).Should().NotBeEmpty();
        var mensagensDoOutro = mensagens.Count(m => m.ConversaId == conversaOutra.Id);
        var usosAntes = await _ambiente.UsosAsync();
        usosAntes.Where(u => u.ConversaId == conversa.Id).Should().NotBeEmpty("a IA respondeu, então há uso registrado");

        using (var escopo = Escopo(c.NegocioId))
            (await escopo.ServiceProvider.GetRequiredService<IGerenciadorClientes>().ExcluirAsync(clienteId)).Should().BeTrue();

        var depois = await _ambiente.MensagensAsync();
        depois.Where(m => m.ConversaId == conversa.Id).Should().BeEmpty("G11: nenhuma mensagem da conversa do cliente sobra");
        depois.Count(m => m.ConversaId == conversaOutra.Id).Should().Be(mensagensDoOutro, "a conversa de outro número não é tocada");

        var anonimizada = (await _ambiente.ConversasAsync()).Single(x => x.Id == conversa.Id);
        anonimizada.TelefoneCliente.Should().StartWith(ConversaWhatsApp.PrefixoTelefoneAnonimizado).And.NotContain("7198888");
        anonimizada.TelefoneCliente.Length.Should().BeLessThanOrEqualTo(20);
        anonimizada.ClienteId.Should().BeNull();
        anonimizada.Resumo.Should().BeNull();
        anonimizada.Status.Should().Be(StatusConversaWhatsApp.Agente);
        anonimizada.ProcessandoAte.Should().BeNull();

        (await ConversaDoAsync(OutroTelefone)).Id.Should().Be(conversaOutra.Id);
        (await _ambiente.UsosAsync()).Should().HaveCount(usosAntes.Count, "usos_ia é base da cota e do diagnóstico");

        // O mesmo número escrevendo depois abre uma conversa NOVA.
        (await _ambiente.EnviarAsync(c, "Voltei", TelefoneSem9)).EnsureSuccessStatusCode();
        var nova = await ConversaDoAsync(TelefoneSem9);
        nova.Id.Should().NotBe(conversa.Id);
    }

    [Fact]
    public async Task Acha_a_conversa_pelo_telefone_nas_duas_formas_do_nono_digito_mesmo_sem_vinculo_de_cliente()
    {
        var c = await _ambiente.SemearAsync();
        const string semNove = "557188887777";
        (await _ambiente.EnviarAsync(c, "Oi", semNove)).EnsureSuccessStatusCode();
        var conversa = await ConversaDoAsync(semNove);
        conversa.ClienteId.Should().BeNull();
        var clienteId = await CriarClienteAsync(c.NegocioId, "Maria", TelefoneMais9);

        using (var escopo = Escopo(c.NegocioId))
            (await escopo.ServiceProvider.GetRequiredService<IGerenciadorClientes>().ExcluirAsync(clienteId)).Should().BeTrue();

        (await _ambiente.MensagensAsync()).Where(m => m.ConversaId == conversa.Id).Should().BeEmpty();
        (await _ambiente.ConversasAsync()).Single(x => x.Id == conversa.Id).TelefoneCliente
            .Should().StartWith(ConversaWhatsApp.PrefixoTelefoneAnonimizado);
    }

    [Fact]
    public async Task Conversa_de_outro_negocio_com_o_mesmo_numero_nao_e_tocada()
    {
        var a = await _ambiente.SemearAsync();
        var b = await _ambiente.SemearAsync();
        (await _ambiente.EnviarAsync(a, "Oi", TelefoneSem9)).EnsureSuccessStatusCode();
        (await _ambiente.EnviarAsync(b, "Oi também", TelefoneSem9)).EnsureSuccessStatusCode();
        var clienteA = await CriarClienteAsync(a.NegocioId, "Maria", TelefoneMais9);

        using (var escopo = Escopo(a.NegocioId))
            await escopo.ServiceProvider.GetRequiredService<IGerenciadorClientes>().ExcluirAsync(clienteA);

        var conversas = await _ambiente.ConversasAsync();
        var deB = conversas.Single(x => x.NegocioId == b.NegocioId);
        deB.TelefoneCliente.Should().Be("+" + TelefoneSem9);
        (await _ambiente.MensagensAsync()).Where(m => m.ConversaId == deB.Id).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Exportacao_traz_as_conversas_sem_ferramenta_nem_nota_de_contexto()
    {
        var c = await _ambiente.SemearAsync();
        (await _ambiente.EnviarAsync(c, "Oi, tem horário amanhã?", TelefoneSem9)).EnsureSuccessStatusCode();
        var conversa = await ConversaDoAsync(TelefoneSem9);
        await _ambiente.NoBancoAsync(async db =>
        {
            db.MensagensWhatsApp.AddRange(
                new MensagemWhatsApp(c.NegocioId, conversa.InstanciaId, conversa.Id, null, PapelMensagemWhatsApp.Ferramenta, "{\"data\":\"2026-01-01\"}", "consultar_horarios"),
                new MensagemWhatsApp(c.NegocioId, conversa.InstanciaId, conversa.Id, null, PapelMensagemWhatsApp.Sistema, "NOTA-INTERNA", MarcadoresAgente.ContextoConsulta));
            await db.SaveChangesAsync();
            return 0;
        });
        var clienteId = await CriarClienteAsync(c.NegocioId, "Maria", TelefoneMais9);

        ExportacaoCliente? exportacao;
        using (var escopo = Escopo(c.NegocioId))
            exportacao = await escopo.ServiceProvider.GetRequiredService<IGerenciadorClientes>().ExportarAsync(clienteId);

        exportacao.Should().NotBeNull();
        var conversas = exportacao!.Conversas!;
        conversas.Should().ContainSingle();
        var textos = conversas[0].Mensagens;
        textos.Should().Contain(m => m.Papel == "cliente" && m.Texto == "Oi, tem horário amanhã?");
        textos.Should().Contain(m => m.Papel == "assistente");
        textos.Select(m => m.Texto).Should().NotContain(["NOTA-INTERNA", "{\"data\":\"2026-01-01\"}"]);
        textos.Select(m => m.Papel).Distinct().Should().BeSubsetOf(["cliente", "assistente", "sistema"]);
    }

    [Fact]
    public async Task ApagarConversa_apaga_as_mensagens_anonimiza_a_linha_e_nao_enxerga_outro_negocio()
    {
        var a = await _ambiente.SemearAsync();
        var b = await _ambiente.SemearAsync();
        (await _ambiente.EnviarAsync(a, "Oi", TelefoneSem9)).EnsureSuccessStatusCode();
        (await _ambiente.EnviarAsync(b, "Oi", TelefoneSem9)).EnsureSuccessStatusCode();
        var conversas = await _ambiente.ConversasAsync();
        var deA = conversas.Single(x => x.NegocioId == a.NegocioId);
        var deB = conversas.Single(x => x.NegocioId == b.NegocioId);
        var usosAntes = (await _ambiente.UsosAsync()).Count;

        using (var escopo = Escopo(a.NegocioId))
        {
            var servico = escopo.ServiceProvider.GetRequiredService<IServicoLgpdConversasWhatsApp>();
            (await servico.ApagarConversaAsync(deB.Id)).Should().BeFalse("a conversa é de outro negócio");
            (await servico.ApagarConversaAsync(Guid.NewGuid())).Should().BeFalse();
            (await servico.ApagarConversaAsync(deA.Id)).Should().BeTrue();
        }

        var mensagens = await _ambiente.MensagensAsync();
        mensagens.Where(m => m.ConversaId == deA.Id).Should().BeEmpty();
        mensagens.Where(m => m.ConversaId == deB.Id).Should().NotBeEmpty();
        (await _ambiente.ConversasAsync()).Single(x => x.Id == deA.Id).TelefoneCliente.Should().StartWith(ConversaWhatsApp.PrefixoTelefoneAnonimizado);
        (await _ambiente.UsosAsync()).Should().HaveCount(usosAntes);
    }

    // ---------------------------------------------------------------- retenção

    private async Task EnvelhecerAsync(Guid conversaId, int diasAtras, bool soAMaisAntiga)
    {
        await _ambiente.NoBancoAsync(async db =>
        {
            var antes = DateTimeOffset.UtcNow.AddDays(-diasAtras);
            var ids = await db.MensagensWhatsApp.IgnoreQueryFilters().Where(m => m.ConversaId == conversaId)
                .OrderBy(m => m.CriadoEm).Select(m => m.Id).ToListAsync();
            var alvo = soAMaisAntiga ? ids.Take(1).ToList() : ids;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE mensagens_whatsapp SET criado_em = {antes} WHERE id = ANY({alvo.ToArray()})");
            return 0;
        });
    }

    [Fact]
    public async Task Retencao_desligada_por_padrao_nao_apaga_nada()
    {
        var c = await _ambiente.SemearAsync();
        (await _ambiente.EnviarAsync(c, "Oi", TelefoneSem9)).EnsureSuccessStatusCode();
        var conversa = await ConversaDoAsync(TelefoneSem9);
        await EnvelhecerAsync(conversa.Id, 1000, soAMaisAntiga: false);
        var total = (await _ambiente.MensagensAsync()).Count;

        using var escopo = _ambiente.Fabrica.Services.CreateScope();
        (await escopo.ServiceProvider.GetRequiredService<JobLimparConversasWhatsApp>().ExecutarAsync()).Should().Be(0);

        (await _ambiente.MensagensAsync()).Should().HaveCount(total);
    }

    [Fact]
    public async Task Retencao_ligada_apaga_so_as_mensagens_mais_velhas_que_o_prazo_e_preserva_conversa_e_usos()
    {
        await IniciarAsync(new() { ["Ai:RetencaoMensagensDias"] = "30" });
        var c = await _ambiente.SemearAsync();
        var outro = await _ambiente.SemearAsync();
        (await _ambiente.EnviarAsync(c, "Oi", TelefoneSem9)).EnsureSuccessStatusCode();
        (await _ambiente.EnviarAsync(outro, "Oi", TelefoneSem9)).EnsureSuccessStatusCode();
        var conversa = (await _ambiente.ConversasAsync()).Single(x => x.NegocioId == c.NegocioId);
        var conversaOutro = (await _ambiente.ConversasAsync()).Single(x => x.NegocioId == outro.NegocioId);
        var antes = await _ambiente.MensagensAsync();
        var recentesDeC = antes.Count(m => m.ConversaId == conversa.Id) - 1;
        recentesDeC.Should().BeGreaterThan(0);
        var usosAntes = (await _ambiente.UsosAsync()).Count;

        await EnvelhecerAsync(conversa.Id, 31, soAMaisAntiga: true); // a primeira mensagem passa do prazo
        await EnvelhecerAsync(conversaOutro.Id, 400, soAMaisAntiga: false); // outro negócio: tudo velho

        using (var escopo = _ambiente.Fabrica.Services.CreateScope())
            (await escopo.ServiceProvider.GetRequiredService<JobLimparConversasWhatsApp>().ExecutarAsync())
                .Should().Be(1 + antes.Count(m => m.ConversaId == conversaOutro.Id));

        var depois = await _ambiente.MensagensAsync();
        depois.Count(m => m.ConversaId == conversa.Id).Should().Be(recentesDeC);
        depois.Where(m => m.ConversaId == conversaOutro.Id).Should().BeEmpty();
        (await _ambiente.ConversasAsync()).Should().HaveCount(2);
        (await _ambiente.UsosAsync()).Should().HaveCount(usosAntes);
    }

    // ---------------------------------------------------------------- protocolo de trava (mensagens não reaparecem)

    /// <summary>Roda uma ação (uma vez) logo depois de o webhook LER a conversa pelo telefone — antes de gravar a mensagem.</summary>
    private sealed class GanchoAposLerConversa : DbCommandInterceptor
    {
        private Func<Task>? _acao;

        public bool Disparou { get; private set; }

        public void Armar(Func<Task> acao) => (_acao, Disparou) = (acao, false);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (_acao is not null && command.CommandText.Contains("FROM conversas_whatsapp") && command.CommandText.Contains("telefone_cliente")
                && command.CommandText.Contains("instancia_id"))
            {
                var acao = _acao;
                _acao = null;
                Disparou = true;
                await acao();
            }

            return result;
        }
    }

    private async Task ExcluirClienteAsync(Guid negocioId, Guid clienteId)
    {
        using var escopo = Escopo(negocioId);
        (await escopo.ServiceProvider.GetRequiredService<IGerenciadorClientes>().ExcluirAsync(clienteId)).Should().BeTrue();
    }

    [Fact]
    public async Task Webhook_que_leu_a_conversa_antes_da_anonimizacao_nao_grava_nela_e_abre_uma_conversa_nova()
    {
        var c = await _ambiente.SemearAsync();
        (await _ambiente.EnviarAsync(c, "primeira", TelefoneSem9)).EnsureSuccessStatusCode();
        var velha = await ConversaDoAsync(TelefoneSem9);
        var clienteId = await CriarClienteAsync(c.NegocioId, "Maria", TelefoneMais9);

        // Leitura (conversa velha) → exclusão por inteiro → gravação.
        _gancho.Armar(() => ExcluirClienteAsync(c.NegocioId, clienteId));
        (await _ambiente.EnviarAsync(c, "segunda", TelefoneSem9)).EnsureSuccessStatusCode();

        _gancho.Disparou.Should().BeTrue();
        var mensagens = await _ambiente.MensagensAsync();
        mensagens.Where(m => m.ConversaId == velha.Id).Should().BeEmpty("nada reaparece na conversa anonimizada");
        var nova = (await _ambiente.ConversasAsync()).Single(x => x.TelefoneCliente == "+" + TelefoneSem9);
        nova.Id.Should().NotBe(velha.Id);
        mensagens.Should().Contain(m => m.ConversaId == nova.Id && m.Conteudo == "segunda");
        (await _ambiente.ConversasAsync()).Single(x => x.Id == velha.Id).TelefoneCliente.Should().StartWith("anonimizado:");
    }

    [Fact]
    public async Task Rodada_em_andamento_nao_envia_nem_grava_mensagem_depois_da_anonimizacao()
    {
        var c = await _ambiente.SemearAsync();
        var clienteId = await CriarClienteAsync(c.NegocioId, "Maria", TelefoneMais9);
        _ambiente.Provedor.Roteiro = (_, n) =>
        {
            if (n == 1)
                return ProvedorIaRoteirizado.Chama(("listar_servicos", "{}"));
            // Segunda chamada ao modelo: o dono apaga os dados no meio da rodada.
            ExcluirClienteAsync(c.NegocioId, clienteId).GetAwaiter().GetResult();
            return ProvedorIaRoteirizado.Texto("Resposta que nunca deve sair");
        };

        (await _ambiente.EnviarAsync(c, "quais serviços?", TelefoneSem9)).EnsureSuccessStatusCode();

        var conversa = (await _ambiente.ConversasAsync()).Single();
        conversa.TelefoneCliente.Should().StartWith("anonimizado:");
        (await _ambiente.MensagensAsync()).Where(m => m.ConversaId == conversa.Id).Should().BeEmpty(
            "nem a auditoria de ferramenta, nem a resposta, nem a mensagem do cliente reaparecem");
        _ambiente.Evolution.Envios.Should().BeEmpty("a conferência antes do envio vê a conversa anonimizada");
    }

    [Fact]
    public async Task Envio_ja_iniciado_no_provedor_nao_e_cancelado_mas_nada_e_gravado_depois()
    {
        var c = await _ambiente.SemearAsync();
        var clienteId = await CriarClienteAsync(c.NegocioId, "Maria", TelefoneMais9);
        var noEnvio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ambiente.Evolution.AoEnviar = async (_, _, _) =>
        {
            noEnvio.TrySetResult();
            await liberar.Task;
        };

        var webhook = _ambiente.EnviarAsync(c, "Oi", TelefoneSem9);
        await noEnvio.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // O envio já passou da última conferência e está no provedor: a anonimização termina mesmo assim.
        await ExcluirClienteAsync(c.NegocioId, clienteId);
        liberar.SetResult();
        (await webhook).EnsureSuccessStatusCode();

        _ambiente.Evolution.Envios.Should().ContainSingle("o texto já entregue ao provedor não é recuperável (limite documentado)");
        var conversa = (await _ambiente.ConversasAsync()).Single();
        conversa.TelefoneCliente.Should().StartWith("anonimizado:");
        (await _ambiente.MensagensAsync()).Where(m => m.ConversaId == conversa.Id).Should().BeEmpty(
            "depois da anonimização a rodada não grava a resposta enviada nem o resto");
    }

    [Fact]
    public async Task Job_apaga_mensagens_que_sobrarem_em_conversa_anonimizada_mesmo_com_retencao_desligada()
    {
        var c = await _ambiente.SemearAsync();
        (await _ambiente.EnviarAsync(c, "Oi", TelefoneSem9)).EnsureSuccessStatusCode();
        var viva = await ConversaDoAsync(TelefoneSem9);
        (await _ambiente.EnviarAsync(c, "Oi", OutroTelefone)).EnsureSuccessStatusCode();
        var outra = await ConversaDoAsync(OutroTelefone);
        // Simula um escritor que escapou do protocolo: anonimiza a linha por fora e deixa as mensagens.
        await _ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE conversas_whatsapp SET telefone_cliente = 'anonimizado:abcd1234' WHERE id = {viva.Id}");
            return 0;
        });
        (await _ambiente.MensagensAsync()).Where(m => m.ConversaId == viva.Id).Should().NotBeEmpty();
        var daOutra = (await _ambiente.MensagensAsync()).Count(m => m.ConversaId == outra.Id);

        using (var escopo = _ambiente.Fabrica.Services.CreateScope())
            (await escopo.ServiceProvider.GetRequiredService<JobLimparConversasWhatsApp>().ExecutarAsync()).Should().BeGreaterThan(0);

        var depois = await _ambiente.MensagensAsync();
        depois.Where(m => m.ConversaId == viva.Id).Should().BeEmpty();
        depois.Count(m => m.ConversaId == outra.Id).Should().Be(daOutra);
    }

    // ---------------------------------------------------------------- G3

    [Fact]
    public async Task Link_de_gestao_e_jwt_colados_pelo_cliente_nao_sao_gravados()
    {
        var c = await _ambiente.SemearAsync();
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.assinaturaSECRETA123";
        (await _ambiente.EnviarAsync(c, $"cancele este https://acme.agendeiiai.com.br/agendamentos/{jwt} por favor", TelefoneSem9)).EnsureSuccessStatusCode();
        (await _ambiente.EnviarAsync(c, $"meu token {jwt}", OutroTelefone)).EnsureSuccessStatusCode();

        var mensagens = await _ambiente.MensagensAsync();
        mensagens.Where(m => m.Papel == PapelMensagemWhatsApp.Usuario).Should().HaveCount(2);
        foreach (var m in mensagens)
        {
            m.Conteudo.Should().NotContain("assinaturaSECRETA").And.NotContain("eyJ").And.NotContain("/agendamentos/");
        }

        mensagens.Single(m => m.Papel == PapelMensagemWhatsApp.Usuario && m.Conteudo.StartsWith("cancele")).Conteudo
            .Should().Be($"cancele este {MascaraLinkGestao.Marca} por favor");
        _ambiente.Provedor.Requisicoes.Should().NotBeEmpty();
        Serializado().Should().NotContain("assinaturaSECRETA");
    }

    private string Serializado() => string.Join("\n", _ambiente.Provedor.Requisicoes.Select(ProvedorIaRoteirizado.Serializar));
}
