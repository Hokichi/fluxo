using Fluxo.Core.Enums;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Shell.Main;
using Xunit;

namespace Fluxo.Tests.Services.Notifications;

public sealed class NotificationGroupingServiceTests
{
    [Fact]
    public void NotificationGroupingService_Group_MapsRecurringTransactionDue_WithoutActionCta()
    {
        var input = new[]
        {
            CreateNotification("RecurringTransactionDue-1", DateTime.Today, NotificationSeverity.Warning)
        };

        var sut = new NotificationGroupingService();

        var cards = sut.Group(input);

        var card = Assert.Single(cards);
        Assert.Equal(NotificationGroupCategory.RecurringTransactionDue, card.Category);
        Assert.False(card.HasActionCta);
        Assert.Equal(1, card.Count);
    }

    [Fact]
    public void NotificationGroupingService_Group_LeavesLegacyDeadlineCardsNonActionable()
    {
        var input = new[]
        {
            CreateNotification("GoalDeadline-9_20260429", DateTime.Today, NotificationSeverity.Warning),
            CreateNotification("LowBalance-2", DateTime.Today.AddMinutes(-1), NotificationSeverity.Danger)
        };

        var sut = new NotificationGroupingService();

        var cards = sut.Group(input);

        Assert.Equal(2, cards.Count);
        Assert.False(cards.Single(card => card.Category == NotificationGroupCategory.GoalDeadline).HasActionCta);
        Assert.False(cards.Single(card => card.Category == NotificationGroupCategory.LowBalance).HasActionCta);
    }

    [Fact]
    public void NotificationGroupingService_Group_GroupsByCategorySetsCountAndLatestCreatedOn_AndOrdersDescending()
    {
        var now = DateTime.Now;
        var input = new[]
        {
            CreateNotification("BudgetThresholdNeeds", now.AddMinutes(-20), NotificationSeverity.Warning, message: "Needs"),
            CreateNotification("BudgetThresholdWants", now.AddMinutes(-10), NotificationSeverity.Warning, message: "Wants"),
            CreateNotification("LowCredit-7", now.AddMinutes(-5), NotificationSeverity.Warning, message: "Credit")
        };

        var sut = new NotificationGroupingService();

        var cards = sut.Group(input);

        Assert.Equal(2, cards.Count);

        var first = cards[0];
        var second = cards[1];

        Assert.Equal(NotificationGroupCategory.LowCredit, first.Category);
        Assert.Equal(NotificationGroupCategory.BudgetThreshold, second.Category);
        Assert.Equal(2, second.Count);
        Assert.Equal(now.AddMinutes(-10), second.LatestCreatedOn);
        Assert.Equal("Budget Threshold", second.Header);
        Assert.Equal("2 pending items", second.Message);
    }

    [Fact]
    public void NotificationGroupingService_Group_SingleNotification_UsesSingularPendingItemMessage()
    {
        var input = new[]
        {
            CreateNotification("LowCredit-7", DateTime.Today, NotificationSeverity.Warning, message: "Credit")
        };

        var sut = new NotificationGroupingService();

        var card = Assert.Single(sut.Group(input));

        Assert.Equal("1 pending item", card.Message);
    }

    [Fact]
    public void NotificationGroupingService_Group_UsesSeverityPrecedence_DangerOverWarningOverSuccessOverInfo()
    {
        var input = new[]
        {
            CreateNotification("BudgetThresholdNeeds", DateTime.Today, NotificationSeverity.Success),
            CreateNotification("BudgetThresholdWants", DateTime.Today.AddMinutes(-1), NotificationSeverity.Warning),
            CreateNotification("BudgetThresholdSavings", DateTime.Today.AddMinutes(-2), NotificationSeverity.Danger),
            CreateNotification("BudgetThresholdOther", DateTime.Today.AddMinutes(-3), NotificationSeverity.Info)
        };

        var sut = new NotificationGroupingService();

        var card = Assert.Single(sut.Group(input));

        Assert.Equal(NotificationGroupCategory.BudgetThreshold, card.Category);
        Assert.Equal(NotificationSeverity.Danger, card.Severity);
    }

    [Fact]
    public void NotificationGroupingService_Group_MapsUnknownType_ToOtherCategory()
    {
        var input = new[]
        {
            CreateNotification("CustomType-42", DateTime.Today, NotificationSeverity.Info)
        };

        var sut = new NotificationGroupingService();

        var card = Assert.Single(sut.Group(input));

        Assert.Equal(NotificationGroupCategory.Other, card.Category);
        Assert.False(card.HasActionCta);
    }

    [Theory]
    [InlineData("LowBalance-2", NotificationGroupCategory.LowBalance)]
    [InlineData("LowCredit-7", NotificationGroupCategory.LowCredit)]
    [InlineData("BudgetThresholdNeeds", NotificationGroupCategory.BudgetThreshold)]
    [InlineData("AutoExpenseProcessed", NotificationGroupCategory.AutoExpenseProcessed)]
    public void NotificationGroupingService_Group_NonActionableCategoriesHaveNoActionCta(string type, NotificationGroupCategory category)
    {
        var input = new[]
        {
            CreateNotification(type, DateTime.Today, NotificationSeverity.Warning)
        };

        var sut = new NotificationGroupingService();

        var card = Assert.Single(sut.Group(input));

        Assert.Equal(category, card.Category);
        Assert.False(card.HasActionCta);
    }

    [Fact]
    public void NotificationGroupingService_Group_AppUpdateMapsCategoryWithoutActionCta_AndPreservesHeaderAndMessage()
    {
        const string persistedHeader = "Update Ready - Install Recommended";
        const string persistedMessage = "Version 9.9.9 is available for download";
        var input = new[]
        {
            new NotificationVM
            {
                Type = "AppUpdate-9.9.9",
                CreatedOn = DateTime.Today,
                Severity = NotificationSeverity.Info,
                Header = persistedHeader,
                Message = persistedMessage
            }
        };

        var sut = new NotificationGroupingService();

        var card = Assert.Single(sut.Group(input));

        Assert.Equal(NotificationGroupCategory.AppUpdate, card.Category);
        Assert.False(card.HasActionCta);
        Assert.Equal(persistedHeader, card.Header);
        Assert.Equal(persistedMessage, card.Message);
    }

    [Fact]
    public void NotificationGroupingService_Group_AppUpdate_IsOrderedBeforeNewerNonUpdateCard()
    {
        var now = DateTime.Now;
        var input = new[]
        {
            CreateNotification(
                "AppUpdate-1.2.3",
                now.AddMinutes(-10),
                NotificationSeverity.Info,
                message: "Version 1.2.3 is available for download"),
            CreateNotification(
                "LowCredit-7",
                now,
                NotificationSeverity.Warning,
                message: "Credit warning")
        };

        var sut = new NotificationGroupingService();

        var cards = sut.Group(input);

        Assert.Equal(2, cards.Count);
        Assert.Equal(NotificationGroupCategory.AppUpdate, cards[0].Category);
        Assert.Equal(NotificationGroupCategory.LowCredit, cards[1].Category);
    }

    private static NotificationVM CreateNotification(
        string type,
        DateTime createdOn,
        NotificationSeverity severity,
        string? message = null)
    {
        return new NotificationVM
        {
            Type = type,
            CreatedOn = createdOn,
            Severity = severity,
            Header = $"Header {type}",
            Message = message ?? $"Message {type}"
        };
    }
}
