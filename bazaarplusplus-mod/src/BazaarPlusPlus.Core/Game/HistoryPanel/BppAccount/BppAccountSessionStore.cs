#nullable enable
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Game.HistoryPanel.BppAccount;

public sealed class BppStoredAccountSession
{
    public BppStoredAccountSession(
        string gameAccountId,
        string deviceToken,
        string userName,
        string emailMasked
    )
    {
        GameAccountId = gameAccountId ?? throw new ArgumentNullException(nameof(gameAccountId));
        DeviceToken = deviceToken ?? throw new ArgumentNullException(nameof(deviceToken));
        UserName = userName ?? throw new ArgumentNullException(nameof(userName));
        EmailMasked = emailMasked ?? throw new ArgumentNullException(nameof(emailMasked));
    }

    public string GameAccountId { get; }
    public string DeviceToken { get; }
    public string UserName { get; }
    public string EmailMasked { get; }
}

/// <summary>Atomically persists the bound BPP Account device token in the Data Root.</summary>
public sealed class BppAccountSessionStore
{
    private const string DeviceTokenPrefix = "bppd_";
    private readonly string _filePath;
    private readonly object _gate = new();

    public BppAccountSessionStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Session file path is required.", nameof(filePath));
        _filePath = filePath;
    }

    public BppStoredAccountSession? TryRead()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_filePath))
                    return null;
                var json = File.ReadAllText(_filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                    return null;
                var obj = JObject.Parse(json);
                var gameAccountId = ReadNonEmpty(obj, "game_account_id");
                var deviceToken = ReadNonEmpty(obj, "device_token");
                var userName = ReadNonEmpty(obj, "user_name");
                var emailMasked = ReadNonEmpty(obj, "email_masked");
                if (
                    gameAccountId == null
                    || deviceToken == null
                    || !deviceToken.StartsWith(DeviceTokenPrefix, StringComparison.Ordinal)
                    || userName == null
                    || emailMasked == null
                )
                    return null;
                return new BppStoredAccountSession(
                    gameAccountId,
                    deviceToken,
                    userName,
                    emailMasked
                );
            }
            catch
            {
                return null;
            }
        }
    }

    public string? TryReadDeviceTokenForAccount(string? gameAccountId)
    {
        if (string.IsNullOrWhiteSpace(gameAccountId))
            return null;
        var stored = TryRead();
        if (stored == null)
            return null;
        return string.Equals(stored.GameAccountId, gameAccountId!.Trim(), StringComparison.Ordinal)
            ? stored.DeviceToken
            : null;
    }

    public void Save(BppStoredAccountSession session)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var payload = new JObject
            {
                ["game_account_id"] = session.GameAccountId,
                ["device_token"] = session.DeviceToken,
                ["user_name"] = session.UserName,
                ["email_masked"] = session.EmailMasked,
            }.ToString(Formatting.None);

            var tempPath = $"{_filePath}.tmp.{Guid.NewGuid():N}";
            try
            {
                File.WriteAllText(tempPath, payload, new UTF8Encoding(false));
                if (File.Exists(_filePath))
                {
                    try
                    {
                        File.Replace(tempPath, _filePath, destinationBackupFileName: null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Delete(_filePath);
                        File.Move(tempPath, _filePath);
                    }
                }
                else
                {
                    File.Move(tempPath, _filePath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch { }
                }
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
            }
            catch { }
        }
    }

    private static string? ReadNonEmpty(JObject source, string name)
    {
        var value =
            source[name]?.Type == JTokenType.String ? source[name]?.Value<string>()?.Trim() : null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
