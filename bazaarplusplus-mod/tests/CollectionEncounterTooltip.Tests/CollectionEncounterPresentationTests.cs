using Xunit;
using Disposition = BazaarPlusPlus.Game.EventPreview.EncounterEventDetailResolver.ChoicePresentation;

namespace EncounterTooltip.Tests;

public class EncounterPresentationTests
{
    // Wishing Fountain: 21 "Make a Wish" price tiers, limit 3 — the 18 beyond-limit
    // same-title variants hide instead of rendering as a dimmed wall.
    [Fact]
    public void Beyond_limit_steps_with_repeated_titles_hide()
    {
        var candidates = new List<(string, bool)>();
        for (var i = 0; i < 21; i++)
            candidates.Add(("Make a Wish", true));

        var result = EncounterEventDetailResolver.ResolvePresentation(candidates, 3);

        Assert.Equal(3, CountOf(result, Disposition.Presented));
        Assert.Equal(0, CountOf(result, Disposition.Dimmed));
        Assert.Equal(18, CountOf(result, Disposition.Hidden));
    }

    [Fact]
    public void Beyond_limit_steps_with_unique_titles_stay_dimmed()
    {
        var candidates = new List<(string, bool)>
        {
            ("Option A", true),
            ("Option B", true),
            ("Option C", true),
        };

        var result = EncounterEventDetailResolver.ResolvePresentation(candidates, 2);

        Assert.Equal(
            new[] { Disposition.Presented, Disposition.Presented, Disposition.Dimmed },
            result
        );
    }

    [Fact]
    public void Prerequisite_unmet_steps_always_show_dimmed_even_with_repeated_titles()
    {
        var candidates = new List<(string, bool)>
        {
            ("Make a Wish", true),
            ("Make a Wish", false),
            ("Clear the Way", false),
        };

        var result = EncounterEventDetailResolver.ResolvePresentation(candidates, 3);

        Assert.Equal(
            new[] { Disposition.Presented, Disposition.Dimmed, Disposition.Dimmed },
            result
        );
    }

    private static int CountOf(Disposition[] result, Disposition kind)
    {
        var count = 0;
        foreach (var entry in result)
            if (entry == kind)
                count++;
        return count;
    }
}
