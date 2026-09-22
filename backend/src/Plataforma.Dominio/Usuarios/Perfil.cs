namespace Plataforma.Dominio.Usuarios;

/// <summary>Perfis iniciais (seção 7). Só definem as permissões padrão ao criar o usuário — a autorização de verdade é sempre por permissão (seção 4).</summary>
public enum Perfil
{
    Administrador = 1,
    Recepcionista = 2,
    Profissional = 3,
}
