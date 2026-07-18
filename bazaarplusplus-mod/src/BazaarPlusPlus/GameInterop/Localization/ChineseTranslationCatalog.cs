#nullable enable
using System.Security.Cryptography;
using System.Text;
using BazaarGameShared.Domain.Core;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Microsoft.Data.Sqlite;
using TheBazaar.DataManagement;

namespace BazaarPlusPlus.GameInterop.Localization;

/// <summary>
/// Reads official Simplified Chinese strings without changing the game's active locale.
/// The game downloads every supported locale into a separate SQLite file at startup;
/// LocalizationService itself keeps only the active locale open.
/// </summary>
internal static class ChineseTranslationCatalog
{
    private const string Locale = "zh-CN";
    private static readonly object Sync = new();
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.Ordinal);
    private static SqliteConnection? _connection;
    private static string? _databasePath;
    private static long _databaseLength;
    private static long _databaseWriteTicks;
    private static readonly OperationalHealthTracker<string, BilingualLogReasonCode> Health = new();
    private static bool _readyReported;

    internal static string? TryResolve(TLocalizableText? text)
    {
        var source = text?.Text;
        if (string.IsNullOrWhiteSpace(source))
            return null;

        lock (Sync)
        {
            try
            {
                if (!EnsureConnection())
                    return null;

                if (Cache.TryGetValue(source!, out var cached))
                    return cached;

                using var command = _connection!.CreateCommand();
                command.CommandText = "SELECT text FROM translation WHERE hash = $hash LIMIT 1";
                command.Parameters.AddWithValue("$hash", ComputeHash(source!));
                var translation = command.ExecuteScalar() as string;
                if (string.IsNullOrWhiteSpace(translation))
                    translation = null;
                Cache[source!] = translation;
                ReportSuccess();
                return translation;
            }
            catch (Exception ex)
            {
                ReportFailure(BilingualLogReasonCode.QueryException, ex);
                CloseConnection();
                return null;
            }
        }
    }

    internal static void Reset()
    {
        lock (Sync)
        {
            CloseConnection();
            Health.Reset();
            _readyReported = false;
        }
    }

    private static bool EnsureConnection()
    {
        var path = Path.Combine(DataManifestActions.Translations.GetCachePath(), $"{Locale}.bytes");
        if (!File.Exists(path))
        {
            ReportFailure(BilingualLogReasonCode.DatabaseUnavailable);
            return false;
        }

        var info = new FileInfo(path);
        var writeTicks = info.LastWriteTimeUtc.Ticks;
        if (
            _connection != null
            && string.Equals(_databasePath, path, StringComparison.Ordinal)
            && _databaseLength == info.Length
            && _databaseWriteTicks == writeTicks
        )
            return true;

        CloseConnection();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        _databasePath = path;
        _databaseLength = info.Length;
        _databaseWriteTicks = writeTicks;
        return true;
    }

    private static string ComputeHash(string text)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var value in hash)
            builder.Append(value.ToString("x2"));
        return builder.ToString();
    }

    private static void CloseConnection()
    {
        _connection?.Dispose();
        _connection = null;
        _databasePath = null;
        _databaseLength = 0;
        _databaseWriteTicks = 0;
        Cache.Clear();
    }

    private static void ReportFailure(
        BilingualLogReasonCode reasonCode,
        Exception? exception = null
    )
    {
        _readyReported = false;
        if (!Health.ObserveFailure(Locale, reasonCode))
            return;

        var fields = new[]
        {
            BilingualItemNamesLogEvents.CatalogDegradedLocale.Bind(Locale),
            BilingualItemNamesLogEvents.CatalogDegradedReasonCode.Bind(reasonCode),
        };
        if (exception == null)
            BppLog.WarnEvent(BilingualItemNamesLogEvents.CatalogDegraded, fields);
        else
            BppLog.WarnEvent(BilingualItemNamesLogEvents.CatalogDegraded, exception, fields);
    }

    private static void ReportSuccess()
    {
        if (Health.ObserveSuccess(Locale, out var reasonCode))
        {
            _readyReported = true;
            BppLog.RecoverStorm(
                BilingualItemNamesLogEvents.CatalogDegraded,
                BilingualItemNamesLogEvents.CatalogDegradedReasonCode.Bind(reasonCode)
            );
            BppLog.InfoEvent(
                BilingualItemNamesLogEvents.CatalogRecovered,
                BilingualItemNamesLogEvents.CatalogRecoveredLocale.Bind(Locale)
            );
            return;
        }

        if (_readyReported)
            return;
        _readyReported = true;
        BppLog.DebugEvent(
            BilingualItemNamesLogEvents.CatalogLoaded,
            () => [BilingualItemNamesLogEvents.CatalogLoadedLocale.Bind(Locale)]
        );
    }
}
