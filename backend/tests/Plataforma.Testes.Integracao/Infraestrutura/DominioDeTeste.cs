namespace Plataforma.Testes.Integracao.Infraestrutura;

/// <summary>
/// Domínio base usado nos testes de resolução por subdomínio — deliberadamente sem
/// relação com o nome do produto, para não depender dele nem fingir que o mecanismo só
/// funciona com "agendei" (que é configuração, nunca código — seção 5).
/// </summary>
public static class DominioDeTeste
{
    public const string Valor = "negocio-teste.example";
}
