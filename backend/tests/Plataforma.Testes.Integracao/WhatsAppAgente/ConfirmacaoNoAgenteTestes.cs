using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;
using static Plataforma.Testes.Integracao.WhatsAppAgente.HelpersAgendamentoAgente;
using static Plataforma.Testes.Integracao.WhatsAppAgente.ProvedorIaRoteirizado;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Fase 3, confirmação: o "sim" do cliente é avaliado pelo SERVIDOR, sem chamar a IA, e só então o agendamento vira
/// <c>Agendado</c>. Texto de confirmação com link de gestão que nunca é persistido nem vai à IA, prazo, "não", lote misto,
/// idempotência, mudanças entre o preparo e o "sim", histórico e aviso de atendimento automatizado.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ConfirmacaoNoAgenteTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private AmbienteAgente _ambiente = null!;

    public ConfirmacaoNoAgenteTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => IniciarAsync();

    public async Task DisposeAsync() => await _ambiente.DisposeAsync();

    private async Task IniciarAsync(Dictionary<string, string?>? extra = null, bool logs = false, Type? fila = null)
    {
        if (_ambiente is not null)
            await _ambiente.DisposeAsync();

        _ambiente = new AmbienteAgente(_postgres);
        var config = new Dictionary<string, string?>
        {
            ["Ai:Agendamento:Modo"] = "Todos",
            ["Ai:LimiteMensagensPorTelefonePorHora"] = "1000",
            ["Ai:LimiteMensagensPorTelefonePorDia"] = "5000",
            ["Ai:MaxChamadasFerramentaPorIteracao"] = "10",
        };
        foreach (var (chave, valor) in extra ?? new Dictionary<string, string?>())
            config[chave] = valor;
        await _ambiente.IniciarAsync(config, capturarLogs: logs, fila: fila);
    }

    private EspiaEmail Email() => _ambiente.Fabrica.Services.GetRequiredService<EspiaEmail>();

    private string? ConfirmacaoEntregue() => _ambiente.Evolution.Envios.Select(e => e.Texto).LastOrDefault(t => t.StartsWith("Agendamento confirmado"));

    private async Task<Guid> IdDaMensagemDoClienteAsync(string conteudo) =>
        (await _ambiente.MensagensAsync()).Last(m => m.Papel == PapelMensagemWhatsApp.Usuario && m.Conteudo == conteudo).Id;

    /// <summary>Prepara (cliente novo com nome) e devolve a pendência.</summary>
    private async Task<PendenciaAgendamentoWhatsApp> PreparadoAsync(CenarioAgente c, string hora = "10:00", string telefone = Tel, string nome = "Maria Souza")
    {
        (await _ambiente.PrepararAsync(c, Prepara(c, c.Ana, hora, nome), telefone)).Should().BeNull();
        return (await _ambiente.PendenciasAsync()).Last(p => p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando);
    }

    // ---------------------------------------------------------------- caminho feliz

    [Fact]
    public async Task Sim_do_cliente_novo_confirma_sem_chamar_a_IA_cria_o_cliente_e_aparece_na_agenda_do_painel()
    {
        var c = await _ambiente.SemearAsync();
        await _ambiente.DefinirEmailDoProfissionalAsync(c.Ana, "ana@teste.com");
        await PreparadoAsync(c);
        var chamadasAntes = _ambiente.Provedor.Chamadas;
        var usosAntes = (await _ambiente.UsosAsync()).Count;

        await _ambiente.EnviarAsync(c, "Sim!");

        _ambiente.Provedor.Chamadas.Should().Be(chamadasAntes, "o 'sim' é avaliado pelo servidor, antes da IA");
        (await _ambiente.UsosAsync()).Should().HaveCount(usosAntes);

        var agendamento = (await _ambiente.AgendamentosAsync()).Single();
        agendamento.Status.Should().Be(StatusAgendamento.Agendado);
        agendamento.Origem.Should().Be(OrigemAgendamento.WhatsApp);
        agendamento.ClienteId.Should().NotBeNull();
        agendamento.ConsentimentoData.Should().NotBeNull();
        agendamento.ConsentimentoVersaoTermos.Should().Be("2.1");
        agendamento.ConsentimentoIp.Should().BeNull();

        var cliente = (await _ambiente.ClientesAsync()).Single();
        cliente.Id.Should().Be(agendamento.ClienteId!.Value);
        cliente.Nome.Should().Be("Maria Souza");
        cliente.Origem.Should().Be(OrigemCliente.WhatsApp);
        cliente.Telefone!.Valor.Should().Be(TelComMais);

        var pendencia = (await _ambiente.PendenciasAsync()).Single();
        pendencia.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Confirmada);
        pendencia.ConfirmacaoMensagemId.Should().Be((Guid?)await IdDaMensagemDoClienteAsync("Sim!"));
        pendencia.NomeInformado.Should().BeNull();
        pendencia.VersaoTermos.Should().Be("2.1");

        // Texto fixo do servidor com o link de gestão (token válido para ESTE agendamento), na própria conversa.
        var confirmacao = ConfirmacaoEntregue()!;
        confirmacao.Should().Contain("Agendamento confirmado em Barbearia do Teste: Corte com Ana").And.Contain("às 10:00").And.Contain("/agendamentos/");
        var token = confirmacao[(confirmacao.IndexOf("/agendamentos/", StringComparison.Ordinal) + "/agendamentos/".Length)..].Split(' ', '\n')[0];
        var tokens = _ambiente.Fabrica.Services.GetRequiredService<IServicoTokenPublico>();
        (await tokens.ValidarTokenAgendamentoAsync(token, c.NegocioId)).Should().Be(agendamento.Id);

        // O que fica no banco (e vai à IA no histórico) nunca leva o token.
        var gravada = (await _ambiente.MarcadoresAsync(MarcadoresAgente.Confirmado)).Single();
        gravada.Conteudo.Should().Contain(TextosAgendamentoAgente.LinkDeGestaoOculto).And.NotContain(token).And.NotContain("/agendamentos/");

        // Profissional avisado por e-mail; o WhatsApp antigo da plataforma não manda confirmação ao cliente.
        Email().Enviados.Should().ContainSingle(e => e.Destinatario == "ana@teste.com");
        _ambiente.Fabrica.Services.GetRequiredService<EspiaWhatsApp>().Enviados.Should().BeEmpty();

        var auditoria = await _ambiente.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().AsNoTracking().Where(l => l.Acao == "AgendarPeloWhatsApp").ToListAsync());
        auditoria.Should().ContainSingle().Which.Detalhes.Should().Contain(pendencia.Id.ToString()).And.NotContain("Maria").And.NotContain("5571988887777");

        // Aparece na agenda do painel (lista do dia e grade).
        using var admin = await _ambiente.AdminAsync(c);
        var dia = Dia().ToString("yyyy-MM-dd");
        var lista = (await admin.GetFromJsonAsync<List<AgendamentoResumo>>($"/painel/agenda?profissionalId={c.Ana}&data={dia}"))!;
        lista.Should().ContainSingle(a => a.Id == agendamento.Id && a.Status == "Agendado" && a.ClienteNome == "Maria Souza" && a.Servicos.Contains("Corte"));
        var grade = (await admin.GetFromJsonAsync<GradeAgenda>($"/painel/agenda/grade?data={dia}"))!;
        grade.Profissionais.Single(p => p.ProfissionalId == c.Ana).Celulas.Single(x => x.Hora == "10:00").Estado.Should().Be(EstadoCelulaGrade.Ocupado);
    }

    [Fact]
    public async Task Sim_do_cliente_existente_usa_o_cadastro_sem_alterar_nada_e_sem_duplicar()
    {
        var c = await _ambiente.SemearAsync();
        await _ambiente.NoBancoAsync(async db =>
        {
            db.Clientes.Add(Cliente.Criar(c.NegocioId, "Maria Cadastro", TelefoneE164.Criar(TelComMais), email: "maria.cadastro@exemplo.com"));
            return await db.SaveChangesAsync();
        });
        (await _ambiente.PrepararAsync(c, Prepara(c, c.Ana, "10:00"))).Should().BeNull();

        await _ambiente.EnviarAsync(c, "sim");

        var clientes = await _ambiente.ClientesAsync();
        clientes.Should().ContainSingle().Which.Nome.Should().Be("Maria Cadastro");
        var agendamento = (await _ambiente.AgendamentosAsync()).Single();
        agendamento.Status.Should().Be(StatusAgendamento.Agendado);
        agendamento.ClienteId.Should().Be(clientes[0].Id);
        agendamento.NomeInformado.Should().BeNull();

        // O nome do cadastro nunca aparece no texto do servidor nem foi à IA em nenhuma rodada.
        ConfirmacaoEntregue().Should().NotContain("Maria Cadastro");
        foreach (var requisicao in _ambiente.Provedor.Requisicoes)
            Serializar(requisicao).Should().NotContain("Maria Cadastro").And.NotContain("maria.cadastro");
    }

    [Theory]
    [InlineData("sim")]
    [InlineData("  Sim  ")]
    [InlineData("confirmo")]
    [InlineData("👍")]
    [InlineData("✅")]
    [InlineData("pode confirmar")]
    public async Task Afirmativas_da_lista_fechada_confirmam(string resposta)
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);

        await _ambiente.EnviarAsync(c, resposta);

        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Agendado);
    }

    // ---------------------------------------------------------------- o que NÃO confirma

    [Theory]
    [InlineData("sim, mas às 15h")]
    [InlineData("simulação")]
    [InlineData("quero mudar o horário")]
    [InlineData("pode marcar sim, obrigado pela atenção de hoje")]
    public async Task Resposta_fora_da_lista_vai_para_a_IA_e_a_pendencia_segue_aguardando(string resposta)
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);
        var antes = _ambiente.Provedor.Chamadas;
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Claro! Qual horário você prefere?");

        await _ambiente.EnviarAsync(c, resposta);

        _ambiente.Provedor.Chamadas.Should().Be(antes + 1);
        _ambiente.UltimaEntrega().Should().Be("Claro! Qual horário você prefere?");
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Reservado);
        (await _ambiente.PendenciasAsync()).Single().Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);
    }

    [Fact]
    public async Task Rajada_sim_mais_mudanca_no_mesmo_lote_nao_confirma()
    {
        await IniciarAsync(fila: typeof(FilaQuePerde));
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, n) => n == 1 ? Chama(("preparar_agendamento", Prepara(c, c.Ana, "10:00", "Maria Souza"))) : Texto("Troco para as 15h?");
        await _ambiente.EnviarAsync(c, "quero corte segunda às 10h");
        await _ambiente.ProcessarConversaAsync();
        (await _ambiente.PendenciasAsync()).Single().Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);

        await _ambiente.EnviarAsync(c, "sim");
        await _ambiente.EnviarAsync(c, "na verdade troca pra 15h");
        await _ambiente.ProcessarConversaAsync();

        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Reservado);
        _ambiente.Provedor.Chamadas.Should().Be(2, "o lote misto vai para a IA");
    }

    [Fact]
    public async Task Sim_depois_de_contestacao_ja_processada_em_outra_rodada_nao_confirma()
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Quer trocar para as 15h? Me diga e eu refaço o resumo.");

        // A contestação é respondida pela IA (sem substituir a pendência) e o lote fica processado.
        await _ambiente.EnviarAsync(c, "troca para 15h");
        var antes = _ambiente.Provedor.Chamadas;
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Antes de agendar, preciso que você confirme o novo horário.");

        // O "sim" seguinte, em rodada própria, não pode confirmar as 10h ignorando a contestação.
        await _ambiente.EnviarAsync(c, "sim");

        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Reservado);
        (await _ambiente.PendenciasAsync()).Single().Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);
        _ambiente.Provedor.Chamadas.Should().Be(antes + 1, "o 'sim' após contestação vai para a IA, nunca confirma");
        ConfirmacaoEntregue().Should().BeNull();
    }

    [Fact]
    public async Task Sim_que_chegou_antes_do_resumo_nao_confirma()
    {
        var c = await _ambiente.SemearAsync();
        var enviouSim = false;
        _ambiente.Provedor.Roteiro = (_, n) =>
        {
            if (n == 1 && !enviouSim)
            {
                enviouSim = true;
                // O "sim" chega enquanto o modelo ainda trabalha: antes de qualquer resumo existir.
                _ambiente.EnviarAsync(c, "sim").GetAwaiter().GetResult();
                return Chama(("preparar_agendamento", Prepara(c, c.Ana, "10:00", "Maria Souza")));
            }

            return Texto("Me confirma o horário?");
        };

        await _ambiente.EnviarAsync(c, "quero corte segunda às 10h");

        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Reservado);
        (await _ambiente.PendenciasAsync()).Single().Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);
    }

    [Fact]
    public async Task Sim_sem_nenhum_resumo_vai_para_a_IA_e_nao_cria_nada()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Ainda não há nada para confirmar. Qual serviço você quer?");

        await _ambiente.EnviarAsync(c, "sim");

        _ambiente.Provedor.Chamadas.Should().Be(1);
        (await _ambiente.AgendamentosAsync()).Should().BeEmpty();
        (await _ambiente.PendenciasAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Sim_na_conversa_de_outro_telefone_nao_confirma_a_pendencia_alheia()
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c, "10:00", "5571911110001");
        await PreparadoAsync(c, "14:00", "5571911110002");

        await _ambiente.EnviarAsync(c, "sim", "5571911110002");

        var agendamentos = await _ambiente.AgendamentosAsync();
        agendamentos.Single(a => a.Inicio == H(14, 0)).Status.Should().Be(StatusAgendamento.Agendado);
        agendamentos.Single(a => a.Inicio == H(10, 0)).Status.Should().Be(StatusAgendamento.Reservado);
    }

    // ---------------------------------------------------------------- prazo e "não"

    [Fact]
    public async Task Sim_depois_do_prazo_responde_texto_fixo_libera_o_horario_e_nao_cria_nada()
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);
        await _ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE pendencias_agendamento_whatsapp SET expira_em = now() - interval '1 minute'; UPDATE agendamentos SET reservado_ate = now() - interval '1 minute'");
            return 0;
        });
        var antes = _ambiente.Provedor.Chamadas;

        await _ambiente.EnviarAsync(c, "sim");

        _ambiente.Provedor.Chamadas.Should().Be(antes);
        _ambiente.UltimaEntrega().Should().Be(TextosAgendamentoAgente.PrazoPassou);
        (await _ambiente.PendenciasAsync()).Single().Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Expirada);
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Expirado);
        (await _ambiente.ClientesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Sim_depois_que_o_job_ja_expirou_a_pendencia_avisa_o_prazo_uma_vez_so()
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);
        await _ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE pendencias_agendamento_whatsapp SET status = 'Expirada', concluida_em = now(), nome_informado = NULL; UPDATE agendamentos SET status = 'Expirado'");
            return 0;
        });
        var antes = _ambiente.Provedor.Chamadas;

        await _ambiente.EnviarAsync(c, "sim");
        _ambiente.UltimaEntrega().Should().Be(TextosAgendamentoAgente.PrazoPassou);
        _ambiente.Provedor.Chamadas.Should().Be(antes);

        _ambiente.Provedor.Roteiro = (_, _) => Texto("Quer que eu veja outro horário?");
        await _ambiente.EnviarAsync(c, "sim");
        _ambiente.Provedor.Chamadas.Should().Be(antes + 1, "o segundo 'sim' já não recebe o aviso e vai para a IA");
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Expirado);
    }

    [Fact]
    public async Task Nao_recusa_libera_o_horario_e_responde_o_texto_fixo_sem_IA()
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);
        var antes = _ambiente.Provedor.Chamadas;

        await _ambiente.EnviarAsync(c, "Não");

        _ambiente.Provedor.Chamadas.Should().Be(antes);
        _ambiente.UltimaEntrega().Should().Be(TextosAgendamentoAgente.AgendamentoNaoConfirmado);
        (await _ambiente.PendenciasAsync()).Single().Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Recusada);
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Expirado);

        // O horário voltou à oferta.
        (await _ambiente.PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria Souza"), mensagem: "quero de novo")).Should().BeNull();
    }

    // ---------------------------------------------------------------- idempotência

    [Fact]
    public async Task Webhook_repetido_do_sim_gera_um_agendamento_e_uma_confirmacao()
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);

        await _ambiente.EnviarAsync(c, "sim", id: "SIM-REPETIDO");
        await _ambiente.EnviarAsync(c, "sim", id: "SIM-REPETIDO");

        (await _ambiente.AgendamentosAsync()).Should().ContainSingle().Which.Status.Should().Be(StatusAgendamento.Agendado);
        _ambiente.Evolution.Envios.Count(e => e.Texto.StartsWith("Agendamento confirmado")).Should().Be(1);
    }

    [Fact]
    public async Task Dois_sim_distintos_confirmam_uma_vez_e_o_segundo_vai_para_a_IA()
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);
        await _ambiente.EnviarAsync(c, "sim");
        var antes = _ambiente.Provedor.Chamadas;
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Seu horário já está certo.");

        await _ambiente.EnviarAsync(c, "sim");

        (await _ambiente.AgendamentosAsync()).Should().ContainSingle();
        _ambiente.Evolution.Envios.Count(e => e.Texto.StartsWith("Agendamento confirmado")).Should().Be(1);
        _ambiente.Provedor.Chamadas.Should().Be(antes + 1);
    }

    [Fact]
    public async Task Confirmacao_gravada_mas_nao_anunciada_e_anunciada_uma_vez_na_rodada_seguinte()
    {
        await IniciarAsync(fila: typeof(FilaQuePerde));
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, n) => n == 1 ? Chama(("preparar_agendamento", Prepara(c, c.Ana, "10:00", "Maria Souza"))) : Texto("ok");
        await _ambiente.EnviarAsync(c, "quero corte");
        await _ambiente.ProcessarConversaAsync();
        var pendencia = (await _ambiente.PendenciasAsync()).Single();

        // O "sim" chega e a confirmação grava, mas o processo cai antes de mandar a mensagem (simulado: confirma direto pelo serviço).
        await _ambiente.EnviarAsync(c, "sim");
        var mensagemSim = await IdDaMensagemDoClienteAsync("sim");
        using (var escopo = _ambiente.Fabrica.Services.CreateScope())
        {
            escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(c.NegocioId);
            var resultado = await escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>()
                .ConfirmarReservaAgenteAsync(new ConfirmarReservaAgente(pendencia.Id, TelComMais, null, mensagemSim));
            resultado.ConfirmadaAgora.Should().BeTrue();
        }

        await _ambiente.ProcessarConversaAsync();
        await _ambiente.ProcessarConversaAsync();

        _ambiente.Evolution.Envios.Count(e => e.Texto.StartsWith("Agendamento confirmado")).Should().Be(1);
        (await _ambiente.MarcadoresAsync(MarcadoresAgente.Confirmado)).Should().ContainSingle();
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Agendado);
    }

    private async Task<CenarioAgente> PreparadoSemFilaAsync()
    {
        await IniciarAsync(fila: typeof(FilaQuePerde));
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, n) => n == 1 ? Chama(("preparar_agendamento", Prepara(c, c.Ana, "10:00", "Maria Souza"))) : Texto("ok");
        await _ambiente.EnviarAsync(c, "quero corte");
        await _ambiente.ProcessarConversaAsync();
        return c;
    }

    [Fact]
    public async Task Recusa_definitiva_da_Evolution_nao_fica_anunciada_e_a_recuperacao_envia_a_confirmacao_uma_vez()
    {
        var c = await PreparadoSemFilaAsync();
        var chamadasIa = _ambiente.Provedor.Chamadas;

        _ambiente.Evolution.FalharEnvio = true;
        await _ambiente.EnviarAsync(c, "sim");
        await _ambiente.ProcessarConversaAsync();

        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Agendado);
        (await _ambiente.MarcadoresAsync(MarcadoresAgente.Confirmado)).Should().BeEmpty("nada foi enviado: não pode parecer anunciada");
        ConfirmacaoEntregue().Should().BeNull();

        _ambiente.Evolution.FalharEnvio = false;
        await _ambiente.ProcessarConversaAsync();
        await _ambiente.ProcessarConversaAsync();

        _ambiente.Evolution.Envios.Count(e => e.Texto.StartsWith("Agendamento confirmado")).Should().Be(1);
        (await _ambiente.MarcadoresAsync(MarcadoresAgente.Confirmado)).Should().ContainSingle();
        _ambiente.Provedor.Chamadas.Should().Be(chamadasIa, "a recuperação não chama a IA");
    }

    [Fact]
    public async Task Resultado_ambiguo_da_Evolution_nao_reenvia_a_confirmacao_nem_chama_a_IA()
    {
        var c = await PreparadoSemFilaAsync();
        var chamadasIa = _ambiente.Provedor.Chamadas;

        _ambiente.Evolution.AoEnviar = (_, texto, _) => texto.StartsWith("Agendamento confirmado")
            ? throw new FalhaEvolutionException("conexão encerrada após o envio", ambigua: true)
            : Task.CompletedTask;
        await _ambiente.EnviarAsync(c, "sim");
        await _ambiente.ProcessarConversaAsync();

        (await _ambiente.MarcadoresAsync(MarcadoresAgente.Confirmado)).Should().ContainSingle("a reserva fica: supressão do ambíguo");

        // Queda depois do envio ambíguo e antes de marcar o lote: a recuperação só dá o lote por tratado, sem reenviar nem chamar a IA.
        await _ambiente.NoBancoAsync(async db =>
            await db.ConversasWhatsApp.IgnoreQueryFilters().ExecuteUpdateAsync(x => x.SetProperty(k => k.ProcessadaAte, (DateTimeOffset?)null)));
        _ambiente.Evolution.AoEnviar = null;
        await _ambiente.ProcessarConversaAsync();
        await _ambiente.ProcessarConversaAsync();

        ConfirmacaoEntregue().Should().BeNull();
        (await _ambiente.MarcadoresAsync(MarcadoresAgente.Confirmado)).Should().ContainSingle();
        _ambiente.Provedor.Chamadas.Should().Be(chamadasIa);
        (await _ambiente.ConversasAsync()).Single().ProcessadaAte.Should().NotBeNull();
    }

    // ---------------------------------------------------------------- o que muda entre o preparo e o "sim"

    [Fact]
    public async Task Profissional_desativado_no_meio_nao_confirma_e_responde_o_texto_fixo()
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);
        await _ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE profissionais SET ativo = false WHERE id = {c.Ana}");
            return 0;
        });

        await _ambiente.EnviarAsync(c, "sim");

        _ambiente.UltimaEntrega().Should().Be(TextosAgendamentoAgente.HorarioIndisponivelNaConfirmacao);
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Expirado);
        (await _ambiente.PendenciasAsync()).Single().Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Falhou);
        (await _ambiente.ClientesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Bloqueio_criado_no_meio_nao_confirma()
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);
        await _ambiente.NoBancoAsync(async db =>
        {
            db.BloqueiosAgenda.Add(BloqueioAgenda.Criar(c.NegocioId, c.Ana, H(9, 30), H(10, 30), "médico"));
            return await db.SaveChangesAsync();
        });

        await _ambiente.EnviarAsync(c, "sim");

        _ambiente.UltimaEntrega().Should().Be(TextosAgendamentoAgente.HorarioIndisponivelNaConfirmacao);
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Expirado);
    }

    [Fact]
    public async Task Assinatura_suspensa_no_meio_deixa_o_agente_em_silencio_e_nao_agenda()
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);
        await _ambiente.NoBancoAsync(async db =>
        {
            var plano = await db.Planos.FirstAsync(p => p.Nome == "Casa Cheia");
            var assinatura = ServicoAssinatura.IniciarTeste(c.NegocioId, plano, Periodicidade.Mensal, "teste", DateTimeOffset.UtcNow.AddDays(-45));
            ServicoAssinatura.AtualizarPorTempo(assinatura, DateTimeOffset.UtcNow);
            db.Assinaturas.Add(assinatura);
            return await db.SaveChangesAsync();
        });
        var enviosAntes = _ambiente.Evolution.Envios.Count;

        await _ambiente.EnviarAsync(c, "sim");

        _ambiente.Evolution.Envios.Count.Should().Be(enviosAntes);
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Reservado);
    }

    [Fact]
    public async Task Modo_desligado_no_meio_manda_o_sim_para_a_IA_de_consulta_e_nao_agenda()
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);
        _ambiente.Fabrica.Services.GetRequiredService<IOptions<OpcoesIa>>().Value.Agendamento.Modo = ModoAgendamentoAgente.Desligado;
        var antes = _ambiente.Provedor.Chamadas;
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Posso ajudar com horários.");

        await _ambiente.EnviarAsync(c, "sim");

        _ambiente.Provedor.Chamadas.Should().Be(antes + 1);
        _ambiente.Provedor.Requisicoes[^1].PromptSistema.Should().Contain("prompt v1");
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Reservado);
    }

    // ---------------------------------------------------------------- histórico, aviso automatizado, guarda, logs

    [Fact]
    public async Task Historico_leva_o_resumo_e_a_confirmacao_como_turnos_do_modelo_e_nunca_o_token()
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);
        await _ambiente.EnviarAsync(c, "sim");
        _ambiente.Provedor.Roteiro = (_, _) => Texto("De nada!");

        await _ambiente.EnviarAsync(c, "obrigado, e se eu precisar cancelar?");

        var ultima = _ambiente.Provedor.Requisicoes[^1];
        var turnosDoModelo = ultima.Mensagens.Where(m => m.Papel == Plataforma.Aplicacao.WhatsAppAgente.Ia.PapelIa.Modelo).Select(m => m.Texto).ToList();
        turnosDoModelo.Should().Contain(t => t!.Contains("Confira o seu agendamento"));
        turnosDoModelo.Should().Contain(t => t!.Contains(TextosAgendamentoAgente.LinkDeGestaoOculto));

        foreach (var requisicao in _ambiente.Provedor.Requisicoes)
            Serializar(requisicao).Should().NotContain("/agendamentos/").And.NotContain("eyJ");
        var confirmacao = ConfirmacaoEntregue()!;
        var token = confirmacao[(confirmacao.IndexOf("/agendamentos/", StringComparison.Ordinal) + "/agendamentos/".Length)..].Split(' ', '\n')[0];
        foreach (var requisicao in _ambiente.Provedor.Requisicoes)
            Serializar(requisicao).Should().NotContain(token);
    }

    [Fact]
    public async Task Aviso_de_atendimento_automatizado_vai_na_primeira_resposta_nao_na_segunda_e_volta_depois_de_N_dias()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Olá! Como posso ajudar?");

        await _ambiente.EnviarAsync(c, "oi");
        var primeira = _ambiente.UltimaEntrega()!;
        primeira.Should().StartWith("Olá! Aqui é o assistente virtual (atendimento automatizado) de Barbearia do Teste.")
            .And.Contain("inteligência artificial").And.Contain("/privacidade").And.EndWith("Olá! Como posso ajudar?");
        (await _ambiente.ConversasAsync()).Single().AvisoAutomatizadoEm.Should().NotBeNull();

        await _ambiente.EnviarAsync(c, "tudo bem?");
        _ambiente.UltimaEntrega().Should().Be("Olá! Como posso ajudar?", "o aviso já foi dado");

        await _ambiente.NoBancoAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE conversas_whatsapp SET aviso_automatizado_em = now() - interval '91 days'"));
        await _ambiente.EnviarAsync(c, "voltei");
        _ambiente.UltimaEntrega().Should().StartWith("Olá! Aqui é o assistente virtual (atendimento automatizado)");
    }

    [Fact]
    public async Task Aviso_de_atendimento_automatizado_nao_existe_com_o_modo_desligado()
    {
        await IniciarAsync(new() { ["Ai:Agendamento:Modo"] = "Desligado" });
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Olá! Como posso ajudar?");

        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.UltimaEntrega().Should().Be("Olá! Como posso ajudar?");
        (await _ambiente.ConversasAsync()).Single().AvisoAutomatizadoEm.Should().BeNull();
    }

    [Fact]
    public async Task Resumo_como_primeira_resposta_leva_o_aviso_e_so_ele_marca_a_conversa()
    {
        var c = await _ambiente.SemearAsync();
        await _ambiente.NoBancoAsync(async db =>
        {
            db.Clientes.Add(Cliente.Criar(c.NegocioId, "Maria Cadastro", TelefoneE164.Criar(TelComMais)));
            return await db.SaveChangesAsync();
        });

        (await _ambiente.PrepararAsync(c, Prepara(c, c.Ana, "10:00"))).Should().BeNull();

        _ambiente.UltimaEntrega().Should().StartWith("Olá! Aqui é o assistente virtual (atendimento automatizado)").And.Contain("Confira o seu agendamento");
        (await _ambiente.ConversasAsync()).Single().AvisoAutomatizadoEm.Should().NotBeNull();
    }

    [Fact]
    public async Task Guarda_de_promessa_falsa_avisa_quando_o_modelo_diz_que_agendou_sem_confirmacao_real()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Pronto, seu corte está agendado para segunda às 10h!");

        await _ambiente.EnviarAsync(c, "agenda aí pra mim");

        _ambiente.UltimaEntrega().Should().Contain("Pronto, seu corte está agendado").And.EndWith(
            "Atenção: ainda não há agendamento feito por aqui. Ele só vale depois do resumo e da sua resposta SIM.");
        (await _ambiente.AgendamentosAsync()).Should().BeEmpty();
        (await _ambiente.PendenciasAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Guarda_de_promessa_falsa_nao_atrapalha_depois_de_uma_confirmacao_real()
    {
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c);
        await _ambiente.EnviarAsync(c, "sim");
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Seu horário está confirmado, até lá!");

        await _ambiente.EnviarAsync(c, "então tá combinado");

        _ambiente.UltimaEntrega().Should().Be("Seu horário está confirmado, até lá!");
    }

    [Fact]
    public async Task Logs_nao_levam_nome_telefone_token_nem_texto_das_mensagens()
    {
        await IniciarAsync(logs: true);
        var c = await _ambiente.SemearAsync();
        await PreparadoAsync(c, nome: "Maria Sigilosa");
        await _ambiente.EnviarAsync(c, "sim");
        var confirmacao = ConfirmacaoEntregue()!;
        var token = confirmacao[(confirmacao.IndexOf("/agendamentos/", StringComparison.Ordinal) + "/agendamentos/".Length)..].Split(' ', '\n')[0];

        var logs = string.Join("\n", _ambiente.Logs!.Linhas);
        logs.Should().NotContain("Maria Sigilosa").And.NotContain("5571988887777").And.NotContain(token)
            .And.NotContain("Confira o seu agendamento").And.NotContain("Agendamento confirmado").And.NotContain("quero agendar");
    }

    // ---------------------------------------------------------------- concorrência e isolamento

    [Fact]
    public async Task Duas_conversas_pedindo_o_mesmo_horario_juntas_geram_um_resumo_e_uma_recusa()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (requisicao, _) => Resultados(requisicao).Count == 0
            ? Chama(("preparar_agendamento", Prepara(c, c.Ana, "10:00", "Maria Souza")))
            : Texto("Esse horário acabou de ser ocupado; posso oferecer outro.");

        await Task.WhenAll(
            _ambiente.EnviarAsync(c, "quero as 10h", "5571911110001"),
            _ambiente.EnviarAsync(c, "quero as 10h", "5571911110002"));

        (await _ambiente.PendenciasAsync()).Should().ContainSingle();
        (await _ambiente.AgendamentosAsync()).Where(a => a.Status == StatusAgendamento.Reservado).Should().ContainSingle();
        _ambiente.Evolution.Envios.Count(e => e.Texto.Contains("Confira o seu agendamento")).Should().Be(1);
        _ambiente.Evolution.Envios.Count(e => e.Texto.Contains("acabou de ser ocupado")).Should().Be(1);
    }

    [Fact]
    public async Task Vinte_conversas_pedindo_o_mesmo_horario_juntas_geram_uma_reserva()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (requisicao, _) => Resultados(requisicao).Count == 0
            ? Chama(("preparar_agendamento", Prepara(c, c.Ana, "10:00", "Maria Souza")))
            : Texto("Esse horário acabou de ser ocupado.");

        await Task.WhenAll(Enumerable.Range(1, 20).Select(i => _ambiente.EnviarAsync(c, "quero as 10h", $"5571922{i:D6}")));

        (await _ambiente.PendenciasAsync()).Should().ContainSingle();
        (await _ambiente.AgendamentosAsync()).Where(a => a.Status == StatusAgendamento.Reservado).Should().ContainSingle();
        _ambiente.Evolution.Envios.Count(e => e.Texto.Contains("Confira o seu agendamento")).Should().Be(1);
        _ambiente.Evolution.Envios.Count(e => e.Texto.Contains("acabou de ser ocupado")).Should().Be(19);
    }

    [Fact]
    public async Task Dois_negocios_com_o_mesmo_telefone_preparam_e_confirmam_cada_um_no_seu()
    {
        var a = await _ambiente.SemearAsync("Negócio Alfa", "Corte Alfa");
        var b = await _ambiente.SemearAsync("Negócio Beta", "Corte Beta");
        _ambiente.Provedor.Roteiro = (requisicao, _) =>
        {
            if (Resultados(requisicao).Count > 0)
                return Texto("ok");
            var prompt = requisicao.PromptSistema;
            var cenario = prompt.Contains("Negócio Alfa") ? a : b;
            return Chama(("preparar_agendamento", Prepara(cenario, cenario.Ana, "10:00", "Maria Souza")));
        };

        await Task.WhenAll(_ambiente.EnviarAsync(a, "quero agendar"), _ambiente.EnviarAsync(b, "quero agendar"));
        (await _ambiente.PendenciasAsync()).Should().HaveCount(2);

        // O "sim" no negócio A só confirma o de A.
        await _ambiente.EnviarAsync(a, "sim");
        var agendamentos = await _ambiente.AgendamentosAsync();
        agendamentos.Single(x => x.NegocioId == a.NegocioId).Status.Should().Be(StatusAgendamento.Agendado);
        agendamentos.Single(x => x.NegocioId == b.NegocioId).Status.Should().Be(StatusAgendamento.Reservado);

        await _ambiente.EnviarAsync(b, "sim");
        agendamentos = await _ambiente.AgendamentosAsync();
        agendamentos.Should().OnlyContain(x => x.Status == StatusAgendamento.Agendado);
        (await _ambiente.ClientesAsync()).Select(x => x.NegocioId).Should().BeEquivalentTo([a.NegocioId, b.NegocioId]);
    }
}
