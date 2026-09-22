namespace Plataforma.Aplicacao.Ics;

/// <summary>Gera o arquivo <c>.ics</c> do agendamento para "Adicionar ao calendário" (seção 6.3).</summary>
public interface IGeradorIcs
{
    string Gerar(DadosIcs dados);
}

public sealed record DadosIcs(
    Guid AgendamentoId, string NomeNegocio, string Local, DateTimeOffset Inicio, DateTimeOffset Fim, string Descricao);
