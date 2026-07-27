using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Core.Budgeting;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;

namespace Fluxo.DataModels.Popups.BudgetForecastVM;

public sealed record BudgetForecastPurchaseResult(string Message, string BrushKey);
