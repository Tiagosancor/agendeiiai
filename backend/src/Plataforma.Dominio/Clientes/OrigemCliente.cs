namespace Plataforma.Dominio.Clientes;

/// <summary>De onde o cliente veio — usado pela recepção para entender o cadastro (seção 8.1.4).</summary>
public enum OrigemCliente
{
    Painel = 1,
    LinkPublico = 2,
}
