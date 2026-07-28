using CommunityToolkit.Mvvm.ComponentModel;

namespace Fluxo.DataModels.Popups.TransactionPopup;

public sealed partial class TransactionFieldFeedback : ObservableObject
{
    public IReadOnlyList<TransactionWarning> Warnings { get; private set; } = [];
    public bool HasFeedback => Warnings.Count > 0;
    public bool HasErrors => Warnings.Any(warning => !warning.IsWarning);

    public void Update(IEnumerable<TransactionWarning> warnings)
    {
        Warnings = warnings.DistinctBy(warning => warning.Message).ToArray();
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(HasFeedback));
        OnPropertyChanged(nameof(HasErrors));
    }
}
