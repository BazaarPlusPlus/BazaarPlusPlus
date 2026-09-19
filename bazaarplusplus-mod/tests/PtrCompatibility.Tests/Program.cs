// Guards the source-level premises of the online/PTR dual-version seams
// (docs/ARCHITECTURE.md, "Game Build Channel And PTR Isolation"). The decompiled trees are
// gitignored local artifacts, so this is a LOCAL-ONLY gate: a missing tree is skipped
// with a notice, never a failure. Rerun after every `just mod::decompile <online|ptr> all`
// refresh — a failure here means a seam premise drifted.
#nullable enable
var repoRoot = FindRepoRoot();
var decompiledSourceRoot =
    Environment.GetEnvironmentVariable("BPP_DECOMPILED_SOURCE_ROOT") ?? repoRoot;
var failures = new List<string>();

CheckTree(
    "online (decompiled/)",
    Path.Combine(decompiledSourceRoot, "decompiled"),
    netMessageProcessor =>
    {
        // NetMessageDispatchSeam premise: online has no private Receive funnel, the
        // public one-arg ReceiveOrQueue is the dispatch point and self-recurses
        // aggregate children. If a refreshed online tree gains the private Receive,
        // online adopted the PTR dispatch shape — the seam then routes there, verify
        // capture end-to-end before shipping.
        Require(
            netMessageProcessor,
            "public void ReceiveOrQueue(INetMessage message)",
            "online dispatch entry point"
        );
        RequireAbsent(
            netMessageProcessor,
            "void Receive(INetMessage message, bool",
            "online must not have the PTR private Receive funnel (shape key of NetMessageDispatchSeam)"
        );
        Require(
            netMessageProcessor,
            "ReceiveOrQueue(message2);",
            "online aggregate children must recurse through the patched public overload"
        );
    },
    (root, tree) =>
    {
        CheckNativeTooltipSuppressionPremises(root, tree);
        // GameBuildInfoResolver probe premise: ServerOption must stay PTR-only. If it
        // appears in online, the resolver would classify online as Ptr on the probe
        // side and (per the disagreement rule) pause uploads — revisit the resolver.
        RequireAbsent(
            ReadTreeFile(root, tree, "TheBazaar", "Config.cs"),
            "class ServerOption",
            "GameBuildInfoResolver probe premise: ServerOption is PTR-only"
        );
    }
);

CheckTree(
    "PTR (decompiled-vptr/)",
    Path.Combine(decompiledSourceRoot, "decompiled-vptr"),
    netMessageProcessor =>
    {
        Require(
            netMessageProcessor,
            "public void ReceiveOrQueue(INetMessage message)",
            "PTR public one-arg overload (seam fallback target)"
        );
        Require(
            netMessageProcessor,
            "private void Receive(INetMessage message, bool allowGameSimAfterStateSync)",
            "PTR private Receive funnel (NetMessageDispatchSeam primary target)"
        );
        Require(
            netMessageProcessor,
            "Receive(message2, allowGameSimAfterStateSync);",
            "PTR aggregate children must recurse through the patched private Receive"
        );
    },
    (root, tree) =>
    {
        CheckNativeTooltipSuppressionPremises(root, tree);
        Require(
            ReadTreeFile(root, tree, "TheBazaar", "Config.cs"),
            "class ServerOption",
            "GameBuildInfoResolver probe premise: PTR Config declares ServerOption"
        );
    }
);

if (failures.Count > 0)
{
    Console.Error.WriteLine("PTR compatibility premise failures:");
    foreach (var failure in failures)
        Console.Error.WriteLine($"  - {failure}");
    throw new InvalidOperationException($"{failures.Count} PTR compatibility premise(s) drifted.");
}

Console.WriteLine("PTR compatibility checks passed.");
return;

void CheckNativeTooltipSuppressionPremises(string root, string tree)
{
    var parent = ReadTreeFile(root, tree, "TheBazaar.UI.Tooltips", "TooltipParentComponent.cs");
    Require(
        parent,
        "public async void ShowCardTooltipController(",
        "native primary tooltip high-level gate"
    );
    Require(
        parent,
        "public async Task ShowSecondaryCardTooltipController(",
        "native secondary tooltip high-level gate"
    );
    Require(
        parent,
        "public async void ShowAuxiliaryTooltipController(",
        "native auxiliary tooltip high-level gate"
    );
    Require(
        parent,
        "CardTooltipController.ShowTooltipController(",
        "primary high-level show reaches the low-level late-show seam"
    );
    Require(
        parent,
        "SecondaryCardTooltipController.ShowTooltipController(",
        "secondary high-level show reaches the low-level late-show seam"
    );
    Require(
        parent,
        "AuxiliaryTooltipController.ShowAuxiliaryTooltipController(",
        "auxiliary high-level show reaches the low-level late-show seam"
    );

    var card = ReadTreeFile(root, tree, "TheBazaar.UI.Tooltips", "CardTooltipController.cs");
    Require(
        card,
        "public void ShowTooltipController(",
        "native card tooltip low-level late-show gate"
    );
    Require(
        card,
        "public CanvasHiderComponent CanvasHiderComponent",
        "authoritative card-tooltip concealment seam"
    );

    var auxiliary = ReadTreeFile(
        root,
        tree,
        "TheBazaar.UI.Tooltips",
        "AuxiliaryTooltipController.cs"
    );
    Require(auxiliary, "private GameObject auxParent;", "complete auxiliary-tooltip visual gate");
    Require(
        auxiliary,
        "public void ShowAuxiliaryTooltipController(",
        "native auxiliary low-level late-show gate"
    );

    var canvasHider = ReadTreeFile(
        root,
        tree,
        "TheBazaar.SequenceFramework",
        "CanvasHiderComponent.cs"
    );
    Require(canvasHider, "public void SetVisibility(bool visible)", "card conceal command");
    Require(canvasHider, "public bool IsVisible()", "card visibility audit");
}

void CheckTree(
    string label,
    string treeRoot,
    Action<string> netMessageProcessorChecks,
    Action<string, string> otherChecks
)
{
    if (!Directory.Exists(Path.Combine(treeRoot, "TheBazaarRuntime")))
    {
        Console.WriteLine(
            $"[skip] {label} tree absent — run the matching `just mod::decompile` command to enable these checks."
        );
        return;
    }

    _currentTreeLabel = label;
    netMessageProcessorChecks(ReadTreeFile(treeRoot, label, "TheBazaar", "NetMessageProcessor.cs"));
    otherChecks(treeRoot, label);
}

string ReadTreeFile(string treeRoot, string label, string namespaceDir, string fileName)
{
    var path = Path.Combine(treeRoot, "TheBazaarRuntime", namespaceDir, fileName);
    if (!File.Exists(path))
    {
        failures.Add($"{label}: required decompiled file missing: {path}");
        return string.Empty;
    }

    return File.ReadAllText(path);
}

void Require(string text, string literal, string why)
{
    if (text.Length > 0 && !text.Contains(literal, StringComparison.Ordinal))
        failures.Add($"{_currentTreeLabel}: expected \"{literal}\" ({why})");
}

void RequireAbsent(string text, string literal, string why)
{
    if (text.Contains(literal, StringComparison.Ordinal))
        failures.Add($"{_currentTreeLabel}: expected NO \"{literal}\" ({why})");
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current != null)
    {
        if (File.Exists(Path.Combine(current.FullName, "mod.just")))
            return current.FullName;
        current = current.Parent;
    }

    throw new InvalidOperationException("Repository root not found from test base directory.");
}

public partial class Program
{
    private static string _currentTreeLabel = string.Empty;
}
