using System.Globalization;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Popups;
using Fluxo.Helpers.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class AddAccountVMTests
{
    [Fact]
    public void Constructor_DefaultsMonthlyDueDateToCurrentDay()
    {
        var sut = CreateSut();
        var expected = MonthlyDueDateHelper.Normalize(DateTime.Today.Day)?
            .ToString(CultureInfo.InvariantCulture);

        Assert.Equal(expected, sut.MonthlyDueDateText);
    }

    [Fact]
    public void PopupTitle_WhenEditingAccount_UsesAccountCopy()
    {
        var sut = new AddAccountVM(
            mainViewModel: null!,
            appData: null!)
        {
            EditingId = 7
        };

        Assert.Equal("Edit Account", sut.PopupTitle);
    }

    [Fact]
    public void NotificationAction_ReflectsCreateOrEditMode()
    {
        var create = new AddAccountVM(mainViewModel: null!, appData: null!);
        var edit = new AddAccountVM(mainViewModel: null!, appData: null!) { EditingId = 7 };

        Assert.Equal("Added", create.NotificationAction);
        Assert.Equal("Updated", edit.NotificationAction);
    }

    [Fact]
    public void InitializeFromAccount_LoadsEditableFields()
    {
        var sut = CreateSut();
        var source = new Fluxo.Core.Entities.Account
        {
            Id = 7,
            Name = "Visa",
            AccountType = AccountType.Credit,
            Balance = 0m,
            SpentAmount = 120m,
            AccountLimit = 1_000m,
            MaximumSpending = 800m,
            MinimumPayment = 5m,
            MonthlyDueDate = 15,
            DeductSource = 2,
            PinnedOnUI = false,
            IsEnabled = false,
            IsDefault = true
        };

        sut.InitializeFromAccount(source);

        Assert.Equal(7, sut.EditingId);
        Assert.Equal("Visa", sut.NameText);
        Assert.Equal(AccountType.Credit, sut.SelectedAccountType);
        Assert.Equal(120m, sut.SpentAmountText);
        Assert.Equal(1_000m, sut.AccountLimitText);
        Assert.Equal(800m, sut.MaximumSpendingText);
        Assert.Equal(5m, sut.MinimumPaymentText);
        Assert.Equal("15", sut.MonthlyDueDateText);
        Assert.Equal(2, sut.SelectedDeductSource);
        Assert.False(sut.PinnedOnUI);
        Assert.False(sut.IsEnabled);
        Assert.True(sut.IsDefault);
    }

    [Theory]
    [InlineData(AccountType.Checking, true)]
    [InlineData(AccountType.Cash, true)]
    [InlineData(AccountType.Saving, true)]
    [InlineData(AccountType.Credit, false)]
    public void IsBalanceLike_MatchesExpectedTypes(AccountType sourceType, bool expected)
    {
        var sut = CreateSut();

        sut.SelectedAccountType = sourceType;

        Assert.Equal(expected, sut.IsBalanceLike);
    }

    [Fact]
    public async Task SaveAsync_WhenCreditMaximumSpendingExceedsAccountLimit_ReturnsWarningAndDoesNotSave()
    {
        var saveWasCalled = false;
        var sut = CreateSut(_ =>
        {
            saveWasCalled = true;
            return Task.FromResult(AddAccountResult.Success());
        });

        ConfigureValidCreditFields(sut);
        sut.AccountLimitText = 1000m;
        sut.SpentAmountText = 900m;
        sut.MaximumSpendingText = 1001m;
        sut.MarkMaximumSpendingModified();

        var result = await sut.SaveAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Maximum spending cannot exceed the account limit.", result.ErrorMessage);
        Assert.Equal(AddAccountFailurePresentation.ToastWarning, result.FailurePresentation);
        Assert.False(saveWasCalled);
    }

    [Fact]
    public async Task SaveAsync_WhenSavingApyExceeds100_ReturnsWarningAndDoesNotSave()
    {
        var saveWasCalled = false;
        var sut = CreateSut(_ =>
        {
            saveWasCalled = true;
            return Task.FromResult(AddAccountResult.Success());
        });

        sut.NameText = "Emergency Savings";
        sut.SelectedAccountType = AccountType.Saving;
        sut.PrimaryAmountText = 500m;
        sut.ApyText = 101m;

        var result = await sut.SaveAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("APY cannot exceed 100%.", result.ErrorMessage);
        Assert.Equal(AddAccountFailurePresentation.ToastWarning, result.FailurePresentation);
        Assert.False(saveWasCalled);
    }

    [Fact]
    public async Task SaveAsync_WhenCreditMinimumPaymentExceeds100_ReturnsWarningAndDoesNotSave()
    {
        var saveWasCalled = false;
        var sut = CreateSut(_ =>
        {
            saveWasCalled = true;
            return Task.FromResult(AddAccountResult.Success());
        });

        ConfigureValidCreditFields(sut);
        sut.MinimumPaymentText = 101m;

        var result = await sut.SaveAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Minimum payment cannot exceed 100%.", result.ErrorMessage);
        Assert.Equal(AddAccountFailurePresentation.ToastWarning, result.FailurePresentation);
        Assert.False(saveWasCalled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateNameField_WhenNameIsEmptyOrWhitespace_ShowsRequiredHint(string name)
    {
        var sut = CreateSut();
        sut.NameText = name;

        sut.ValidateNameField();

        Assert.True(sut.HasErrors);
        Assert.Equal("Required", sut.NameValidationHint);
    }

    [Fact]
    public void ValidateNameField_WhenNameContainsControlCharacter_ShowsInvalidNameHint()
    {
        var sut = CreateSut();
        sut.NameText = "Bad\u0001Name";

        sut.ValidateNameField();

        Assert.True(sut.HasErrors);
        Assert.Equal("Invalid Name", sut.NameValidationHint);
    }

    [Fact]
    public void ValidateNameField_WhenNameExceedsMaximumLength_ShowsTooLongHint()
    {
        var sut = CreateSut();
        sut.NameText = new string('A', 257);

        sut.ValidateNameField();

        Assert.True(sut.HasErrors);
        Assert.Equal("Too Long", sut.NameValidationHint);
    }

    [Fact]
    public void ValidateMaximumSpendingField_WhenBalanceLikeMaximumExceedsBalance_ShowsLimitHint()
    {
        var sut = CreateSut();
        sut.NameText = "Checking";
        sut.SelectedAccountType = AccountType.Checking;
        sut.PrimaryAmountText = 100m;
        sut.MaximumSpendingText = 101m;

        sut.ValidateMaximumSpendingField();

        Assert.True(sut.HasErrors);
        Assert.Equal("Exceeds Limit", sut.MaximumSpendingValidationHint);
    }

    [Fact]
    public void ValidateMaximumSpendingField_WhenCreditMaximumExceedsAccountLimit_ShowsLimitHint()
    {
        var sut = CreateSut();
        ConfigureValidCreditFields(sut);
        sut.AccountLimitText = 100m;
        sut.SpentAmountText = 0m;
        sut.MaximumSpendingText = 101m;

        sut.ValidateMaximumSpendingField();

        Assert.True(sut.HasErrors);
        Assert.Equal("Exceeds Limit", sut.MaximumSpendingValidationHint);
    }

    [Fact]
    public void ValidateSpentAmountField_WhenCreditSpentAmountExceedsAccountLimit_ShowsLimitHint()
    {
        var sut = CreateSut();
        ConfigureValidCreditFields(sut);
        sut.AccountLimitText = 100m;
        sut.SpentAmountText = 101m;

        sut.ValidateSpentAmountField();

        Assert.True(sut.HasErrors);
        Assert.Equal("Exceeds Limit", sut.SpentAmountValidationHint);
    }

    [Fact]
    public void ValidateMinimumPaymentField_WhenCreditMinimumPaymentIsZero_ShowsRequiredHint()
    {
        var sut = CreateSut();
        ConfigureValidCreditFields(sut);
        sut.MinimumPaymentText = 0m;

        sut.ValidateMinimumPaymentField();

        Assert.True(sut.HasErrors);
        Assert.Equal("Required", sut.MinimumPaymentValidationHint);
    }

    [Fact]
    public void ValidateApyField_WhenSavingApyIsZero_ShowsRequiredHint()
    {
        var sut = CreateSut();
        sut.NameText = "Savings";
        sut.SelectedAccountType = AccountType.Saving;
        sut.PrimaryAmountText = 100m;
        sut.ApyText = 0m;

        sut.ValidateApyField();

        Assert.True(sut.HasErrors);
        Assert.Equal("Required", sut.ApyValidationHint);
    }

    [Fact]
    public void SelectedAccountTypeChanged_ClearsNameValidation()
    {
        var sut = CreateSut();
        sut.NameText = " ";
        sut.ValidateNameField();

        Assert.True(sut.HasErrors);
        Assert.Equal("Required", sut.NameValidationHint);

        sut.SelectedAccountType = AccountType.Credit;

        Assert.Empty(sut.GetErrors(nameof(AddAccountVM.NameText)));
        Assert.Equal(string.Empty, sut.NameValidationHint);
    }

    [Fact]
    public void SelectedAccountTypeChanged_ClearsMaximumSpendingValidation()
    {
        var sut = CreateSut();
        ConfigureValidCreditFields(sut);
        sut.AccountLimitText = 100m;
        sut.MaximumSpendingText = 101m;
        sut.ValidateMaximumSpendingField();

        Assert.True(sut.HasErrors);
        Assert.Equal("Exceeds Limit", sut.MaximumSpendingValidationHint);

        sut.SelectedAccountType = AccountType.Cash;

        Assert.Empty(sut.GetErrors(nameof(AddAccountVM.MaximumSpendingText)));
        Assert.Equal(string.Empty, sut.MaximumSpendingValidationHint);
    }

    [Fact]
    public void SelectedAccountTypeChanged_ClearsInvalidNameValidationButKeepsSaveDisabled()
    {
        var sut = CreateSut();
        sut.NameText = new string('A', 257);
        sut.ValidateNameField();

        Assert.True(sut.HasErrors);
        Assert.Equal("Too Long", sut.NameValidationHint);

        sut.SelectedAccountType = AccountType.Cash;

        Assert.Empty(sut.GetErrors(nameof(AddAccountVM.NameText)));
        Assert.Equal(string.Empty, sut.NameValidationHint);
        Assert.False(sut.CanSave);
    }

    [Fact]
    public async Task ValidateNameField_WhenSourceNameAlreadyExists_ShowsExistingNameHint()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetAccountsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(
            [
                new Account
                {
                    Id = 1,
                    Name = "Checking",
                    AccountType = AccountType.Checking,
                    IsEnabled = true
                }
            ]));
        var sut = new AddAccountVM(
            mainViewModel: null!,
            appData: appData);
        await sut.LoadDeductSourcesAsync();
        sut.NameText = "checking";

        sut.ValidateNameField();

        Assert.True(sut.HasErrors);
        Assert.Equal("Existing Name", sut.NameValidationHint);
    }

    [Fact]
    public void HasChanges_IgnoresDueDateDeductSourceMaximumSpendingAndSourceType()
    {
        var sut = CreateSut();
        sut.DeductSources.Add(new AddAccountDeductSourceOption(1, "Checking", AccountType.Checking));
        sut.BeginChangeTracking();

        sut.MonthlyDueDateText = "12";
        sut.SelectedDeductSource = 1;
        sut.MaximumSpendingText = 500m;
        sut.SelectedAccountType = AccountType.Credit;

        Assert.False(sut.HasChanges);
    }

    [Fact]
    public void MaximumSpendingPlaceholderText_UsesBalanceForBalanceLikeSources()
    {
        var sut = CreateSut();
        sut.SelectedAccountType = AccountType.Checking;
        sut.PrimaryAmountText = 1250.50m;

        Assert.Equal("1250.50", sut.MaximumSpendingPlaceholderText);
    }

    [Fact]
    public void MaximumSpendingPlaceholderText_UsesAccountLimitForCreditSources()
    {
        var sut = CreateSut();
        sut.SelectedAccountType = AccountType.Credit;
        sut.AccountLimitText = 2500m;

        Assert.Equal("2500", sut.MaximumSpendingPlaceholderText);
    }

    [Fact]
    public async Task SaveAsync_WhenMaximumSpendingIsUnmodified_PersistsPlaceholderValue()
    {
        AddAccountInput? savedInput = null;
        var sut = CreateSut(input =>
        {
            savedInput = input;
            return Task.FromResult(AddAccountResult.Success());
        });
        sut.NameText = "Checking";
        sut.SelectedAccountType = AccountType.Checking;
        sut.PrimaryAmountText = 750m;

        var result = await sut.SaveAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(750m, savedInput?.MaximumSpending);
    }

    [Fact]
    public async Task SaveAsync_WhenMaximumSpendingIsModified_PersistsEditedValue()
    {
        AddAccountInput? savedInput = null;
        var sut = CreateSut(input =>
        {
            savedInput = input;
            return Task.FromResult(AddAccountResult.Success());
        });
        sut.NameText = "Checking";
        sut.SelectedAccountType = AccountType.Checking;
        sut.PrimaryAmountText = 750m;
        sut.MaximumSpendingText = 100m;
        sut.MarkMaximumSpendingModified();

        var result = await sut.SaveAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(100m, savedInput?.MaximumSpending);
    }

    private static AddAccountVM CreateSut(
        Func<AddAccountInput, Task<AddAccountResult>>? saveDraftAsync = null)
    {
        return new AddAccountVM(
            mainViewModel: null!,
            appData: null!,
            saveDraftAsync: saveDraftAsync);
    }

    private static void ConfigureValidCreditFields(AddAccountVM sut)
    {
        sut.NameText = "Credit Card";
        sut.SelectedAccountType = AccountType.Credit;
        sut.AccountLimitText = 1000m;
        sut.SpentAmountText = 100m;
        sut.MaximumSpendingText = 100m;
        sut.MinimumPaymentText = 10m;
        sut.MonthlyDueDateText = "15";
        sut.DeductSources.Add(new AddAccountDeductSourceOption(1, "Checking", AccountType.Checking));
        sut.SelectedDeductSource = 1;
    }
}
