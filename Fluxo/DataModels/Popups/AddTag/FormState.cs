using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Fluxo.DataModels.Popups.AddTag;

public readonly record struct FormState(string NameText, string SelectedColorHex, string SpendingLimitText, string OptionFingerprint);
