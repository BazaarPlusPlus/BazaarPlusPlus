#nullable enable
namespace BazaarPlusPlus.Core.Events;

/// <summary>
/// Upload validation committed a new seal job after rejecting a local Bundle file. Published
/// on the upload worker after the transaction commits; handlers should only schedule work.
/// </summary>
public sealed class BundleSealJobRecreated
{
    public string RunId { get; set; } = string.Empty;
}
