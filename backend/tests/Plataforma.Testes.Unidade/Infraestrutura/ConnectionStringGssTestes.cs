using Npgsql;
using Plataforma.Infraestrutura.DependencyInjection;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

public class ConnectionStringGssTestes
{
    [Fact]
    public void Desliga_a_criptografia_GSS_sem_perder_os_demais_campos()
    {
        var resultado = new NpgsqlConnectionStringBuilder(InfraestruturaServiceCollectionExtensions.DesligarCriptografiaGss(
            "Host=postgres.railway.internal;Port=5432;Database=railway;Username=postgres;Password=segredo"));

        Assert.Equal(GssEncryptionMode.Disable, resultado.GssEncryptionMode);
        Assert.Equal("postgres.railway.internal", resultado.Host);
        Assert.Equal(5432, resultado.Port);
        Assert.Equal("railway", resultado.Database);
        Assert.Equal("postgres", resultado.Username);
    }

    [Fact]
    public void Mantem_a_senha_com_caracteres_especiais()
    {
        var resultado = new NpgsqlConnectionStringBuilder(InfraestruturaServiceCollectionExtensions.DesligarCriptografiaGss(
            "Host=h;Database=d;Username=u;Password=\"a;b=c'd\""));

        Assert.Equal("a;b=c'd", resultado.Password);
    }

    [Fact]
    public void Forca_Disable_mesmo_se_a_string_pedir_Require()
    {
        var resultado = new NpgsqlConnectionStringBuilder(InfraestruturaServiceCollectionExtensions.DesligarCriptografiaGss(
            "Host=h;Database=d;Username=u;Password=p;GSS Encryption Mode=Require"));

        Assert.Equal(GssEncryptionMode.Disable, resultado.GssEncryptionMode);
    }
}
