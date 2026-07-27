using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Budgeting;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Logging;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Shell;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;
using System.Globalization;
using OperationResult = Fluxo.DataModels.Popups.Settings.SettingsOperationResult.SettingsOperationResult;

namespace Fluxo.DataModels.Popups.Settings.SettingsTagsTab;

public readonly record struct SettingsTagDialogRequest(
    AddTagVM ViewModel,
    Func<string, string, string, Task<OperationResult>> SaveTagAsync);
