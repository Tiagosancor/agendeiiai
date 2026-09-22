using Microsoft.Extensions.DependencyInjection;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Servicos;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Testes.Integracao.Infraestrutura;

/// <summary>Semeia profissional + horário de trabalho + serviço + cliente direto no banco — os testes de agenda não precisam repetir esse arranjo via HTTP.</summary>
public static class SemeadorDeAgenda
{
    public sealed record CenarioDeAgenda(Guid ProfissionalId, Guid ServicoId, int DuracaoMinutos, Guid ClienteId);

    /// <summary>Profissional com expediente de segunda a sexta, 09:00–12:00 e 13:00–18:00 (almoço das 12h às 13h), um serviço de 30 min e um cliente.</summary>
    public static async Task<CenarioDeAgenda> CriarCenarioPadraoAsync(
        this PlataformaWebApplicationFactory fabrica, Guid negocioId, int duracaoMinutos = 30)
    {
        using var escopo = fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var profissional = Profissional.Criar(negocioId, "Profissional de Teste");
        dbContext.Profissionais.Add(profissional);

        foreach (var dia in new[] { DiaSemana.Segunda, DiaSemana.Terca, DiaSemana.Quarta, DiaSemana.Quinta, DiaSemana.Sexta })
        {
            dbContext.HorariosTrabalho.Add(HorarioTrabalho.Criar(negocioId, profissional.Id, dia, new TimeOnly(9, 0), new TimeOnly(12, 0)));
            dbContext.HorariosTrabalho.Add(HorarioTrabalho.Criar(negocioId, profissional.Id, dia, new TimeOnly(13, 0), new TimeOnly(18, 0)));
        }

        var categoria = Categoria.Criar(negocioId, "Categoria de Teste");
        dbContext.Categorias.Add(categoria);

        var servico = Servico.Criar(negocioId, categoria.Id, "Serviço de Teste", 50m, duracaoMinutos);
        dbContext.Servicos.Add(servico);

        var cliente = Cliente.Criar(negocioId, "Cliente de Teste", TelefoneE164.Criar($"+55719{Random.Shared.Next(10000000, 99999999)}"));
        dbContext.Clientes.Add(cliente);

        await dbContext.SaveChangesAsync();

        return new CenarioDeAgenda(profissional.Id, servico.Id, duracaoMinutos, cliente.Id);
    }
}
