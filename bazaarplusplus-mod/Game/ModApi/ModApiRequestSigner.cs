#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace BazaarPlusPlus.Game.ModApi;

internal sealed class ModApiRequestSigner
{
    private readonly ModApiKeyStore _keyStore;

    public ModApiRequestSigner(ModApiKeyStore keyStore)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
    }

    public HttpRequestMessage CreateSignedUploadRequest(
        string uploadEndpoint,
        string json,
        string clientId,
        string installId,
        string runId
    )
    {
        var request = CreateSignedRequest(
            HttpMethod.Post,
            uploadEndpoint,
            json,
            clientId,
            installId,
            "application/json"
        );
        request.Headers.TryAddWithoutValidation("X-BPP-Run-Id", runId);
        return request;
    }

    public HttpRequestMessage CreateSignedRequest(
        HttpMethod method,
        string endpoint,
        string? body,
        string clientId,
        string installId,
        string? contentType = null
    )
    {
        return CreateSignedRequest(
            method,
            endpoint,
            body,
            clientId,
            installId,
            contentType,
            extraHeaders: null
        );
    }

    public HttpRequestMessage CreateSignedRequest(
        HttpMethod method,
        string endpoint,
        string? body,
        string clientId,
        string installId,
        string? contentType,
        IReadOnlyDictionary<string, string?>? extraHeaders
    )
    {
        if (method == null)
            throw new ArgumentNullException(nameof(method));
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint is required.", nameof(endpoint));

        var timestamp = DateTimeOffset.UtcNow.ToString("o");
        var bodyText = body ?? string.Empty;
        var bodyHash = ComputeBodyHash(bodyText);
        var canonical = BuildCanonicalRequest(
            method.Method,
            new Uri(endpoint).AbsolutePath,
            clientId,
            installId,
            timestamp,
            bodyHash
        );
        var signature = _keyStore.Sign(canonical);
        var pluginVersion = BppPluginVersion.Current;

        var request = new HttpRequestMessage(method, endpoint);
        if (body != null)
            request.Content = new StringContent(
                body,
                Encoding.UTF8,
                contentType ?? "application/json"
            );
        request.Headers.TryAddWithoutValidation("X-BPP-Client-Id", clientId);
        request.Headers.TryAddWithoutValidation("X-BPP-Install-Id", installId);
        request.Headers.TryAddWithoutValidation("X-BPP-Plugin-Version", pluginVersion);
        request.Headers.TryAddWithoutValidation("X-BPP-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-BPP-Content-SHA256", bodyHash);
        request.Headers.TryAddWithoutValidation("X-BPP-Signature-Alg", "rsa-pkcs1-sha256");
        request.Headers.TryAddWithoutValidation("X-BPP-Signature", signature);
        if (extraHeaders != null)
        {
            foreach (var entry in extraHeaders)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                    continue;

                request.Headers.TryAddWithoutValidation(entry.Key, entry.Value);
            }
        }

        return request;
    }

    internal static string BuildCanonicalRequest(
        string method,
        string absolutePath,
        string clientId,
        string installId,
        string timestamp,
        string bodyHash
    )
    {
        return string.Join(
            "\n",
            new[]
            {
                method.Trim().ToUpperInvariant(),
                NormalizePath(absolutePath),
                clientId.Trim(),
                installId.Trim(),
                timestamp.Trim(),
                bodyHash.Trim(),
            }
        );
    }

    internal static string ComputeBodyHash(string json)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(json)));
    }

    private static string NormalizePath(string absolutePath)
    {
        return string.IsNullOrWhiteSpace(absolutePath) ? "/" : absolutePath.Trim();
    }
}
