using System.Text;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.EventPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Domain.Spawning;
using Il2CppTheBazaar;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;

namespace BazaarPlusPlus.Android.Game;

/// <summary>
/// Reads encounter choices, their rewards, merchant quality rules and each package's merchant
/// off-thread, for Core's <see cref="EncounterPreviewModule"/> (<see cref="Module"/>) to resolve
/// on inspection through <see cref="AndroidEncounterPreviewReads"/>. A package names its merchant,
/// which no preview switch hides.
/// </summary>
internal sealed class AndroidEncounterPreview
{
    private IntPtr _source;
    private Task<AndroidEncounterPlans>? _load;
    private AndroidEncounterPlans? _plans;
    private readonly Dictionary<(Guid Package, string Language), string> _packageSummaries = new();
    private static readonly EncounterChoicePlanParser Parser = new(Normalize);
    private static readonly EncounterPreviewCardReader Reader = new(Normalize, Hero);

    public AndroidEncounterPreview(BppSettings settings) =>
        Module = new EncounterPreviewModule(settings, new AndroidEncounterPreviewReads(this));

    /// <summary>The event, encounter-step and hero-level previews over these plans.</summary>
    public EncounterPreviewModule Module { get; }

    /// <summary>The plans of the current static-data source; null until they are read.</summary>
    public AndroidEncounterPlans? Plans => _plans;

    public void Tick()
    {
        // Not behind the Detailed Preview switch: a package's merchant summary has none.
        if (!Data.IsManagerCreated())
            return;
        var manager = Data.GetStatic();
        if (manager == null)
            return;
        // The path is marshalled only for a new manager: each read of the IL2CPP field allocates
        // a managed string. An empty path leaves the source unset, so the next frame retries.
        if (_source != manager.Pointer)
        {
            var path = manager._dbPath;
            if (string.IsNullOrWhiteSpace(path))
                return;
            _source = manager.Pointer;
            _plans = null;
            _packageSummaries.Clear();
            _load = Task.Run(() => Read(path));
        }
        if (_load?.IsCompleted == true)
        {
            if (_load.IsCompletedSuccessfully)
            {
                _plans = _load.Result;
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.encounter_preview.ready"),
                    ("events", _plans.Snapshot.EventCount),
                    ("packages", _plans.PackageMerchants.Count),
                    ("level_ups", _plans.Snapshot.LevelUpCount)
                );
            }
            else
                BppLog.WarnEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.encounter_preview.failed"),
                    _load.Exception!,
                    ("reason_code", "database_read")
                );
            _load = null;
        }
    }

    /// <summary>
    /// The native passive text of <paramref name="card"/> with the merchant that accepts it, as
    /// the desktop's <c>PackageMerchantSummaryTooltipPatch</c> appends it; null for any other
    /// card, or while the plans are not read yet. Main thread only.
    /// </summary>
    public string? RenderPackageMerchant(Card? card, string text)
    {
        if (
            _plans == null
            || card == null
            || !Guid.TryParse(card.TemplateId.ToString(), out var id)
            || !_plans.PackageMerchants.TryGetValue(id, out var merchant)
        )
            return null;

        var key = (id, L.CurrentLanguageCode);
        if (!_packageSummaries.TryGetValue(key, out var summary))
        {
            summary = PackageMerchantSummaryText.Format(
                AndroidCardTemplateText.Title(merchant),
                AndroidCardTemplateText.Description(merchant)
            );
            // A text that did not render yet may render later, so only a real one stays.
            if (string.IsNullOrWhiteSpace(summary))
                return null;
            _packageSummaries[key] = summary;
        }

        var passive = new StringBuilder(text);
        PackageMerchantSummaryText.AppendToPassiveBlock(passive, summary);
        return passive.ToString();
    }

    internal static AndroidEncounterPlans Read(string path)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString()
        );
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Data FROM cards";
        using var reader = command.ExecuteReader();
        var events = new List<EncounterPreviewEventPlan>();
        var templates = new List<EncounterPreviewTemplatePlan>();
        var packageMerchants = new Dictionary<Guid, Guid>();
        while (reader.Read())
        {
            var card = JObject.Parse(Encoding.UTF8.GetString((byte[])reader.GetValue(0)));
            if (Reader.ReadTemplate(card) is not { } template)
                continue;
            templates.Add(template);
            if (Parser.TryParsePackageMerchant(card) is { } packageMerchant)
                packageMerchants[template.TemplateId] = packageMerchant;
            if (
                template.Kind == EncounterPreviewTemplateKind.Event
                && Reader.ReadEvent(card) is { } plan
            )
                events.Add(plan);
        }
        reader.Close();
        command.CommandText = "SELECT Id, Data FROM level_ups";
        using var levelReader = command.ExecuteReader();
        var levelUps = new List<LevelUpPreviewPlan>();
        while (levelReader.Read())
            if (
                Parser.TryParseLevelUp(
                    levelReader.GetInt32(0),
                    JObject.Parse(Encoding.UTF8.GetString((byte[])levelReader.GetValue(1))),
                    Hero
                ) is
                { } levelUp
            )
                levelUps.Add(levelUp);
        return new(new EncounterPreviewSnapshot(events, templates, levelUps), packageMerchants);
    }

    private static CardHero? Hero(string name) =>
        Enum.TryParse<EHero>(name, out var native)
        && AndroidCardFacets.Hero.TryToFacet(native, out var hero)
            ? hero
            : null;

    private static string? Normalize(string kind, string? raw) =>
        kind switch
        {
            "SelectionMethod" => Name<ESpawnSelectionMethod>(raw),
            "Comparison" => Name<EComparisonOperator>(raw),
            "ListComparison" => Name<EListComparisonOperator>(raw),
            "HiddenTag" => Name<EHiddenTag>(raw),
            "Tag" => Name<ECardTag>(raw),
            "Tier" => Name<ETier>(raw),
            "Size" => Name<ECardSize>(raw),
            "CardType" => Name<ECardType>(raw),
            _ => null,
        };

    private static string? Name<T>(string? raw)
        where T : struct, Enum =>
        Enum.TryParse<T>(raw, true, out var value) ? value.ToString() : null;
}

/// <summary>
/// The encounter plans read from one static-data source, and the merchant each package template
/// names.
/// </summary>
internal sealed record AndroidEncounterPlans(
    EncounterPreviewSnapshot Snapshot,
    IReadOnlyDictionary<Guid, Guid> PackageMerchants
);
