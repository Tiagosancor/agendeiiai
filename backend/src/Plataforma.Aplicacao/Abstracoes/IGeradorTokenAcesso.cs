using Plataforma.Dominio.Usuarios;

namespace Plataforma.Aplicacao.Abstracoes;

/// <summary>
/// Emite o JWT de curta duração do painel (seção 4). Carrega negócio, perfil e permissões
/// como claims — autorização por permissão fica então só na leitura do token, sem round-trip
/// no banco a cada requisição (trade-off: mudar a permissão de alguém só vale no próximo
/// login/renovação — ver docs/decisoes.md).
/// </summary>
public interface IGeradorTokenAcesso
{
    ResultadoTokenAcesso Gerar(Usuario usuario);
}

public sealed record ResultadoTokenAcesso(string Token, DateTimeOffset ExpiraEm);
