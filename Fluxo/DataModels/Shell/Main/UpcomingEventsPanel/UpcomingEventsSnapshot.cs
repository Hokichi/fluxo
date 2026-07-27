using System.Collections.ObjectModel;
using System.Globalization;
using AutoMapper;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.DTO;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Entities;
using Fluxo.Helpers.Popups;

namespace Fluxo.DataModels.Shell.Main.UpcomingEventsPanel;

public sealed record UpcomingEventsSnapshot(
        IReadOnlyList<RecurringTransaction> RecurringTransactions,
        IReadOnlyList<SavingGoal> SavingGoals,
        IReadOnlyList<Account> Accounts);
