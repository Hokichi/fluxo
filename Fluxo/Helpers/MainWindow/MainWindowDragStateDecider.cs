namespace Fluxo.Helpers.MainWindow;

public enum MainWindowRestoreMode
{
    Noop,
    DragOnly,
    InstantRestoreAndDrag
}

public static class MainWindowDragStateDecider
{
    public static MainWindowRestoreMode DecideRestoreMode(
        bool isMaximized,
        bool isStateChangeTransitionActive,
        bool isEligibleHeaderDrag)
    {
        if (!isEligibleHeaderDrag || isStateChangeTransitionActive)
        {
            return MainWindowRestoreMode.Noop;
        }

        if (isMaximized)
        {
            return MainWindowRestoreMode.InstantRestoreAndDrag;
        }

        return MainWindowRestoreMode.DragOnly;
    }
}
