using Plataforma.Aplicacao.Abstracoes;

namespace Plataforma.Infraestrutura.Seguranca;

/// <summary>bcrypt (seção 8.4) — escolhido em vez de Argon2 por ter API mais simples em .NET e ser igualmente aceito pela especificação.</summary>
public sealed class SenhaHasher : ISenhaHasher
{
    private const int FatorDeCusto = 12;

    public string Hash(string senha) => BCrypt.Net.BCrypt.HashPassword(senha, FatorDeCusto);

    public bool Verificar(string senha, string hash) => BCrypt.Net.BCrypt.Verify(senha, hash);
}
