#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace BazaarPlusPlus.Game.ModApi;

internal sealed class ModApiClientStateStore
{
    private readonly string _statePath;
    private readonly object _sync = new();
    private bool _stateLoaded;
    private string? _cachedClientId;
    private string? _cachedBoundPlayerAccountId;

    public ModApiClientStateStore(string statePath)
    {
        if (string.IsNullOrWhiteSpace(statePath))
            throw new ArgumentException("State path is required.", nameof(statePath));

        _statePath = statePath;
    }

    public string? TryGetClientId()
    {
        lock (_sync)
        {
            EnsureStateLoaded();
            return _cachedClientId;
        }
    }

    public void SaveClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Client id is required.", nameof(clientId));

        lock (_sync)
        {
            EnsureStateLoaded();
            _cachedClientId = clientId.Trim();
            PersistState();
        }
    }

    public void ClearClientId()
    {
        lock (_sync)
        {
            EnsureStateLoaded();
            if (string.IsNullOrWhiteSpace(_cachedClientId))
                return;

            _cachedClientId = null;
            PersistState();
        }
    }

    public string? TryGetBoundPlayerAccountId()
    {
        lock (_sync)
        {
            EnsureStateLoaded();
            return _cachedBoundPlayerAccountId;
        }
    }

    public void SaveBoundPlayerAccountId(string playerAccountId)
    {
        if (string.IsNullOrWhiteSpace(playerAccountId))
            throw new ArgumentException("Player account id is required.", nameof(playerAccountId));

        lock (_sync)
        {
            EnsureStateLoaded();
            _cachedBoundPlayerAccountId = playerAccountId.Trim();
            PersistState();
        }
    }

    private void EnsureStateLoaded()
    {
        if (_stateLoaded)
            return;

        var state = ReadStateFromDisk();
        _cachedClientId = state.ClientId;
        _cachedBoundPlayerAccountId = state.BoundPlayerAccountId;
        _stateLoaded = true;
    }

    private ModApiClientStatePayload ReadStateFromDisk()
    {
        if (!File.Exists(_statePath))
            return new ModApiClientStatePayload();

        try
        {
            var payload = JsonConvert.DeserializeObject<ModApiClientState>(
                File.ReadAllText(_statePath)
            );
            return new ModApiClientStatePayload(
                payload?.ClientId
                    ?? payload?.ClientIds?.Values.FirstOrDefault(static value =>
                        !string.IsNullOrWhiteSpace(value)
                    ),
                payload?.BoundPlayerAccountId
                    ?? payload?.BoundPlayerAccountIds?.Values.FirstOrDefault(static value =>
                        !string.IsNullOrWhiteSpace(value)
                    )
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "ModApiClientStateStore",
                $"Failed to read client state from {_statePath}: {ex.GetType().Name} - {ex.Message}. Resetting to empty state."
            );
            return new ModApiClientStatePayload();
        }
    }

    private void PersistState()
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(
            _statePath,
            JsonConvert.SerializeObject(
                new ModApiClientState
                {
                    ClientId = _cachedClientId,
                    BoundPlayerAccountId = _cachedBoundPlayerAccountId,
                },
                Formatting.Indented
            )
        );
    }

    private sealed class ModApiClientState
    {
        [JsonProperty("client_id")]
        public string? ClientId { get; set; }

        [JsonProperty("bound_player_account_id")]
        public string? BoundPlayerAccountId { get; set; }

        [JsonProperty("client_ids")]
        public Dictionary<string, string>? ClientIds { get; set; }

        [JsonProperty("bound_player_account_ids")]
        public Dictionary<string, string>? BoundPlayerAccountIds { get; set; }
    }

    private sealed class ModApiClientStatePayload
    {
        public ModApiClientStatePayload()
        {
            ClientId = null;
            BoundPlayerAccountId = null;
        }

        public ModApiClientStatePayload(string? clientId, string? boundPlayerAccountId)
        {
            ClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
            BoundPlayerAccountId = string.IsNullOrWhiteSpace(boundPlayerAccountId)
                ? null
                : boundPlayerAccountId.Trim();
        }

        public string? ClientId { get; }

        public string? BoundPlayerAccountId { get; }
    }
}
