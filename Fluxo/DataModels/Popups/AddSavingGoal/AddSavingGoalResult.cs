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

public readonly record struct AddSavingGoalResult(bool IsSuccess, bool ShouldClose, string? ErrorMessage)
    {
        public static AddSavingGoalResult Success(bool shouldClose = false)
        {
            return new AddSavingGoalResult(true, shouldClose, null);
        }

        public static AddSavingGoalResult Failure(string? errorMessage)
        {
            return new AddSavingGoalResult(false, false, errorMessage);
        }
    }
