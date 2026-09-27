using FluentAssertions;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Dominio.Verificacao;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class CodigoVerificacaoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly TelefoneE164 Telefone = TelefoneE164.Criar("+5571988887777");

    [Fact]
    public void ConferirEMarcar_com_hash_certo_funciona_uma_unica_vez()
    {
        var codigo = CodigoVerificacao.Criar(Guid.NewGuid(), Telefone, "hash-certo", Agora);

        codigo.ConferirEMarcar("hash-certo", Agora).Should().BeTrue();
        codigo.Usado.Should().BeTrue();

        // Uso único (seção 8.1.2) — o mesmo código não vale de novo.
        codigo.ConferirEMarcar("hash-certo", Agora).Should().BeFalse();
    }

    [Fact]
    public void ConferirEMarcar_com_hash_errado_consome_uma_tentativa()
    {
        var codigo = CodigoVerificacao.Criar(Guid.NewGuid(), Telefone, "hash-certo", Agora);

        codigo.ConferirEMarcar("hash-errado", Agora).Should().BeFalse();

        codigo.TentativasRestantes.Should().Be(2);
        codigo.Usado.Should().BeFalse();
    }

    [Fact]
    public void Apos_3_tentativas_erradas_o_codigo_certo_tambem_falha()
    {
        var codigo = CodigoVerificacao.Criar(Guid.NewGuid(), Telefone, "hash-certo", Agora);

        for (var i = 0; i < 3; i++)
            codigo.ConferirEMarcar("hash-errado", Agora);

        codigo.TentativasRestantes.Should().Be(0);
        codigo.ConferirEMarcar("hash-certo", Agora).Should().BeFalse();
    }

    [Fact]
    public void Codigo_expirado_falha_mesmo_com_hash_certo()
    {
        var codigo = CodigoVerificacao.Criar(Guid.NewGuid(), Telefone, "hash-certo", Agora);

        var seisMinutosDepois = Agora.AddMinutes(6); // válido só por 5 min (seção 8.1.2)

        codigo.ConferirEMarcar("hash-certo", seisMinutosDepois).Should().BeFalse();
    }

    [Fact]
    public void Codigo_invalidado_falha_mesmo_com_hash_certo()
    {
        var codigo = CodigoVerificacao.Criar(Guid.NewGuid(), Telefone, "hash-certo", Agora);
        codigo.Invalidar(); // um código mais novo foi solicitado (seção 8.1.2)

        codigo.ConferirEMarcar("hash-certo", Agora).Should().BeFalse();
    }

    [Fact]
    public void Reenviar_troca_o_codigo_renova_prazo_e_tentativas_ate_o_limite()
    {
        var codigo = CodigoVerificacao.Criar(Guid.NewGuid(), Telefone, "hash-1", Agora);
        codigo.ConferirEMarcar("errado", Agora);
        codigo.RegistrarEnvio(StatusCanal.Falhou, null, StatusCanal.Enviado);

        var depois = Agora.AddMinutes(4);
        codigo.Reenviar("hash-2", depois, maximoReenvios: 3);

        codigo.TentativasReenvio.Should().Be(1);
        codigo.TentativasRestantes.Should().Be(CodigoVerificacao.MaximoTentativasPadrao);
        codigo.ExpiraEm.Should().Be(depois.AddMinutes(CodigoVerificacao.ValidadePadraoMinutos));
        codigo.CanalWhatsAppStatus.Should().BeNull("o envio novo grava o próprio resultado");
        codigo.ConferirEMarcar("hash-1", depois).Should().BeFalse("o código anterior deixa de valer");

        codigo.Reenviar("hash-3", depois, maximoReenvios: 3);
        codigo.Reenviar("hash-4", depois, maximoReenvios: 3);
        codigo.PodeReenviar(3).Should().BeFalse();
        codigo.Invoking(c => c.Reenviar("hash-5", depois, maximoReenvios: 3)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Status_do_whatsapp_so_sai_de_pendente_e_nunca_volta_atras()
    {
        var codigo = CodigoVerificacao.Criar(Guid.NewGuid(), Telefone, "hash", Agora);
        codigo.RegistrarEnvio(StatusCanal.Pendente, "MSG-1", null);

        codigo.AtualizarStatusWhatsApp(StatusCanal.Enviado).Should().BeTrue();
        codigo.AtualizarStatusWhatsApp(StatusCanal.Enviado).Should().BeFalse("repetido não muda nada");
        codigo.AtualizarStatusWhatsApp(StatusCanal.Falhou).Should().BeFalse("um \"falhou\" atrasado não desfaz o entregue");
        codigo.CanalWhatsAppStatus.Should().Be(StatusCanal.Enviado);
    }
}
