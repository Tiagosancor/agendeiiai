namespace Plataforma.Aplicacao.Idempotencia;

/// <summary>Resultado mínimo guardado para operações que devolvem só o Id do que criaram (consumo, vale, agendamento).</summary>
public sealed record ResultadoId(Guid Id);

/// <summary>Resultado mínimo do resgate de fidelidade (a resposta é 204; guarda só que concluiu).</summary>
public sealed record ResultadoConcluido(bool Concluido = true);
