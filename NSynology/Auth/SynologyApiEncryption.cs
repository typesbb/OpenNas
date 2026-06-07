using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NSynology.Auth;

/// <summary>SYNO.API.Encryption RSA+AES 混合加密（与 DSM / Synology Photos Android 登录一致）。</summary>
internal static class SynologyApiEncryption
{
    private const string CipherTokenField = "__cIpHeRtOkEn";
    private const string AesSaltMagic = "Salted__";
    private static readonly char[] PassphraseChars =
        "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ~!@#$%^&*()_+-/".ToCharArray();

    public sealed record EncryptionInfo(
        string CipherKey,
        string CipherToken,
        string PublicKey,
        long ServerTime);

    public static EncryptionInfo ParseInfo(JsonElement data) =>
        new(
            data.GetProperty("cipherkey").GetString() ?? "__cIpHeRtExT",
            data.GetProperty("ciphertoken").GetString() ?? CipherTokenField,
            data.GetProperty("public_key").GetString() ?? "",
            data.TryGetProperty("server_time", out var t) ? t.GetInt64() : DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>将登录字段加密为 <c>__cIpHeRtExT</c> JSON 与 <c>client_time</c>。</summary>
    public static (string CipherFieldName, string CipherPayload, long ClientTime) EncryptLoginFields(
        EncryptionInfo info,
        IReadOnlyDictionary<string, string> fields)
    {
        var payload = new Dictionary<string, string>(fields)
        {
            [info.CipherToken] = info.ServerTime.ToString()
        };

        var randomPassphrase = RandomPassphrase(501);
        var rsaCipher = RsaEncrypt(randomPassphrase, info.PublicKey);
        var plainQuery = string.Join("&", payload.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var aesCipher = AesEncrypt(randomPassphrase, plainQuery);

        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["rsa"] = Convert.ToBase64String(rsaCipher),
            ["aes"] = Convert.ToBase64String(aesCipher)
        });

        return (info.CipherKey, json, info.ServerTime);
    }

    private static byte[] RandomPassphrase(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
            bytes[i] = (byte)PassphraseChars[RandomNumberGenerator.GetInt32(PassphraseChars.Length)];
        return bytes;
    }

    private static byte[] RsaEncrypt(byte[] plaintext, string publicKey)
    {
        using var rsa = RSA.Create();
        if (publicKey.Contains('=', StringComparison.Ordinal) || publicKey.StartsWith("MI", StringComparison.Ordinal))
        {
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        }
        else
        {
            var modulus = ParseHexBigInteger(publicKey);
            var parameters = new RSAParameters
            {
                Modulus = modulus,
                Exponent = [0x01, 0x00, 0x01]
            };
            rsa.ImportParameters(parameters);
        }

        return rsa.Encrypt(plaintext, RSAEncryptionPadding.Pkcs1);
    }

    private static byte[] ParseHexBigInteger(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        if (bytes.Length > 0 && bytes[0] == 0)
            return bytes;
        var padded = new byte[bytes.Length + 1];
        Buffer.BlockCopy(bytes, 0, padded, 1, bytes.Length);
        return padded;
    }

    private static byte[] AesEncrypt(byte[] password, string text)
    {
        var salt = RandomNumberGenerator.GetBytes(8);
        var (key, iv) = DeriveKeyAndIv(password, salt, 32, 16);
        var plain = Pkcs7Pad(Encoding.UTF8.GetBytes(text), 16);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key;
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
        var result = new byte[AesSaltMagic.Length + salt.Length + cipher.Length];
        Encoding.ASCII.GetBytes(AesSaltMagic).CopyTo(result, 0);
        salt.CopyTo(result, AesSaltMagic.Length);
        cipher.CopyTo(result, AesSaltMagic.Length + salt.Length);
        return result;
    }

    private static (byte[] Key, byte[] Iv) DeriveKeyAndIv(byte[] password, byte[] salt, int keyLength, int ivLength)
    {
        var derived = new byte[keyLength + ivLength];
        var offset = 0;
        var block = Array.Empty<byte>();
        using var md5 = MD5.Create();
        while (offset < derived.Length)
        {
            block = md5.ComputeHash(block.Concat(password).Concat(salt).ToArray());
            var copy = Math.Min(block.Length, derived.Length - offset);
            Buffer.BlockCopy(block, 0, derived, offset, copy);
            offset += copy;
        }

        var key = derived[..keyLength];
        var iv = derived[keyLength..(keyLength + ivLength)];
        return (key, iv);
    }

    private static byte[] Pkcs7Pad(byte[] data, int blockSize)
    {
        var pad = blockSize - (data.Length % blockSize);
        var padded = new byte[data.Length + pad];
        Buffer.BlockCopy(data, 0, padded, 0, data.Length);
        for (var i = data.Length; i < padded.Length; i++)
            padded[i] = (byte)pad;
        return padded;
    }
}
