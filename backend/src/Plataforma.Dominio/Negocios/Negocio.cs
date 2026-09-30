using System.Text.RegularExpressions;
using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Negocios;

/// <summary>
/// Raiz do multi-tenant: cada negócio (barbearia, salão, clínica de estética ou
/// autônomo) é um tenant isolado, resolvido pelo <see cref="Slug"/> no subdomínio.
/// Os campos de marca/perfil (logo, cores, textos, endereço, redes sociais, horário de
/// funcionamento) entraram na Sprint 1 — na Sprint 0 só existia o essencial para a
/// resolução por subdomínio.
/// </summary>
public partial class Negocio : EntidadeBase
{
    private readonly List<HorarioFuncionamentoDia> _horarioFuncionamento = [];

    public Slug Slug { get; private set; } = null!;

    public string NomeExibido { get; private set; } = string.Empty;

    public TipoNegocio Tipo { get; private set; }

    /// <summary>Fuso horário IANA do negócio (ex.: "America/Sao_Paulo"). Datas ficam em UTC no banco (seção 8.2.6).</summary>
    public string Fuso { get; private set; } = "America/Sao_Paulo";

    public bool Ativo { get; private set; } = true;

    public string? LogoUrl { get; private set; }

    public string? CorPrimaria { get; private set; }

    public string? CorSecundaria { get; private set; }

    /// <summary>
    /// Cor de fundo da página pública e do assistente (seção 5, "Cada negócio configura"), sempre <c>#RRGGBB</c>.
    /// Sem imagem de fundo, é só ela; com imagem, fica por baixo enquanto a imagem carrega.
    /// </summary>
    public string? CorFundo { get; private set; }

    /// <summary>Chave da imagem de fundo no <c>IArmazenamentoArquivos</c> (nunca uma URL de fora): só entra imagem enviada e reprocessada.</summary>
    public string? ImagemFundo { get; private set; }

    public string? TituloPagina { get; private set; }

    public string? SubtituloPagina { get; private set; }

    public string? TextoSobre { get; private set; }

    public Endereco Endereco { get; private set; } = Endereco.Vazio;

    public string? Telefone { get; private set; }

    /// <summary>Destino do formulário "Fale Conosco" (seção 6.1.6) — distinto do e-mail de um usuário/login.</summary>
    public string? EmailContato { get; private set; }

    public RedesSociais RedesSociais { get; private set; } = RedesSociais.Vazio;

    /// <summary>
    /// Opt-in por negócio para confirmações e lembretes por WhatsApp além do código, que é
    /// sempre enviado pelos dois canais independente disto (seção 9: cada mensagem tem
    /// custo na Meta).
    /// </summary>
    public bool WhatsAppAtivoParaConfirmacoes { get; private set; }

    /// <summary>
    /// Opt-in por negócio para avisar o profissional também por WhatsApp de agendamento novo,
    /// remarcado ou cancelado (seção 9). Desligado por padrão, inclusive nos negócios que já
    /// existiam — o e-mail ao profissional sai sempre, ligado ou não.
    /// </summary>
    public bool WhatsAppAvisoProfissional { get; private set; }

    public IReadOnlyCollection<HorarioFuncionamentoDia> HorarioFuncionamento => _horarioFuncionamento.AsReadOnly();

    /// <summary>Checklist de primeiros passos do painel dispensado pelo usuário (seção 6.5).</summary>
    public bool ChecklistDispensado { get; private set; }

    /// <summary>Único passo do checklist que o sistema não deduz dos dados: copiar o link de agendamento.</summary>
    public bool LinkAgendamentoCopiado { get; private set; }

    public void DispensarChecklist() => ChecklistDispensado = true;

    public void MarcarLinkAgendamentoCopiado() => LinkAgendamentoCopiado = true;

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

    /// <summary>Atualiza o perfil do negócio (marca, textos, endereço, contato — seção 5 / Sprint 1). Todo texto é sempre tratado como texto puro pelo front (seção 5), nunca HTML.</summary>
    public void AtualizarPerfil(
        string nomeExibido, string? logoUrl, string? corPrimaria, string? corSecundaria,
        string? tituloPagina, string? subtituloPagina, string? textoSobre,
        Endereco endereco, string? telefone, string? emailContato, RedesSociais redesSociais,
        bool whatsAppAtivoParaConfirmacoes)
    {
        if (string.IsNullOrWhiteSpace(nomeExibido))
            throw new ArgumentException("O nome exibido é obrigatório.", nameof(nomeExibido));

        NomeExibido = nomeExibido.Trim();
        LogoUrl = logoUrl;
        CorPrimaria = corPrimaria;
        CorSecundaria = corSecundaria;
        TituloPagina = tituloPagina;
        SubtituloPagina = subtituloPagina;
        TextoSobre = textoSobre;
        Endereco = endereco;
        Telefone = telefone;
        EmailContato = emailContato;
        RedesSociais = redesSociais;
        WhatsAppAtivoParaConfirmacoes = whatsAppAtivoParaConfirmacoes;
    }

    public void DefinirAvisoProfissionalPorWhatsApp(bool ativo) => WhatsAppAvisoProfissional = ativo;

    /// <summary>Vazio tira a cor. Lança <see cref="ArgumentException"/> fora do formato <c>#RRGGBB</c>.</summary>
    public void DefinirCorFundo(string? cor)
    {
        if (string.IsNullOrWhiteSpace(cor))
        {
            CorFundo = null;
            return;
        }

        cor = cor.Trim();
        if (!CorHex().IsMatch(cor))
            throw new ArgumentException("A cor de fundo precisa estar no formato #RRGGBB.", nameof(cor));

        CorFundo = cor.ToLowerInvariant();
    }

    /// <summary>Troca (ou tira, com nulo) a imagem de fundo e devolve a chave anterior, para o arquivo antigo ser apagado.</summary>
    public string? DefinirImagemFundo(string? chave)
    {
        var anterior = ImagemFundo;
        ImagemFundo = string.IsNullOrWhiteSpace(chave) ? null : chave;
        return anterior;
    }

    /// <summary>Substitui o horário de funcionamento inteiro — sempre os 7 dias da semana, um registro cada.</summary>
    public void DefinirHorarioFuncionamento(IEnumerable<HorarioFuncionamentoDia> horario)
    {
        var dias = horario.ToList();

        if (dias.Select(d => d.DiaSemana).Distinct().Count() != dias.Count)
            throw new ArgumentException("Cada dia da semana só pode aparecer uma vez.", nameof(horario));

        _horarioFuncionamento.Clear();
        _horarioFuncionamento.AddRange(dias);
    }

    public void Desativar() => Ativo = false;

    public void Ativar() => Ativo = true;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex CorHex();
}
