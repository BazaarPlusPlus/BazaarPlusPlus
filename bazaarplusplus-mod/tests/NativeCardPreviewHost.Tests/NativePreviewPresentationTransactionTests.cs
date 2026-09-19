using BazaarPlusPlus.GameInterop.CardPreview;
using Xunit;

namespace NativeCardPreviewHost.Tests;

public sealed class NativePreviewPresentationTransactionTests
{
    [Fact]
    public void Successful_show_reveals_supplemental_visuals_after_native_show()
    {
        var calls = new List<string>();

        var result = NativePreviewPresentationTransaction.Apply(
            show: true,
            revealSupplementalVisualsOnSuccess: true,
            ApplyNative,
            () => calls.Add("reveal"),
            () => calls.Add("conceal")
        );

        Assert.Equal(NativePreviewActionStatus.Applied, result.Status);
        Assert.Equal(["conceal", "native", "reveal"], calls);

        NativePreviewActionResult ApplyNative()
        {
            calls.Add("native");
            return Applied();
        }
    }

    [Fact]
    public void Artwork_only_show_keeps_supplemental_visuals_concealed()
    {
        var calls = new List<string>();

        var result = NativePreviewPresentationTransaction.Apply(
            show: true,
            revealSupplementalVisualsOnSuccess: false,
            () =>
            {
                calls.Add("native");
                return Applied();
            },
            () => calls.Add("reveal"),
            () => calls.Add("conceal")
        );

        Assert.Equal(NativePreviewActionStatus.Applied, result.Status);
        Assert.Equal(["conceal", "native"], calls);
    }

    [Fact]
    public void Failed_show_leaves_supplemental_visuals_concealed()
    {
        var calls = new List<string>();
        var failure = new NativeCardPreviewFailure(
            NativeCardPreviewOperation.Show,
            NativeCardPreviewFailureReason.ShowException,
            Guid.NewGuid()
        );

        var result = NativePreviewPresentationTransaction.Apply(
            show: true,
            revealSupplementalVisualsOnSuccess: true,
            () =>
            {
                calls.Add("native");
                return new NativePreviewActionResult(NativePreviewActionStatus.Failed, failure);
            },
            () => calls.Add("reveal"),
            () => calls.Add("conceal")
        );

        Assert.Same(failure, result.Failure);
        Assert.Equal(["conceal", "native"], calls);
    }

    [Fact]
    public void Hide_conceals_supplemental_visuals_even_when_native_hide_fails()
    {
        var calls = new List<string>();
        var failure = new NativeCardPreviewFailure(
            NativeCardPreviewOperation.Show,
            NativeCardPreviewFailureReason.ShowException,
            Guid.NewGuid()
        );

        var result = NativePreviewPresentationTransaction.Apply(
            show: false,
            revealSupplementalVisualsOnSuccess: true,
            () =>
            {
                calls.Add("native");
                return new NativePreviewActionResult(NativePreviewActionStatus.Failed, failure);
            },
            () => calls.Add("reveal"),
            () => calls.Add("conceal")
        );

        Assert.Same(failure, result.Failure);
        Assert.Equal(["native", "conceal"], calls);
    }

    private static NativePreviewActionResult Applied() =>
        new(NativePreviewActionStatus.Applied, null);
}
