using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Budgeting;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Helpers.Transaction;
using Fluxo.Helpers.MainWindow;
using Fluxo.Resources.CustomControls;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.Services.Transactions;
using Fluxo.ViewModels.Entities;
using Fluxo.Helpers.Popups;
using Fluxo.ViewModels.Shell;
using Fluxo.ViewModels.Shell.Main;
using System.Globalization;

namespace Fluxo.DataModels.Popups.TransactionPopup;

public sealed record RecurringTimeOption(string Label, string Value);
