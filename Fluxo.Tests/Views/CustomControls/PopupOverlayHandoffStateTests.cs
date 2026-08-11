using Xunit;

namespace Fluxo.Tests.Views.CustomControls;

public sealed class PopupOverlayHandoffStateTests
{
    [Fact]
    public void PopupOverlayHandoffState_OnPopupShown_FirstShow_ReturnsShowOverlayAndCountIsOne()
    {
        var state = new PopupOverlayHandoffState();

        var action = state.OnPopupShown();

        Assert.Equal(PopupOverlayHostAction.ShowOverlay, action);
        Assert.Equal(1, state.ActivePopupCount);
    }

    [Fact]
    public void PopupOverlayHandoffState_OnPopupHidden_LastHideWithoutHandoff_ReturnsHideOverlayAndCountIsZero()
    {
        var state = new PopupOverlayHandoffState();
        state.OnPopupShown();

        var action = state.OnPopupHidden();

        Assert.Equal(PopupOverlayHostAction.HideOverlay, action);
        Assert.Equal(0, state.ActivePopupCount);
    }

    [Fact]
    public void PopupOverlayHandoffState_HandoffCloseThenOpen_DefersHideConsumesHandoff_AndDoesNotRestartShow()
    {
        var state = new PopupOverlayHandoffState();
        state.OnPopupShown();

        var closeAction = state.OnPopupHiddenForHandoff(out var deferredHideGeneration);
        var openAction = state.OnPopupShown();
        var deferredResolutionAction = state.ResolveDeferredHide(deferredHideGeneration);

        Assert.Equal(PopupOverlayHostAction.DeferHide, closeAction);
        Assert.Equal(PopupOverlayHostAction.None, openAction);
        Assert.Equal(PopupOverlayHostAction.None, deferredResolutionAction);
        Assert.Equal(1, state.ActivePopupCount);
    }

    [Fact]
    public void PopupOverlayHandoffState_ResolveDeferredHide_WhenNoReopen_ReturnsHideOverlayAndClearsStaleHandoff()
    {
        var state = new PopupOverlayHandoffState();
        state.OnPopupShown();
        state.OnPopupHiddenForHandoff(out var deferredHideGeneration);

        var resolveAction = state.ResolveDeferredHide(deferredHideGeneration);
        var nextShowAction = state.OnPopupShown();

        Assert.Equal(PopupOverlayHostAction.HideOverlay, resolveAction);
        Assert.Equal(PopupOverlayHostAction.ShowOverlay, nextShowAction);
        Assert.Equal(1, state.ActivePopupCount);
    }

    [Fact]
    public void PopupOverlayHandoffState_OnPopupHidden_ExtraHideCalls_DoNotGoNegative()
    {
        var state = new PopupOverlayHandoffState();

        var extraHideBeforeShow = state.OnPopupHidden();
        state.OnPopupShown();
        state.OnPopupHidden();
        var extraHideAfterLastHide = state.OnPopupHidden();

        Assert.Equal(PopupOverlayHostAction.None, extraHideBeforeShow);
        Assert.Equal(PopupOverlayHostAction.None, extraHideAfterLastHide);
        Assert.Equal(0, state.ActivePopupCount);
    }

    [Fact]
    public void PopupOverlayHandoffState_ResolveDeferredHide_StaleGeneration_DoesNotHideDuringNewerHandoffWindow()
    {
        var state = new PopupOverlayHandoffState();
        state.OnPopupShown();

        var firstDeferAction = state.OnPopupHiddenForHandoff(out var firstGeneration);
        state.OnPopupShown();
        var secondDeferAction = state.OnPopupHiddenForHandoff(out var secondGeneration);

        var staleResolveAction = state.ResolveDeferredHide(firstGeneration);
        var currentResolveAction = state.ResolveDeferredHide(secondGeneration);

        Assert.Equal(PopupOverlayHostAction.DeferHide, firstDeferAction);
        Assert.Equal(PopupOverlayHostAction.DeferHide, secondDeferAction);
        Assert.Equal(PopupOverlayHostAction.None, staleResolveAction);
        Assert.Equal(PopupOverlayHostAction.HideOverlay, currentResolveAction);
        Assert.Equal(0, state.ActivePopupCount);
    }
}
