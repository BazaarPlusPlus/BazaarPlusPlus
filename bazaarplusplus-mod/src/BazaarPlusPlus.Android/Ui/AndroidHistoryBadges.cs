using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// History's two BPP badge atlases, the desktop's own PNGs linked into this assembly: the
/// battle win and loss badges, and the run state badges (completed, abandoned, active). They
/// decode through <c>ImageConversion.LoadImage</c> on the main thread; a failure is logged and
/// the rows show their text fallback instead.
/// </summary>
internal sealed class AndroidHistoryBadges : IDisposable
{
    private readonly List<UnityEngine.Object> _owned = new();
    private Sprite[] _runStates = [];

    private AndroidHistoryBadges() { }

    public Sprite? Win { get; private set; }

    public Sprite? Loss { get; private set; }

    public Sprite? RunState(int index) =>
        index >= 0 && index < _runStates.Length ? _runStates[index] : null;

    public static AndroidHistoryBadges? TryLoad()
    {
        var badges = new AndroidHistoryBadges();
        try
        {
            var results = badges.LoadAtlas("battle-result-badges.png", 2);
            badges.Win = results[0];
            badges.Loss = results[1];
            badges._runStates = badges.LoadAtlas("run-state-badges.png", 3);
            return badges;
        }
        catch (Exception exception)
        {
            badges.Dispose();
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.history.badges_failed",
                    storm: ["reason_code"]
                ),
                exception,
                ("reason_code", exception.GetType().Name)
            );
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var owned in _owned)
        {
            if (owned != null)
                UnityEngine.Object.Destroy(owned);
        }
        _owned.Clear();
        _runStates = [];
        Win = Loss = null;
    }

    private Sprite[] LoadAtlas(string name, int count)
    {
        using var stream =
            typeof(AndroidHistoryBadges).Assembly.GetManifestResourceStream(
                $"BazaarPlusPlus.Android.Resources.HistoryPanel.{name}"
            ) ?? throw new InvalidOperationException($"History artwork is missing: {name}");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        _owned.Add(texture);
        if (!ImageConversion.LoadImage(texture, new Il2CppStructArray<byte>(bytes.ToArray())))
            throw new InvalidOperationException($"History artwork could not be decoded: {name}");
        var width = texture.width / (float)count;
        var sprites = new Sprite[count];
        for (var index = 0; index < count; index++)
        {
            sprites[index] = Sprite.Create(
                texture,
                new Rect(index * width, 0, width, texture.height),
                new Vector2(.5f, .5f)
            );
            _owned.Add(sprites[index]);
        }
        return sprites;
    }
}
