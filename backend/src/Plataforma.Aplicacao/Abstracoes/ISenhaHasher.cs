namespace Plataforma.Aplicacao.Abstracoes;

/// <summary>Hash de senha com Argon2 ou bcrypt (seção 8.4) — nunca guardar senha em texto puro.</summary>
public interface ISenhaHasher
{
    string Hash(string senha);

    bool Verificar(string senha, string hash);
}
