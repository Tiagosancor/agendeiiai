using System.Text;
using Plataforma.Aplicacao.Administracao;
using Plataforma.Infraestrutura.Cadastro;

namespace Plataforma.Api.Comandos;

/// <summary>
/// Comandos administrativos pela linha de comando, na mesma imagem da API (no Railway:
/// <c>railway run</c> ou o shell do serviço). Hoje só existe um:
/// <c>criar-admin-plataforma --email dono@exemplo.com [--nome "Nome"]</c> — o único jeito de
/// criar o <c>AdministradorPlataforma</c> (seção 7). A senha é digitada na hora, sem eco, e
/// nunca vai em argumento (ficaria no histórico do shell). Se o e-mail já existir, redefine a senha.
/// </summary>
public static class ComandosDeLinha
{
    private const string CriarAdminPlataforma = "criar-admin-plataforma";

    public static async Task<bool> ExecutarSeHouverAsync(IServiceProvider servicos, string[] args)
    {
        if (args.Length == 0 || args[0] != CriarAdminPlataforma)
            return false;

        var email = LerOpcao(args, "--email");
        if (string.IsNullOrWhiteSpace(email))
        {
            Console.Error.WriteLine($"Uso: {CriarAdminPlataforma} --email dono@exemplo.com [--nome \"Nome\"]");
            Environment.ExitCode = 1;
            return true;
        }

        var senha = LerSenha("Senha: ");
        if (!ServicoCadastro.SenhaForte(senha))
        {
            Console.Error.WriteLine($"A senha precisa ter pelo menos {ServicoCadastro.TamanhoMinimoSenha} caracteres, com letras e números.");
            Environment.ExitCode = 1;
            return true;
        }

        if (!Console.IsInputRedirected && LerSenha("Repita a senha: ") != senha)
        {
            Console.Error.WriteLine("As senhas não conferem.");
            Environment.ExitCode = 1;
            return true;
        }

        using var escopo = servicos.CreateScope();
        var administracao = escopo.ServiceProvider.GetRequiredService<IAdministracaoPlataforma>();
        var criado = await administracao.CriarOuRedefinirAdministradorAsync(email, LerOpcao(args, "--nome") ?? email, senha);

        Console.WriteLine(criado
            ? $"Administrador da plataforma criado: {email.Trim().ToLowerInvariant()}"
            : $"Administrador já existia; senha redefinida: {email.Trim().ToLowerInvariant()}");
        return true;
    }

    private static string? LerOpcao(string[] args, string nome)
    {
        var indice = Array.IndexOf(args, nome);
        return indice >= 0 && indice + 1 < args.Length ? args[indice + 1] : null;
    }

    /// <summary>Sem eco no terminal; com a entrada redirecionada (pipe), lê a linha como veio.</summary>
    private static string LerSenha(string rotulo)
    {
        if (Console.IsInputRedirected)
            return Console.ReadLine() ?? string.Empty;

        Console.Write(rotulo);
        var senha = new StringBuilder();
        while (true)
        {
            var tecla = Console.ReadKey(intercept: true);
            if (tecla.Key == ConsoleKey.Enter)
                break;
            if (tecla.Key == ConsoleKey.Backspace)
            {
                if (senha.Length > 0)
                    senha.Length--;
                continue;
            }
            if (!char.IsControl(tecla.KeyChar))
                senha.Append(tecla.KeyChar);
        }

        Console.WriteLine();
        return senha.ToString();
    }
}
