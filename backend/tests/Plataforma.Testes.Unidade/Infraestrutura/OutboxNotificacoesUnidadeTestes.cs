using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Notificacoes;
using Plataforma.Infraestrutura.Notificacoes.Outbox;
using Plataforma.Infraestrutura.Opcoes;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>B7.4-B: política de tentativas, opções (flags e backoff), chaves de deduplicação e classificação dos resultados dos provedores.</summary>
public class PoliticaDeTentativasTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static NotificacaoPendente Item(TipoNotificacao tipo, DateTimeOffset expiraEm)
    {
        var item = (NotificacaoPendente)Activator.CreateInstance(typeof(NotificacaoPendente), nonPublic: true)!;
        Definir(item, nameof(NotificacaoPendente.Tipo), tipo);
        Definir(item, nameof(NotificacaoPendente.ExpiraEm), expiraEm);
        return item;
    }

    private static void Definir(object alvo, string propriedade, object valor) =>
        alvo.GetType().GetProperty(propriedade, BindingFlags.Public | BindingFlags.Instance)!.SetValue(alvo, valor);

    private static PoliticaDeTentativas Politica(Action<OpcoesNotificacoes>? ajustar = null)
    {
        var opcoes = new OpcoesNotificacoes();
        ajustar?.Invoke(opcoes);
        return new PoliticaDeTentativas(opcoes);
    }

    private static readonly ResultadoDespacho Retentavel = new(ClasseDespacho.FalhaRetentavel);

    [Fact]
    public void Operacional_tem_no_maximo_cinco_tentativas_com_esperas_de_1_5_60_e_360_minutos()
    {
        var politica = Politica();
        var item = Item(TipoNotificacao.Confirmacao, Agora.AddHours(24));
        var esperas = new[] { 1, 5, 60, 360 };

        for (var tentativa = 1; tentativa <= 4; tentativa++)
        {
            var t = politica.AposResultado(item, tentativa, Retentavel, Agora);
            t.Status.Should().Be(StatusNotificacao.Pendente);
            t.ProximaTentativaEm.Should().Be(Agora.AddMinutes(esperas[tentativa - 1]));
            t.Tentativas.Should().Be(tentativa);
        }

        var quinta = politica.AposResultado(item, 5, Retentavel, Agora);
        quinta.Status.Should().Be(StatusNotificacao.Falhou);
        quinta.Codigo.Should().Be(CodigosOutbox.TentativasEsgotadas);
    }

    [Fact]
    public void Lembrete_tem_despacho_imediato_mais_tres_tentativas_apos_1_5_e_15_minutos()
    {
        var politica = Politica();
        var item = Item(TipoNotificacao.Lembrete60, Agora.AddHours(1));
        var esperas = new[] { 1, 5, 15 };

        for (var tentativa = 1; tentativa <= 3; tentativa++)
            politica.AposResultado(item, tentativa, Retentavel, Agora).ProximaTentativaEm.Should().Be(Agora.AddMinutes(esperas[tentativa - 1]));

        politica.AposResultado(item, 4, Retentavel, Agora).Should().Match<Transicao>(
            t => t.Status == StatusNotificacao.Falhou && t.Codigo == CodigosOutbox.TentativasEsgotadas);
    }

    [Fact]
    public void Tentativa_que_cairia_depois_do_prazo_e_cancelada_no_lembrete_e_falha_definitiva_na_operacional()
    {
        var politica = Politica();

        var lembrete = politica.AposResultado(Item(TipoNotificacao.Lembrete60, Agora.AddMinutes(10)), 3, Retentavel, Agora);
        lembrete.Status.Should().Be(StatusNotificacao.Cancelada);
        lembrete.Codigo.Should().Be(CodigosOutbox.Expirada);

        var operacional = politica.AposResultado(Item(TipoNotificacao.Cancelamento, Agora.AddMinutes(30)), 3, Retentavel, Agora);
        operacional.Status.Should().Be(StatusNotificacao.Falhou);
        operacional.Codigo.Should().Be(CodigosOutbox.PrazoEsgotado);
    }

    [Fact]
    public void Aceito_recusado_e_ambiguo_encerram_sem_nova_tentativa()
    {
        var politica = Politica();
        var item = Item(TipoNotificacao.ProfissionalNovo, Agora.AddHours(24));

        var aceito = politica.AposResultado(item, 1, new ResultadoDespacho(ClasseDespacho.Aceito, "ID-1", StatusCanal.Pendente), Agora);
        aceito.Status.Should().Be(StatusNotificacao.Enviada);
        aceito.IdMensagem.Should().Be("ID-1");

        politica.AposResultado(item, 1, new ResultadoDespacho(ClasseDespacho.FalhaDefinitiva), Agora).Should().Match<Transicao>(
            t => t.Status == StatusNotificacao.Falhou && t.Codigo == CodigosOutbox.RecusadaProvedor && t.ProximaTentativaEm == null);
        politica.AposResultado(item, 1, new ResultadoDespacho(ClasseDespacho.Ambiguo), Agora).Should().Match<Transicao>(
            t => t.Status == StatusNotificacao.Desconhecida && t.Codigo == CodigosOutbox.TimeoutAmbiguo && t.ProximaTentativaEm == null);
    }

    [Fact]
    public void Prazo_vencido_cancela_lembretes_e_retorno_e_falha_as_operacionais()
    {
        PoliticaDeTentativas.Vencida(Item(TipoNotificacao.Lembrete60, Agora), 0, Agora).Status.Should().Be(StatusNotificacao.Cancelada);
        PoliticaDeTentativas.Vencida(Item(TipoNotificacao.LembreteRetorno, Agora), 0, Agora).Status.Should().Be(StatusNotificacao.Cancelada);
        PoliticaDeTentativas.Vencida(Item(TipoNotificacao.Confirmacao, Agora), 0, Agora).Status.Should().Be(StatusNotificacao.Falhou);
    }

    [Fact]
    public void Esperas_configuradas_valem_e_o_numero_de_tentativas_acompanha()
    {
        var politica = Politica(o => o.OperacionaisBackoffMinutos = "2,4");
        var item = Item(TipoNotificacao.Confirmacao, Agora.AddHours(24));

        politica.AposResultado(item, 1, Retentavel, Agora).ProximaTentativaEm.Should().Be(Agora.AddMinutes(2));
        politica.AposResultado(item, 2, Retentavel, Agora).ProximaTentativaEm.Should().Be(Agora.AddMinutes(4));
        politica.AposResultado(item, 3, Retentavel, Agora).Status.Should().Be(StatusNotificacao.Falhou);
    }
}

