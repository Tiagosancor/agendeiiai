using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Seguranca;

namespace Plataforma.Dominio.Profissionais;

/// <summary>
/// Recurso agendável (barbeiro, manicure, terapeuta capilar etc. — seção 1). Horário de
/// trabalho, bloqueios e serviços executados entram na Sprint 2; aqui só o cadastro
/// básico (seção 7 / Sprint 1).
/// </summary>
public class Profissional : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public string Nome { get; private set; } = string.Empty;

    public string? Telefone { get; private set; }

    public string? Email { get; private set; }

    /// <summary>Cargo exibido na página pública (seção 6.1.3, ex.: "Barbeiro", "Manicure").</summary>
    public string? Funcao { get; private set; }

    public Endereco Endereco { get; private set; } = Endereco.Vazio;

    public CpfProtegido? Cpf { get; private set; }

    public string? FotoUrl { get; private set; }

    public bool Ativo { get; private set; } = true;

    /// <summary>
    /// Exclusão lógica (seção 7) de quem já atendeu: some das listas, da página pública e do
    /// limite do plano (fica inativo), mas o nome continua no histórico de atendimentos.
    /// </summary>
    public bool Excluido { get; private set; }

    public DateTimeOffset? ExcluidoEm { get; private set; }

    /// <summary>
    /// Comissão (%) sobre os serviços que ele conclui (seção 7), 0 a 100 com até duas casas.
    /// Só vale para atendimentos concluídos daqui em diante: cada linha concluída guarda o
    /// percentual do momento.
    /// </summary>
    public decimal PercentualComissao { get; private set; }

    protected Profissional()
    {
    }

    private Profissional(Guid negocioId, string nome)
    {
        NegocioId = negocioId;
        Nome = nome;
    }

    public static Profissional Criar(
        Guid negocioId, string nome, string? telefone = null, string? email = null,
        Endereco? endereco = null, CpfProtegido? cpf = null, string? funcao = null)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome é obrigatório.", nameof(nome));

        return new Profissional(negocioId, nome.Trim())
        {
            Telefone = telefone,
            Email = email,
            Endereco = endereco ?? Endereco.Vazio,
            Cpf = cpf,
            Funcao = funcao,
        };
    }

    public void AtualizarDados(string nome, string? telefone, string? email, Endereco endereco, string? funcao = null)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome é obrigatório.", nameof(nome));

        Nome = nome.Trim();
        Telefone = telefone;
        Email = email;
        Endereco = endereco;
        Funcao = funcao;
    }

    public void DefinirCpf(CpfProtegido cpf) => Cpf = cpf;

    public void DefinirFoto(string? fotoUrl) => FotoUrl = fotoUrl;

    public void DefinirPercentualComissao(decimal percentual)
    {
        if (percentual is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percentual), "A comissão precisa estar entre 0 e 100%.");

        if (decimal.Round(percentual, 2) != percentual)
            throw new ArgumentException("A comissão aceita no máximo duas casas decimais.", nameof(percentual));

        PercentualComissao = percentual;
    }

    public void Desativar() => Ativo = false;

    public void Ativar()
    {
        if (Excluido)
            throw new InvalidOperationException("Um profissional excluído não pode ser reativado.");

        Ativo = true;
    }

    /// <summary>Fica só o nome (e a função) para o histórico; os dados pessoais são apagados (seção 7).</summary>
    public void Excluir(DateTimeOffset agora)
    {
        Ativo = false;
        Excluido = true;
        ExcluidoEm = agora;
        Telefone = null;
        Email = null;
        Endereco = Endereco.Vazio;
        Cpf = null;
        FotoUrl = null;
    }
}
