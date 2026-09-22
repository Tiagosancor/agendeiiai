using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Plataforma.Infraestrutura.Persistencia;

/// <summary>
/// Detecta violação de índice único do Postgres dentro de um <see cref="DbUpdateException"/>.
/// Usado para converter uma corrida (duas requisições simultâneas passam pela checagem
/// "já existe?" antes de qualquer uma delas inserir) na exceção de domínio certa, em vez
/// de deixar vazar um 500 com detalhe de banco (seção 8.2.4 — mesmo espírito, fora do
/// contexto de horário).
/// </summary>
public static class ExcecoesDeConcorrencia
{
    public static bool EhViolacaoDeUnicidade(this DbUpdateException excecao) =>
        excecao.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
