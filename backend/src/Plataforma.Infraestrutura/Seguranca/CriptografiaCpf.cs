using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Seguranca;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.Seguranca;

/// <summary>
/// AES-256-GCM (autenticado) para o CPF em repouso (seção 8.4). O texto cifrado guarda
/// ciphertext + tag de autenticação concatenados; o nonce fica em coluna separada (nunca
/// reutilizado — gerado aleatoriamente a cada chamada).
/// </summary>
public sealed class CriptografiaCpf : ICriptografiaCpf
{
    private const int TamanhoTag = 16;

    private readonly byte[] _chave;
    private readonly string _chaveId;

    public CriptografiaCpf(IOptions<OpcoesCriptografiaCpf> opcoes)
    {
        _chave = Convert.FromBase64String(opcoes.Value.ChaveBase64);
        _chaveId = opcoes.Value.ChaveId;
    }

    public CpfProtegido Proteger(Cpf cpf)
    {
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var textoPuro = Encoding.UTF8.GetBytes(cpf.Digitos);
        var textoCifrado = new byte[textoPuro.Length];
        var tag = new byte[TamanhoTag];

        using (var aesGcm = new AesGcm(_chave, TamanhoTag))
        {
            aesGcm.Encrypt(nonce, textoPuro, textoCifrado, tag);
        }

        var textoCifradoComTag = new byte[textoCifrado.Length + tag.Length];
        Buffer.BlockCopy(textoCifrado, 0, textoCifradoComTag, 0, textoCifrado.Length);
        Buffer.BlockCopy(tag, 0, textoCifradoComTag, textoCifrado.Length, tag.Length);

        return new CpfProtegido(cpf.Mascarado(), textoCifradoComTag, nonce, _chaveId);
    }

    public string Revelar(CpfProtegido protegido)
    {
        if (protegido.ChaveId != _chaveId)
        {
            // Rotação de chave (seção 8.5.4) ainda não implementada — só a chave atual decifra.
            throw new InvalidOperationException(
                $"O CPF foi cifrado com a chave '{protegido.ChaveId}', que não é a chave atual ('{_chaveId}').");
        }

        var tamanhoTextoCifrado = protegido.TextoCifrado.Length - TamanhoTag;
        var textoCifrado = protegido.TextoCifrado[..tamanhoTextoCifrado];
        var tag = protegido.TextoCifrado[tamanhoTextoCifrado..];
        var textoPuro = new byte[tamanhoTextoCifrado];

        using (var aesGcm = new AesGcm(_chave, TamanhoTag))
        {
            aesGcm.Decrypt(protegido.Nonce, textoCifrado, tag, textoPuro);
        }

        return Encoding.UTF8.GetString(textoPuro);
    }
}
