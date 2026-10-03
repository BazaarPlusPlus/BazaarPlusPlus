var repoRoot = FindRepoRoot();

var assetLoaderPath = Path.Combine(repoRoot, "decompiled", "TheBazaarRuntime", "AssetLoader.cs");
if (!File.Exists(assetLoaderPath))
{
    throw new InvalidOperationException(
        $"Required decompiled source file is missing: {assetLoaderPath}"
    );
}

var assetLoaderText = File.ReadAllText(assetLoaderPath);
var instantiateUICardAsyncBody = GetRequiredMethodBody(
    assetLoaderPath,
    assetLoaderText,
    "public async Task<GameObject> InstantiateUICardAsync"
);
RequireContains(
    assetLoaderPath,
    instantiateUICardAsyncBody,
    "await ConstructAndInstantiateUICard(cardInstance, parentTransform, token)"
);

var constructAndInstantiateUICardBody = GetRequiredMethodBody(
    assetLoaderPath,
    assetLoaderText,
    "private async Task<GameObject> ConstructAndInstantiateUICard"
);
RequireStatementContains(
    assetLoaderPath,
    constructAndInstantiateUICardBody,
    "assetReference =",
    "SmallCardUIAssetRef"
);
RequireStatementContains(
    assetLoaderPath,
    constructAndInstantiateUICardBody,
    "assetReference =",
    "MediumCardUIAssetRef"
);
RequireStatementContains(
    assetLoaderPath,
    constructAndInstantiateUICardBody,
    "assetReference =",
    "LargeCardUIAssetRef"
);
RequireStatementContains(
    assetLoaderPath,
    constructAndInstantiateUICardBody,
    "assetReference =",
    "SkillUIAssetRef"
);
RequireContains(assetLoaderPath, constructAndInstantiateUICardBody, "component.Resize();");
RequireContains(
    assetLoaderPath,
    constructAndInstantiateUICardBody,
    "await component.SetUp(cardBase, isPremium: false, cardInstance, token);"
);
RequireContains(
    assetLoaderPath,
    constructAndInstantiateUICardBody,
    "selectedItem.transform.SetParent(parentTransform, worldPositionStays: false);"
);
RequireAnyContains(
    assetLoaderPath,
    assetLoaderText,
    [
        "internal async Task<T> LoadAssetAsyncByAddress<T>(string address, bool reportSuccess = false) where T : UnityEngine.Object",
        "internal async Task<T> LoadAssetAsyncByAddress<T>(string address, AssetScope? scope = null) where T : UnityEngine.Object",
    ]
);
RequireAnyContains(
    assetLoaderPath,
    assetLoaderText,
    [
        "internal async Task<T> LoadAssetAsyncByReference<T>(AssetReference assetReference) where T : UnityEngine.Object",
        "internal async Task<T> LoadAssetAsyncByReference<T>(AssetReference assetReference, AssetScope? scope = null) where T : UnityEngine.Object",
    ]
);
RequireAnyContains(
    assetLoaderPath,
    assetLoaderText,
    [
        "internal async Task<GameObject> InstantiateAssetAsyncByReference(AssetReference assetReference)",
        "internal async Task<GameObject> InstantiateAssetAsyncByReference(AssetReference assetReference, AssetScope? scope = null)",
    ]
);

var skinAssetPath = Path.Combine(
    repoRoot,
    "decompiled",
    "TheBazaarRuntime",
    "TheBazaar.Assets.Scripts.ScriptableObjectsScripts",
    "SkinAssetDataSO.cs"
);
if (!File.Exists(skinAssetPath))
{
    throw new InvalidOperationException(
        $"Required decompiled source file is missing: {skinAssetPath}"
    );
}

