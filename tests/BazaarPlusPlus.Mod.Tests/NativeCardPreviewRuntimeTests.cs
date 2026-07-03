using System.Reflection;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.GameInterop.CardPreview;
using Xunit;

namespace BazaarPlusPlus.Mod.Tests;

public sealed class NativeCardPreviewRuntimeTests
{
    [Fact]
    public void BuildSetUpArguments_supports_current_four_parameter_signature()
    {
        var method = typeof(FourParameterCardPreview).GetMethod(
            nameof(FourParameterCardPreview.SetUp)
        );
        var template = new TCardItem { Id = Guid.NewGuid(), Size = ECardSize.Small };
        var instance = new TCardInstanceItem
        {
            TemplateId = template.Id,
            TemplateVersion = string.Empty,
            InstanceId = "test-instance",
            Tier = ETier.Bronze,
            Attributes = new Dictionary<ECardAttributeType, int>(),
        };

        var args = NativeCardPreviewRuntime.BuildSetUpArgumentsForTest(method!, template, instance);

        Assert.Equal(4, args.Length);
        Assert.Same(template, args[0]);
        Assert.False((bool)args[1]);
        Assert.Same(instance, args[2]);
        Assert.Equal(CancellationToken.None, args[3]);
    }

    [Fact]
    public void BuildSetUpArguments_keeps_legacy_three_parameter_signature()
    {
        var method = typeof(ThreeParameterCardPreview).GetMethod(
            nameof(ThreeParameterCardPreview.SetUp)
        );
        var template = new TCardItem { Id = Guid.NewGuid(), Size = ECardSize.Small };
        var instance = new TCardInstanceItem
        {
            TemplateId = template.Id,
            TemplateVersion = string.Empty,
            InstanceId = "test-instance",
            Tier = ETier.Bronze,
            Attributes = new Dictionary<ECardAttributeType, int>(),
        };

        var args = NativeCardPreviewRuntime.BuildSetUpArgumentsForTest(method!, template, instance);

        Assert.Equal(3, args.Length);
        Assert.Same(template, args[0]);
        Assert.False((bool)args[1]);
        Assert.Same(instance, args[2]);
    }

    [Fact]
    public void BuildSetUpArguments_rejects_unknown_signature()
    {
        var method = typeof(UnknownCardPreview).GetMethod(nameof(UnknownCardPreview.SetUp));
        var template = new TCardItem { Id = Guid.NewGuid(), Size = ECardSize.Small };
        var instance = new TCardInstanceItem
        {
            TemplateId = template.Id,
            TemplateVersion = string.Empty,
            InstanceId = "test-instance",
            Tier = ETier.Bronze,
            Attributes = new Dictionary<ECardAttributeType, int>(),
        };

        Assert.Throws<InvalidOperationException>(() =>
            NativeCardPreviewRuntime.BuildSetUpArgumentsForTest(method!, template, instance)
        );
    }

    private sealed class FourParameterCardPreview
    {
        public Task SetUp(
            TCardBase template,
            bool isPremium,
            TCardInstance instance,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    private sealed class ThreeParameterCardPreview
    {
        public Task SetUp(TCardBase template, bool isPremium, TCardInstance instance) =>
            Task.CompletedTask;
    }

    private sealed class UnknownCardPreview
    {
        public Task SetUp(TCardBase template, string unsupported, TCardInstance instance) =>
            Task.CompletedTask;
    }
}
