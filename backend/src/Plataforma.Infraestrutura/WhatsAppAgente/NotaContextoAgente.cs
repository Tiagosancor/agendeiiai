using System.Text;
using System.Text.Json;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>Uma ferramenta de consulta que rodou na rodada: nome, argumentos e o resultado que o modelo recebeu.</summary>
public sealed record ConsultaDaRodada(string Ferramenta, JsonElement Argumentos, JsonElement Resultado);

/// <summary>
/// Nota de contexto de ferramentas (<see cref="MarcadoresAgente.ContextoConsulta"/>): resumo curto e DETERMINÍSTICO, montado pelo
/// servidor, do que as ferramentas de consulta devolveram numa rodada. Os resultados de ferramenta não voltam ao modelo nas rodadas
/// seguintes (decisão de segurança), então sem isto ele reinventa ids. Só contém dado público do cadastro (nomes e ids de serviços e
/// profissionais, data e horários livres): nunca texto do cliente, telefone, nome de cliente nem segredo. Os nomes (cadastro livre do
/// tenant) são higienizados como dado. A nota nunca é enviada ao cliente (é uma mensagem <c>Sistema</c> só de histórico).
/// </summary>
public static class NotaContextoAgente
{
    public const string Rotulo = "[contexto consultado pelo sistema — use estes ids; se faltar algo, consulte de novo]";

    /// <summary>Tamanho máximo de UMA nota gravada.</summary>
    public const int MaximoCaracteresNota = 1000;

    /// <summary>Quantas notas entram no histórico e o teto de caracteres delas somadas.</summary>
    public const int MaximoNotasNoHistorico = 2;
    public const int MaximoCaracteresNotasNoHistorico = 1500;

    private const int MaximoNome = 40;
    private const int MaximoLinhasHorarios = 3;

