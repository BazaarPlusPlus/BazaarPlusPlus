#nullable enable

internal sealed class ContractlessRunArtifact
{
    public List<ContractlessRunArtifactBattle> Battles { get; set; } = [];
}

internal sealed class ContractlessRunArtifactBattle
{
    public string BattleId { get; set; } = string.Empty;

    public ContractlessReplayPayload? ReplayPayload { get; set; }
}

internal sealed class ContractlessReplayPayload
{
    public byte[] SpawnMessageBytes { get; set; } = [];

    public byte[] CombatMessageBytes { get; set; } = [];
}