public class OpcoesNotificacoesTestes
{
    private static List<ValidationResult> Validar(OpcoesNotificacoes opcoes)
    {
        var resultados = new List<ValidationResult>();
        Validator.TryValidateObject(opcoes, new ValidationContext(opcoes), resultados, validateAllProperties: true);
        return resultados;
    }

    [Fact]
    public void Padroes_sao_validos_e_a_outbox_nasce_desligada()
    {
        var opcoes = new OpcoesNotificacoes();

        Validar(opcoes).Should().BeEmpty();
        opcoes.OutboxHabilitada.Should().BeFalse();
        opcoes.UsaOutbox(FamiliaNotificacao.Lembrete60).Should().BeFalse();
        opcoes.LeaseSegundos.Should().Be(120);
        opcoes.RetencaoDias.Should().Be(30);
        opcoes.MaximoDeTentativas(FamiliaNotificacao.Lembrete60).Should().Be(4);
        opcoes.MaximoDeTentativas(FamiliaNotificacao.Operacional).Should().Be(5);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Familia_so_usa_a_outbox_com_a_chave_geral_e_a_propria_chave_ligadas(bool geral, bool familia, bool esperado)
    {
        var opcoes = new OpcoesNotificacoes { OutboxHabilitada = geral, OutboxLembrete = familia, OutboxOperacionais = familia, OutboxRetorno = familia };

        opcoes.UsaOutbox(FamiliaNotificacao.Lembrete60).Should().Be(esperado);
        opcoes.UsaOutbox(FamiliaNotificacao.Operacional).Should().Be(esperado);
        opcoes.UsaOutbox(FamiliaNotificacao.Retorno).Should().Be(esperado);
    }

    [Fact]
    public void Chave_geral_ligada_sozinha_nao_liga_nenhuma_familia()
    {
        var opcoes = new OpcoesNotificacoes { OutboxHabilitada = true };

        new[] { FamiliaNotificacao.Lembrete60, FamiliaNotificacao.Operacional, FamiliaNotificacao.Retorno }
            .Should().OnlyContain(f => !opcoes.UsaOutbox(f));
    }

    [Theory]
    [InlineData("0,5")]
    [InlineData("1,,5")]
    [InlineData("a")]
    [InlineData("")]
    [InlineData("1,5,15,30")]
    public void Backoff_do_lembrete_invalido_e_recusado(string valor) =>
        Validar(new OpcoesNotificacoes { LembreteBackoffMinutos = valor }).Should().NotBeEmpty();

    [Theory]
    [InlineData("1,5,60,360,720")]
    [InlineData("0,10")]
    public void Backoff_operacional_com_mais_de_cinco_tentativas_ou_valor_zero_e_recusado(string valor) =>
        Validar(new OpcoesNotificacoes { OperacionaisBackoffMinutos = valor }).Should().NotBeEmpty();

    [Fact]
    public void Esperas_operacionais_que_nao_cabem_no_prazo_sao_recusadas()
    {
        var opcoes = new OpcoesNotificacoes { OperacionaisBackoffMinutos = "60,600,900", OperacionaisPrazoHoras = 24 };

        opcoes.Validate(new ValidationContext(opcoes)).Should().ContainSingle();
    }

    [Fact]
    public void Lease_e_retencao_fora_da_faixa_sao_recusados()
    {
        Validar(new OpcoesNotificacoes { LeaseSegundos = 5 }).Should().NotBeEmpty();
        Validar(new OpcoesNotificacoes { RetencaoDias = 0 }).Should().NotBeEmpty();
        Validar(new OpcoesNotificacoes { TamanhoLote = 0 }).Should().NotBeEmpty();
    }
}

public class ChavesDaOutboxTestes
{
    [Fact]
    public void Versao_do_horario_e_deterministica_por_minuto_e_distingue_o_horario_antigo_do_novo()
    {
        var inicio = new DateTimeOffset(2026, 10, 19, 13, 0, 0, TimeSpan.Zero);

        TiposDeNotificacao.VersaoDoHorario(inicio).Should().Be(TiposDeNotificacao.VersaoDoHorario(inicio.AddSeconds(30)));
        TiposDeNotificacao.VersaoDoHorario(inicio).Should().Be(TiposDeNotificacao.VersaoDoHorario(inicio.ToOffset(TimeSpan.FromHours(-3))), "o fuso não muda a versão");
        TiposDeNotificacao.VersaoDoHorario(inicio).Should().NotBe(TiposDeNotificacao.VersaoDoHorario(inicio.AddMinutes(15)));
    }

