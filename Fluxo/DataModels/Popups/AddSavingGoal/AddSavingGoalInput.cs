using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Shell;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;

namespace Fluxo.DataModels.Popups.AddSavingGoal;

public readonly record struct AddSavingGoalInput(
        string Name,
        decimal TargetAmount,
        decimal CurrentAmount,
        DateTime? EndDate);
