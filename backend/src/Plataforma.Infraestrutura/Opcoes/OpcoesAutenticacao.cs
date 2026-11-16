using System.ComponentModel.DataAnnotations;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>Limites de abuso do login e dos identificadores do painel. Tudo tem padrão: nada é obrigatório no ambiente.</summary>
public sealed class OpcoesAutenticacao
{
    public const string Secao = "Autenticacao";

    /// <summary>
    /// Falhas de login no mesmo identificador (existente ou não) dentro da janela que bloqueiam o identificador. Para desligar de
    /// emergência, ponha um valor alto (ex.: 1000) — o bloqueio por identificador é a proteção principal; o limite por IP é contornável.
    /// </summary>
    [Range(1, 1000)]
    public int MaximoFalhasLoginPorIdentificador { get; set; } = 5;

    /// <summary>Janela de contagem das falhas e duração do bloqueio, em minutos.</summary>
    [Range(1, 1440)]
    public int JanelaBloqueioLoginMinutos { get; set; } = 15;

    /// <summary>Requisições de login por IP por minuto (política <c>LoginPainelPorIp</c>; lida na hora, pelo <c>Program.cs</c>).</summary>
    [Range(1, 100000)]
    public int LimiteLoginPorIpPorMinuto { get; set; } = 10;

    /// <summary>Pedidos de redefinição de senha por IP por minuto (lido na hora, pelo <c>Program.cs</c>).</summary>
    [Range(1, 100000)]
    public int LimiteRedefinicaoSenhaPorIpPorMinuto { get; set; } = 5;

    /// <summary>
    /// Respostas 409 de identificador indisponível por autor por hora — acima, 429. Impede o painel de virar um enumerador de
    /// telefones e nomes de usuário. Contado em memória (por instância da API).
    /// </summary>
    [Range(1, 100000)]
    public int LimiteConflitosIdentificadorPorHora { get; set; } = 20;
}