    [Fact]
    public void Chaves_de_deduplicacao_seguem_o_formato_combinado()
    {
        var agendamento = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var profissional = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var cliente = Guid.Parse("33333333-3333-3333-3333-333333333333");

        TiposDeNotificacao.ChaveConfirmacao(agendamento).Should().Be($"agendamento:{agendamento}:confirmacao");
        TiposDeNotificacao.ChaveLembrete60(agendamento, 7).Should().Be($"agendamento:{agendamento}:lembrete60:7");
        TiposDeNotificacao.ChaveCancelamento(agendamento).Should().Be($"agendamento:{agendamento}:cancelamento");
        TiposDeNotificacao.ChaveProfissionalNovo(agendamento, profissional).Should().Be($"agendamento:{agendamento}:profissional:novo:{profissional}");
        TiposDeNotificacao.ChaveProfissionalRemarcado(agendamento, 9).Should().Be($"agendamento:{agendamento}:profissional:remarcado:9");
        TiposDeNotificacao.ChaveProfissionalCancelado(agendamento).Should().Be($"agendamento:{agendamento}:profissional:cancelado");
        TiposDeNotificacao.ChaveRetorno(cliente, agendamento).Should().Be($"cliente:{cliente}:retorno:{agendamento}");
    }

    [Fact]
    public void Cada_tipo_pertence_a_uma_familia_de_regras()
    {
        TipoNotificacao.Lembrete60.Familia().Should().Be(FamiliaNotificacao.Lembrete60);
        TipoNotificacao.LembreteRetorno.Familia().Should().Be(FamiliaNotificacao.Retorno);
        new[] { TipoNotificacao.Confirmacao, TipoNotificacao.Cancelamento, TipoNotificacao.ProfissionalNovo, TipoNotificacao.ProfissionalRemarcado, TipoNotificacao.ProfissionalCancelado }
            .Should().OnlyContain(t => t.Familia() == FamiliaNotificacao.Operacional);
    }
}

public class DespachanteDeCanaisTestes
{
    private static readonly TelefoneE164 Telefone = TelefoneE164.Criar("+5571988887777");
    private static readonly EmailMontado Mensagem = new("assunto", "<p>corpo</p>");

    private static DespachanteDeCanais Despachante(IEmailSender? email = null, IMensageriaWhatsApp? whatsApp = null) =>
        new(email ?? new EmailQue(() => Task.CompletedTask), whatsApp ?? new WhatsAppQue(() => Task.FromResult(ResultadoEnvioWhatsApp.Enviado())),
            NullLogger<DespachanteDeCanais>.Instance);

