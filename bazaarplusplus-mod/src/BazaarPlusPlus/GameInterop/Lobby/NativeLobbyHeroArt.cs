#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.AssetLoading;
using UnityEngine;
using UnityEngine.U2D;

namespace BazaarPlusPlus.GameInterop.Lobby;

internal sealed record NativeLobbyHeroArt(
    IReadOnlyDictionary<EHero, Sprite> Portraits,
    Sprite Selected,
    Sprite NamePlate,
    Sprite RandomFrame,
    Sprite RandomCheck,
    Sprite? Backdrop,
    Material? LabelPreset
)
{
    private const string PortraitPath = "Assets/TheBazaar/Art/MainMenu/HeroSelect/Hero/Button/";
    private const string SelectionPath = "Assets/TheBazaar/Art/UI/Buttons/Heroes/";

    // Tints serialized on the old lobby's gradient, name label and random toggle label.
    internal static readonly Color32 BackdropTint = new(20, 5, 45, 178);
    internal static readonly Color32 SelectedNameInk = new(254, 213, 172, 255);
    internal static readonly Color32 RandomLabelInk = new(235, 158, 80, 255);

    internal static async Task<NativeLobbyHeroArt?> Load(AssetLoader loader, EHero[] heroes)
    {
        var portraits = heroes.Select(hero => Read(loader, PortraitAddress(hero))).ToArray();
        var selected = Read(loader, SelectionPath + "UI_HeroSelected.png");
        var namePlate = Read(loader, SelectionPath + "UI_HeroSelectedBG.png");
        var form = NativeGlobalAssetLoader.LoadByAddressAsync<SpriteAtlas>(
            loader,
            "Assets/TheBazaar/Art/UI/Forms/Form_TAtlas.spriteatlas"
        );
        var common = NativeGlobalAssetLoader.LoadByAddressAsync<SpriteAtlas>(
            loader,
            "Assets/TheBazaar/Art/UI/Common/Common_TAtlas.spriteatlas"
        );

        var labelPreset = NativeGlobalAssetLoader.LoadByAddressAsync<Material>(
            loader,
            "NotoSans_Eyebrow_M"
        );
        await Task.WhenAll(
            portraits
                .Cast<Task>()
                .Concat(new Task[] { selected, namePlate, form, common, labelPreset })
        );
        var randomFrame = form.Result?.GetSprite("Form_Checkbox_Frame_TUI");
        var randomCheck = form.Result?.GetSprite("Form_Checkbox_Check_TUI");
        var backdrop = common.Result?.GetSprite("Gradient_Left_Tall_TUI");
        if (
            portraits.All(portrait => portrait.Result == null)
            || selected.Result == null
            || namePlate.Result == null
            || randomFrame == null
            || randomCheck == null
        )
        {
            UnityEngine.Object.Destroy(randomFrame);
            UnityEngine.Object.Destroy(randomCheck);
            UnityEngine.Object.Destroy(backdrop);
            return null;
        }

        return new NativeLobbyHeroArt(
            heroes
                .Select((hero, index) => (hero, sprite: portraits[index].Result))
                .Where(item => item.sprite != null)
                .ToDictionary(item => item.hero, item => item.sprite!),
            selected.Result!,
            namePlate.Result!,
            randomFrame,
            randomCheck,
            backdrop,
            labelPreset.Result
        );
    }

    // Keep the localized font atlas, gradient scale and weights. Only transfer the game's style.
    internal Material? CloneLabelMaterial(Material fontMaterial)
    {
        if (LabelPreset == null || fontMaterial == null)
            return null;

        var material = new Material(fontMaterial);
        foreach (
            var property in new[]
            {
                "_FaceDilate",
                "_OutlineWidth",
                "_OutlineSoftness",
                "_UnderlayDilate",
                "_UnderlayOffsetX",
                "_UnderlayOffsetY",
                "_UnderlaySoftness",
            }
        )
        {
            if (LabelPreset.HasProperty(property) && material.HasProperty(property))
                material.SetFloat(property, LabelPreset.GetFloat(property));
        }

        foreach (var property in new[] { "_OutlineColor", "_UnderlayColor" })
        {
            if (LabelPreset.HasProperty(property) && material.HasProperty(property))
                material.SetColor(property, LabelPreset.GetColor(property));
        }

        if (LabelPreset.IsKeywordEnabled("UNDERLAY_ON"))
            material.EnableKeyword("UNDERLAY_ON");
        return material;
    }

    private static Task<Sprite?> Read(AssetLoader loader, string address) =>
        NativeGlobalAssetLoader.LoadByAddressAsync<Sprite>(loader, address);

    private static string PortraitAddress(EHero hero) =>
        hero switch
        {
            EHero.Karnok => "karnok_icon",
            EHero.Hero8 => "thedragons_icon",
            _ => PortraitPath + hero + ".png",
        };
}