    /// <summary>Monta a nota; <c>null</c> se nenhuma consulta trouxe algo aproveitável.</summary>
    public static string? Montar(IReadOnlyList<ConsultaDaRodada> consultas)
    {
        var servicos = new Dictionary<Guid, string>();
        var profissionais = new Dictionary<Guid, (string Nome, List<string> Servicos)>();
        var horarios = new List<ConsultaHorarios>();

        foreach (var consulta in consultas)
        {
            var resultado = consulta.Resultado;
            if (resultado.ValueKind == JsonValueKind.Object && resultado.TryGetProperty("erro", out _))
                continue;

            switch (consulta.Ferramenta)
            {
                case "listar_servicos" when resultado.ValueKind == JsonValueKind.Array:
                    foreach (var item in resultado.EnumerateArray())
                        if (LerIdENome(item, out var id, out var nome))
                            servicos[id] = nome;
                    break;

                case "listar_profissionais" when resultado.ValueKind == JsonValueKind.Array:
                    foreach (var item in resultado.EnumerateArray())
                    {
                        if (!LerIdENome(item, out var id, out var nome))
                            continue;

                        var faz = new List<string>();
                        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("servicos", out var lista) && lista.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var s in lista.EnumerateArray())
                            {
                                if (LerIdENome(s, out var sid, out var snome))
                                {
                                    servicos[sid] = snome;
                                    faz.Add(snome);
                                }
                            }
                        }

                        profissionais[id] = (nome, faz);
                    }
                    break;

                case "consultar_horarios" when resultado.ValueKind == JsonValueKind.Object:
                    var linha = LerHorarios(consulta.Argumentos, resultado);
                    if (linha is not null)
                        horarios.Add(linha);
                    break;
            }
        }

        // Só as consultas de horário mais recentes; a mais nova primeiro (a de maior prioridade na hora de caber no tamanho).
        horarios.Reverse();
        var linhas = new List<string>();
        var restante = MaximoCaracteresNota - Rotulo.Length - 1;

        foreach (var h in horarios.Take(MaximoLinhasHorarios))
        {
            var texto = FormatarHorarios(h, restante - 1);
            if (texto is not null)
                Adicionar(linhas, texto, ref restante);
        }

        if (profissionais.Count > 0)
        {
            var itens = profissionais.Select(p => p.Value.Servicos.Count > 0
                ? $"{p.Value.Nome}={p.Key} (faz: {string.Join(", ", p.Value.Servicos.Distinct())})"
                : $"{p.Value.Nome}={p.Key}");
            AdicionarLista(linhas, "Profissionais: ", itens, ref restante);
        }

        if (servicos.Count > 0)
            AdicionarLista(linhas, "Serviços: ", servicos.Select(s => $"{s.Value}={s.Key}"), ref restante);

        if (linhas.Count == 0)
            return null;

        // Ordem de leitura: serviços, profissionais, horários (a lista foi montada por prioridade de espaço).
        var ordenadas = linhas.OrderBy(l => l.StartsWith("Serviços", StringComparison.Ordinal) ? 0 : l.StartsWith("Profissionais", StringComparison.Ordinal) ? 1 : 2);
        return Rotulo + "\n" + string.Join("\n", ordenadas);
    }

    /// <summary>Marcador de corte dos horários (os ids de profissional e serviço nunca são cortados).</summary>
    public const string MarcadorHorariosOmitidos = " (…mais horários omitidos)";

    private sealed record ConsultaHorarios(DateOnly Dia, Guid? ServicoId, List<(Guid Id, string Nome, List<string> Horas)> Profissionais, bool Truncado);

    private static ConsultaHorarios? LerHorarios(JsonElement argumentos, JsonElement resultado)
    {
        if (!resultado.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String
            || !DateOnly.TryParseExact(data.GetString(), "yyyy-MM-dd", out var dia))
            return null;
        if (!resultado.TryGetProperty("horarios", out var lista) || lista.ValueKind != JsonValueKind.Array)
            return null;

        // O id do serviço vem do argumento da chamada que DEU CERTO (o servidor conferiu que existe); só entra se for um GUID.
        Guid? servicoId = argumentos.ValueKind == JsonValueKind.Object && argumentos.TryGetProperty("servico_id", out var sid)
            && sid.ValueKind == JsonValueKind.String && Guid.TryParse(sid.GetString(), out var g) ? g : null;

        var porProfissional = new List<(Guid Id, string Nome, List<string> Horas)>();
        foreach (var item in lista.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("profissional_id", out var pid) || !Guid.TryParse(pid.GetString(), out var profissionalId)
                || !item.TryGetProperty("hora", out var hora) || hora.ValueKind != JsonValueKind.String
                || !TimeOnly.TryParseExact(hora.GetString(), "HH:mm", out _))
                continue;

            var nome = item.TryGetProperty("profissional", out var n) ? Higienizar(n.GetString()) : string.Empty;
            var indice = porProfissional.FindIndex(x => x.Id == profissionalId);
            if (indice < 0)
                porProfissional.Add((profissionalId, nome, [hora.GetString()!]));
            else
                porProfissional[indice].Horas.Add(hora.GetString()!);
        }

        var truncado = resultado.TryGetProperty("truncado", out var t) && t.ValueKind == JsonValueKind.True;
        return new ConsultaHorarios(dia, servicoId, porProfissional, truncado);
    }

    /// <summary>
    /// Monta a linha de horários dentro do orçamento. O cabeçalho (data e id do serviço) e o par nome+id COMPLETO de cada profissional
    /// são o mínimo inegociável; os horários entram depois, em rodízio (um de cada profissional por volta), enquanto couberem, e o corte
    /// é marcado. Se nem o mínimo couber (muitos profissionais com nomes longos: ~85 caracteres cada, ou seja, uns 9 numa nota), devolve
    /// <c>null</c> — comportamento seguro: a linha some em vez de ficar com um id cortado.
    /// </summary>
    private static string? FormatarHorarios(ConsultaHorarios c, int orcamento)
    {
        var cabecalho = new StringBuilder("Horários livres em ").Append(c.Dia.ToString("yyyy-MM-dd"));
        if (c.ServicoId is not null)
            cabecalho.Append(" (servico_id=").Append(c.ServicoId).Append(')');
        cabecalho.Append(": ");

        if (c.Profissionais.Count == 0)
            return cabecalho.Append("nenhum").Length <= orcamento ? cabecalho.ToString() : null;

        var bases = c.Profissionais.Select(p => $"{p.Nome} (profissional_id={p.Id})").ToList();
        var minimo = cabecalho.Length + bases.Sum(b => b.Length) + 2 * (bases.Count - 1);
        if (minimo > orcamento)
            return null;

        // Custo de cada horário: " HH:mm" (6), e o primeiro de um profissional ainda paga o ": " no lugar do espaço (7).
        var total = minimo + c.Profissionais.Sum(p => 2 + p.Horas.Count * 6 - 1) + (c.Truncado ? " (há mais)".Length : 0);
        var quantos = new int[bases.Count];
        var omitiu = false;
        if (total <= orcamento)
        {
            for (var i = 0; i < quantos.Length; i++)
                quantos[i] = c.Profissionais[i].Horas.Count;
        }
        else
        {
            var livre = orcamento - minimo - MarcadorHorariosOmitidos.Length;
            var progrediu = true;
            while (progrediu)
            {
                progrediu = false;
                for (var i = 0; i < quantos.Length; i++)
                {
                    if (quantos[i] >= c.Profissionais[i].Horas.Count)
                        continue;
                    var custo = quantos[i] == 0 ? 7 : 6;
                    if (custo > livre)
                        continue;
                    quantos[i]++;
                    livre -= custo;
                    progrediu = true;
                }
            }
            omitiu = true;
        }

        var partes = bases.Select((b, i) => quantos[i] == 0 ? b : $"{b}: {string.Join(' ', c.Profissionais[i].Horas.Take(quantos[i]))}");
        var sb = cabecalho.Append(string.Join("; ", partes));
        if (omitiu)
            sb.Append(MarcadorHorariosOmitidos);
        else if (c.Truncado)
            sb.Append(" (há mais)");
        return sb.ToString();
    }

    private static bool LerIdENome(JsonElement item, out Guid id, out string nome)
    {
        id = default;
        nome = string.Empty;
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("id", out var i) || i.ValueKind != JsonValueKind.String || !Guid.TryParse(i.GetString(), out id)
            || !item.TryGetProperty("nome", out var n) || n.ValueKind != JsonValueKind.String)
            return false;

        nome = Higienizar(n.GetString());
        return nome.Length > 0;
    }

    /// <summary>
    /// Nome de cadastro como DADO: sem controle, sem os delimitadores da nota ([ ] " = ; ( ) :), espaços colapsados, tamanho limitado.
    /// </summary>
    public static string Higienizar(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
            return string.Empty;

        var sb = new StringBuilder(nome.Length);
        foreach (var c in nome)
            sb.Append(char.IsControl(c) || c is '[' or ']' or '"' or '=' or ';' or '(' or ')' or ':' or (char)0x2028 or (char)0x2029 ? ' ' : c);

        var limpo = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return limpo.Length > MaximoNome ? limpo[..MaximoNome].Trim() : limpo;
    }

    private static void Adicionar(List<string> linhas, string linha, ref int restante)
    {
        var custo = linha.Length + 1;
        if (custo > restante)
            return;

        linhas.Add(linha);
        restante -= custo;
    }

    /// <summary>Linha de lista que entra com quantos itens couberem (nunca corta um id no meio).</summary>
    private static void AdicionarLista(List<string> linhas, string prefixo, IEnumerable<string> itens, ref int restante)
    {
        var sb = new StringBuilder(prefixo);
        var algum = false;
        foreach (var item in itens)
        {
            var acrescimo = (algum ? 2 : 0) + item.Length;
            if (sb.Length + acrescimo + 1 > restante)
                break;

            if (algum)
                sb.Append("; ");
            sb.Append(item);
            algum = true;
        }

        if (!algum)
            return;

        linhas.Add(sb.ToString());
        restante -= sb.Length + 1;
    }
}
