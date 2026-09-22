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

    public Endereco Endereco { get; private set; } = Endereco.Vazio;

    public CpfProtegido? Cpf { get; private set; }

    public string? FotoUrl { get; private set; }

    public bool Ativo { get; private set; } = true;

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
        Endereco? endereco = null, CpfProtegido? cpf = null)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome é obrigatório.", nameof(nome));

        return new Profissional(negocioId, nome.Trim())
        {
            Telefone = telefone,
            Email = email,
            Endereco = endereco ?? Endereco.Vazio,
            Cpf = cpf,
        };
    }

    public void AtualizarDados(string nome, string? telefone, string? email, Endereco endereco)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome é obrigatório.", nameof(nome));

        Nome = nome.Trim();
        Telefone = telefone;
        Email = email;
        Endereco = endereco;
    }

    public void DefinirCpf(CpfProtegido cpf) => Cpf = cpf;

    public void DefinirFoto(string? fotoUrl) => FotoUrl = fotoUrl;

    public void Desativar() => Ativo = false;

    public void Ativar() => Ativo = true;
}
