namespace Fluxo.Resources.CustomControls;

public sealed class BulkInsertUncheckedEventArgs : EventArgs
{
    public bool ShouldSwitchToSaveOnly { get; set; }
}
