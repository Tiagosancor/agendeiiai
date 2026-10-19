using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Publico;

namespace Plataforma.Infraestrutura.Publico;

public sealed class ConsultaHorariosPublicos : IConsultaHorariosPublicos
{
    private readonly IConsultaDisponibilidade _consultaDisponibilidade;
    private readonly IConsultaCatalogoPublico _consultaCatalogo;

    public ConsultaHorariosPublicos(IConsultaDisponibilidade consultaDisponibilidade, IConsultaCatalogoPublico consultaCatalogo)
    {
        _consultaDisponibilidade = consultaDisponibilidade;
        _consultaCatalogo = consultaCatalogo;
    }

    public async Task<IReadOnlyList<HorarioLivre>> ListarAsync(
        DateOnly data, int duracaoMinutos, IReadOnlyList<Guid> servicoIds, Guid? profissionalId, int maximo,
        CancellationToken cancellationToken = default)
    {
        var profissionais = await _consultaCatalogo.ListarProfissionaisAsync(cancellationToken);
        IReadOnlyList<Guid> profissionalIds = profissionalId is not null
            ? [profissionalId.Value]
            : profissionais.Where(p => servicoIds.All(id => p.ServicoIds.Contains(id))).Select(p => p.Id).ToList();

        var horarios = new List<HorarioLivre>();

        foreach (var id in profissionalIds)
        {
            var duracao = DuracaoDoProfissional(profissionais.FirstOrDefault(p => p.Id == id), servicoIds) ?? duracaoMinutos;
            var livres = await _consultaDisponibilidade.ListarHorariosLivresAsync(id, data, duracao, cancellationToken);
            horarios.AddRange(livres.Select(h => new HorarioLivre(h, id)));
        }

        return horarios.OrderBy(h => h.Inicio).Take(maximo).ToList();
    }

    /// <summary>Soma das durações dele para os serviços pedidos; nulo se não executa algum deles.</summary>
    private static int? DuracaoDoProfissional(ProfissionalPublico? profissional, IReadOnlyList<Guid> servicoIds)
    {
        if (profissional?.Servicos is not { } servicos || servicoIds.Count == 0)
            return null;
        var deles = servicoIds.Select(id => servicos.FirstOrDefault(s => s.ServicoId == id)).ToList();
        return deles.Any(s => s is null) ? null : deles.Sum(s => s!.DuracaoMinutos);
    }
}
