using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxo.Core.DTO;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Ui;

namespace Fluxo.DataModels.Shell.Main.Analytics;

public readonly record struct TrendBarSeed(
        string Label,
        decimal PrimaryValue,
        bool IsPrimaryExpenseSeries,
        bool IsPrimaryIncomeSeries,
        decimal SecondaryValue,
        bool HasSecondaryValue,
        bool IsSecondaryExpenseSeries,
        bool IsSecondaryIncomeSeries);
