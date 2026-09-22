namespace Plataforma.Dominio.Seguranca;

/// <summary>
/// CPF como fica guardado no banco (seção 8.4): nunca em texto puro. <see cref="Mascarado"/>
/// é seguro para aparecer na interface para qualquer usuário autorizado a ver o cadastro;
/// os dígitos completos só saem via <c>ICriptografiaCpf.Revelar</c>, chamado apenas em fluxos
/// que já checaram a permissão de ver CPF completo.
/// </summary>
public sealed record CpfProtegido(
    string Mascarado,
    byte[] TextoCifrado,
    byte[] Nonce,
    string ChaveId);
