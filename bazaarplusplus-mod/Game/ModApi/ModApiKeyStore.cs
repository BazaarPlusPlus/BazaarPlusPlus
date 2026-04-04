#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.ModApi;

internal sealed class ModApiKeyStore
{
    private readonly string _privateKeyPath;
    private readonly object _sync = new();
    private ModApiKeyMaterial? _cachedKeyMaterial;

    public ModApiKeyStore(string privateKeyPath)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPath))
            throw new ArgumentException("Private key path is required.", nameof(privateKeyPath));

        _privateKeyPath = privateKeyPath;
    }

    public ModApiKeyMaterial GetOrCreateKeyMaterial()
    {
        lock (_sync)
        {
            if (_cachedKeyMaterial != null)
                return _cachedKeyMaterial;

            if (File.Exists(_privateKeyPath))
            {
                try
                {
                    var keyMaterial = JsonConvert.DeserializeObject<ModApiKeyMaterial>(
                        File.ReadAllText(_privateKeyPath)
                    );
                    if (keyMaterial != null)
                    {
                        ValidateKeyMaterial(keyMaterial);
                        _cachedKeyMaterial = keyMaterial;
                        return _cachedKeyMaterial;
                    }
                }
                catch (Exception ex)
                {
                    BackupCorruptedKeyFile(ex);
                }
            }

            using var rsa = RSA.Create();
            rsa.KeySize = 2048;
            var parameters = rsa.ExportParameters(true);
            _cachedKeyMaterial = ModApiKeyMaterial.FromParameters(parameters);

            var directory = Path.GetDirectoryName(_privateKeyPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                _privateKeyPath,
                JsonConvert.SerializeObject(_cachedKeyMaterial, Formatting.Indented)
            );

            return _cachedKeyMaterial;
        }
    }

    public string Sign(string canonicalString)
    {
        if (canonicalString == null)
            throw new ArgumentNullException(nameof(canonicalString));

        var keyMaterial = GetOrCreateKeyMaterial();
        using var rsa = RSA.Create();
        rsa.ImportParameters(keyMaterial.ToParameters());
        var signatureBytes = rsa.SignData(
            System.Text.Encoding.UTF8.GetBytes(canonicalString),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        return Convert.ToBase64String(signatureBytes);
    }

    private void BackupCorruptedKeyFile(Exception ex)
    {
        try
        {
            var backupPath = $"{_privateKeyPath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
            File.Copy(_privateKeyPath, backupPath, overwrite: false);
            BppLog.Warn(
                "ModApiKeyStore",
                $"Failed to read private key from {_privateKeyPath}: {ex.GetType().Name} - {ex.Message}. Backed up corrupted key to {backupPath} and generating a new keypair."
            );
        }
        catch (Exception backupEx)
        {
            BppLog.Warn(
                "ModApiKeyStore",
                $"Failed to read private key from {_privateKeyPath}: {ex.GetType().Name} - {ex.Message}. Could not back up corrupted key: {backupEx.GetType().Name} - {backupEx.Message}. Generating a new keypair."
            );
        }
    }

    private static void ValidateKeyMaterial(ModApiKeyMaterial keyMaterial)
    {
        using var rsa = RSA.Create();
        rsa.ImportParameters(keyMaterial.ToParameters());
    }
}

internal sealed class ModApiKeyMaterial
{
    [JsonProperty("algorithm")]
    public string Algorithm { get; set; } = "rsa-pkcs1-sha256";

    [JsonProperty("modulus_b64")]
    public string ModulusBase64 { get; set; } = string.Empty;

    [JsonProperty("exponent_b64")]
    public string ExponentBase64 { get; set; } = string.Empty;

    [JsonProperty("d_b64")]
    public string DBase64 { get; set; } = string.Empty;

    [JsonProperty("p_b64")]
    public string PBase64 { get; set; } = string.Empty;

    [JsonProperty("q_b64")]
    public string QBase64 { get; set; } = string.Empty;

    [JsonProperty("dp_b64")]
    public string DPBase64 { get; set; } = string.Empty;

    [JsonProperty("dq_b64")]
    public string DQBase64 { get; set; } = string.Empty;

    [JsonProperty("inverse_q_b64")]
    public string InverseQBase64 { get; set; } = string.Empty;

    [JsonProperty("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    public ModApiPublicKey ToPublicKey()
    {
        return new ModApiPublicKey
        {
            Algorithm = Algorithm,
            ModulusBase64 = ModulusBase64,
            ExponentBase64 = ExponentBase64,
            Fingerprint = Fingerprint,
        };
    }

    public RSAParameters ToParameters()
    {
        return new RSAParameters
        {
            Modulus = Convert.FromBase64String(ModulusBase64),
            Exponent = Convert.FromBase64String(ExponentBase64),
            D = Convert.FromBase64String(DBase64),
            P = Convert.FromBase64String(PBase64),
            Q = Convert.FromBase64String(QBase64),
            DP = Convert.FromBase64String(DPBase64),
            DQ = Convert.FromBase64String(DQBase64),
            InverseQ = Convert.FromBase64String(InverseQBase64),
        };
    }

    public static ModApiKeyMaterial FromParameters(RSAParameters parameters)
    {
        return new ModApiKeyMaterial
        {
            ModulusBase64 = Convert.ToBase64String(parameters.Modulus ?? Array.Empty<byte>()),
            ExponentBase64 = Convert.ToBase64String(parameters.Exponent ?? Array.Empty<byte>()),
            DBase64 = Convert.ToBase64String(parameters.D ?? Array.Empty<byte>()),
            PBase64 = Convert.ToBase64String(parameters.P ?? Array.Empty<byte>()),
            QBase64 = Convert.ToBase64String(parameters.Q ?? Array.Empty<byte>()),
            DPBase64 = Convert.ToBase64String(parameters.DP ?? Array.Empty<byte>()),
            DQBase64 = Convert.ToBase64String(parameters.DQ ?? Array.Empty<byte>()),
            InverseQBase64 = Convert.ToBase64String(parameters.InverseQ ?? Array.Empty<byte>()),
            Fingerprint = ComputeFingerprint(parameters),
        };
    }

    private static string ComputeFingerprint(RSAParameters parameters)
    {
        using var sha256 = SHA256.Create();
        var modulus = parameters.Modulus ?? Array.Empty<byte>();
        var exponent = parameters.Exponent ?? Array.Empty<byte>();
        var combined = new byte[modulus.Length + exponent.Length];
        Buffer.BlockCopy(modulus, 0, combined, 0, modulus.Length);
        Buffer.BlockCopy(exponent, 0, combined, modulus.Length, exponent.Length);
        return Convert.ToBase64String(sha256.ComputeHash(combined));
    }
}

internal sealed class ModApiPublicKey
{
    [JsonProperty("algorithm")]
    public string Algorithm { get; set; } = "rsa-pkcs1-sha256";

    [JsonProperty("modulus_b64")]
    public string ModulusBase64 { get; set; } = string.Empty;

    [JsonProperty("exponent_b64")]
    public string ExponentBase64 { get; set; } = string.Empty;

    [JsonProperty("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;
}