    [Fact]
    public async Task Email_aceito_e_aceito()
    {
        var r = await Despachante().EnviarEmailAsync("a@b.com", Mensagem, CancellationToken.None);

        r.Classe.Should().Be(ClasseDespacho.Aceito);
        r.Status.Should().Be(StatusCanal.Enviado);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ClasseDespacho.FalhaDefinitiva)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ClasseDespacho.FalhaDefinitiva)]
    [InlineData(HttpStatusCode.Unauthorized, ClasseDespacho.FalhaDefinitiva)]
    [InlineData(HttpStatusCode.TooManyRequests, ClasseDespacho.FalhaRetentavel)]
    [InlineData(HttpStatusCode.RequestTimeout, ClasseDespacho.FalhaRetentavel)]
    [InlineData(HttpStatusCode.InternalServerError, ClasseDespacho.FalhaRetentavel)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ClasseDespacho.FalhaRetentavel)]
    public async Task Email_com_erro_http_e_classificado_pelo_status(HttpStatusCode status, ClasseDespacho esperado)
    {
        var despachante = Despachante(new EmailQue(() => throw new HttpRequestException("falhou", null, status)));

        (await despachante.EnviarEmailAsync("a@b.com", Mensagem, CancellationToken.None)).Classe.Should().Be(esperado);
    }

    [Fact]
    public async Task Email_com_falha_de_transporte_ou_timeout_e_retentavel()
    {
        var semResposta = Despachante(new EmailQue(() => throw new HttpRequestException("sem rede")));
        var timeout = Despachante(new EmailQue(() => throw new TaskCanceledException("timeout do HttpClient")));

        (await semResposta.EnviarEmailAsync("a@b.com", Mensagem, CancellationToken.None)).Classe.Should().Be(ClasseDespacho.FalhaRetentavel);
        (await timeout.EnviarEmailAsync("a@b.com", Mensagem, CancellationToken.None)).Classe.Should().Be(ClasseDespacho.FalhaRetentavel);
    }

    [Fact]
    public async Task Cancelamento_pedido_pelo_chamador_propaga_em_vez_de_virar_falha()
    {
        using var origem = new CancellationTokenSource();
        await origem.CancelAsync();
        var despachante = Despachante(new EmailQue(() => throw new OperationCanceledException(origem.Token)));

        var enviar = () => despachante.EnviarEmailAsync("a@b.com", Mensagem, origem.Token);

        await enviar.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WhatsApp_e_classificado_pelo_que_a_falha_permite_concluir()
    {
        async Task<ResultadoDespacho> Enviar(ResultadoEnvioWhatsApp resultado) =>
            await Despachante(whatsApp: new WhatsAppQue(() => Task.FromResult(resultado))).EnviarWhatsAppAsync(Telefone, "oi", CancellationToken.None);

        (await Enviar(ResultadoEnvioWhatsApp.Enviado())).Classe.Should().Be(ClasseDespacho.Aceito);
        var aguardando = await Enviar(ResultadoEnvioWhatsApp.AguardandoConfirmacao("ID-9"));
        aguardando.Classe.Should().Be(ClasseDespacho.Aceito);
        aguardando.IdMensagem.Should().Be("ID-9");
        aguardando.Status.Should().Be(StatusCanal.Pendente);
        (await Enviar(ResultadoEnvioWhatsApp.Indisponivel())).Classe.Should().Be(ClasseDespacho.FalhaRetentavel);
        (await Enviar(ResultadoEnvioWhatsApp.Recusada())).Classe.Should().Be(ClasseDespacho.FalhaDefinitiva);
        (await Enviar(ResultadoEnvioWhatsApp.Ambigua())).Classe.Should().Be(ClasseDespacho.Ambiguo);
        (await Enviar(new ResultadoEnvioWhatsApp(StatusCanal.Falhou))).Classe.Should().Be(ClasseDespacho.FalhaRetentavel, "falha sem classe, de um provedor antigo");
    }

    [Fact]
    public async Task Provedor_de_whatsapp_que_lanca_e_tratado_como_ambiguo()
    {
        var despachante = Despachante(whatsApp: new WhatsAppQue(() => throw new InvalidOperationException("erro com dado pessoal")));

        (await despachante.EnviarWhatsAppAsync(Telefone, "oi", CancellationToken.None)).Classe.Should().Be(ClasseDespacho.Ambiguo);
    }

    private sealed class EmailQue(Func<Task> acao) : IEmailSender
    {
        public Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken cancellationToken = default) => acao();
    }

    private sealed class WhatsAppQue(Func<Task<ResultadoEnvioWhatsApp>> acao) : IMensageriaWhatsApp
    {
        public Task<ResultadoEnvioWhatsApp> EnviarAsync(TelefoneE164 telefone, string mensagem, CancellationToken cancellationToken = default) => acao();
    }
}
