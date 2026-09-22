namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>Modo manutenção (seção 8.5.8) — 503 amigável em tudo (inclusive criação de agendamentos), pra virada de uma migração.</summary>
public sealed class OpcoesManutencao
{
    public const string Secao = "Manutencao";

    public bool Ativo { get; set; }
}
