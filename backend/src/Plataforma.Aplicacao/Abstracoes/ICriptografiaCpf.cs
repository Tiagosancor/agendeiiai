using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Seguranca;

namespace Plataforma.Aplicacao.Abstracoes;

/// <summary>
/// Criptografia do CPF em repouso (seção 8.4). A chave fica fora do repositório
/// (variável de ambiente) e é versionada — <see cref="CpfProtegido.ChaveId"/> registra
/// qual chave cifrou cada valor, para permitir rotação de chave no futuro sem
/// reprocessar tudo de uma vez.
/// </summary>
public interface ICriptografiaCpf
{
    CpfProtegido Proteger(Cpf cpf);

    /// <summary>Decifra e devolve os 11 dígitos — só chame depois de checar a permissão de ver CPF completo.</summary>
    string Revelar(CpfProtegido protegido);
}