var skinAssetText = File.ReadAllText(skinAssetPath);
RequireContains(
    skinAssetPath,
    skinAssetText,
    "public AssetReferenceSprite portraitTextureReference;"
);
RequireContains(
    skinAssetPath,
    skinAssetText,
    "public AssetReferenceTexture storePortraitTextureReference;"
);
RequireContains(
    skinAssetPath,
    skinAssetText,
    "if (animatedPortraitPrefabReference != null && animatedPortraitPrefabReference.RuntimeKeyIsValid())"
);
RequireContains(
    skinAssetPath,
    skinAssetText,
    "return await LoadTexture(storePortraitTextureReference);"
);

var cardPreviewBasePath = Path.Combine(
    repoRoot,
    "decompiled",
    "TheBazaarRuntime",
    "TheBazaar.UI",
    "CardPreviewBase.cs"
);
if (!File.Exists(cardPreviewBasePath))
{
    throw new InvalidOperationException(
        $"Required decompiled source file is missing: {cardPreviewBasePath}"
    );
}

var cardPreviewBaseText = File.ReadAllText(cardPreviewBasePath);
RequireContains(
    cardPreviewBasePath,
    cardPreviewBaseText,
    "public async Task SetUp(TCardBase card, bool isPremium, TCardInstance cardInstance, CancellationToken ct"
);
RequireContains(cardPreviewBasePath, cardPreviewBaseText, "public void Show(bool show)");
RequireContains(cardPreviewBasePath, cardPreviewBaseText, "public abstract void Resize();");
RequireContains(cardPreviewBasePath, cardPreviewBaseText, "public void OnHover()");
RequireContains(cardPreviewBasePath, cardPreviewBaseText, "public void OnHoverOut()");
RequireContains(cardPreviewBasePath, cardPreviewBaseText, "protected Card _clientCard;");
RequireContains(cardPreviewBasePath, cardPreviewBaseText, "private CardTooltipData _tooltipData;");

Console.WriteLine("Native asset compatibility checks passed.");

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current != null)
    {
        var hasAgents = File.Exists(Path.Combine(current.FullName, "AGENTS.md"));
        var hasSrc = Directory.Exists(Path.Combine(current.FullName, "src"));
        if (hasAgents && hasSrc)
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException(
        $"Could not locate repository root by walking up from {AppContext.BaseDirectory}; expected AGENTS.md and src directory."
    );
}

static void RequireContains(string path, string text, string requiredText)
{
    if (!text.Contains(requiredText, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Required string missing from {path}: {requiredText}");
    }
}

static void RequireAnyContains(string path, string text, string[] alternatives)
{
    if (alternatives.Any(alternative => text.Contains(alternative, StringComparison.Ordinal)))
        return;

    throw new InvalidOperationException(
        $"None of the supported strings were present in {path}: {string.Join(" | ", alternatives)}"
    );
}

static void RequireStatementContains(string path, string text, params string[] requiredFragments)
{
    if (
        text.Split(';')
            .Any(statement =>
                requiredFragments.All(fragment =>
                    statement.Contains(fragment, StringComparison.Ordinal)
                )
            )
    )
    {
        return;
    }

    throw new InvalidOperationException(
        $"Required statement missing from {path}: {string.Join(" ... ", requiredFragments)}"
    );
}

static string GetRequiredMethodBody(string path, string text, string methodSignature)
{
    var signatureIndex = text.IndexOf(methodSignature, StringComparison.Ordinal);
    if (signatureIndex < 0)
    {
        throw new InvalidOperationException(
            $"Required method signature missing from {path}: {methodSignature}"
        );
    }

    var bodyStart = text.IndexOf('{', signatureIndex);
    if (bodyStart < 0)
    {
        throw new InvalidOperationException(
            $"Required method body start missing from {path}: {methodSignature}"
        );
    }

    var depth = 0;
    for (var i = bodyStart; i < text.Length; i++)
    {
        if (text[i] == '{')
        {
            depth++;
        }
        else if (text[i] == '}')
        {
            depth--;
            if (depth == 0)
            {
                return text.Substring(bodyStart, i - bodyStart + 1);
            }
        }
    }

    throw new InvalidOperationException(
        $"Required method body end missing from {path}: {methodSignature}"
    );
}
