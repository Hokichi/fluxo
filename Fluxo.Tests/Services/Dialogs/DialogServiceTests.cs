using System.Windows;
using Fluxo.Services.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fluxo.Tests.Services.Dialogs;

public sealed class DialogServiceTests
{
    [Fact]
    public void ShowWarning_UsesWarningIcon()
    {
        var (sut, state) = CreateSut();

        var result = sut.ShowWarning("message", "title");

        Assert.Equal(MessageBoxResult.OK, result);
        Assert.Equal(MessageBoxImage.Warning, state.LastIcon);
    }

    [Fact]
    public void ShowError_UsesErrorIcon()
    {
        var (sut, state) = CreateSut();

        var result = sut.ShowError("message", "title");

        Assert.Equal(MessageBoxResult.OK, result);
        Assert.Equal(MessageBoxImage.Error, state.LastIcon);
    }

    [Fact]
    public void ShowInformation_UsesInformationIcon()
    {
        var (sut, state) = CreateSut();

        var result = sut.ShowInformation("message", "title");

        Assert.Equal(MessageBoxResult.OK, result);
        Assert.Equal(MessageBoxImage.Information, state.LastIcon);
    }

    [Fact]
    public void ShowQuestion_UsesQuestionIcon()
    {
        var (sut, state) = CreateSut();

        var result = sut.ShowQuestion("message", "title", buttons: MessageBoxButton.YesNo);

        Assert.Equal(MessageBoxResult.OK, result);
        Assert.Equal(MessageBoxImage.Question, state.LastIcon);
        Assert.Equal(MessageBoxButton.YesNo, state.LastButtons);
    }

    private static (DialogService sut, TestMessageBoxState state) CreateSut()
    {
        var state = new TestMessageBoxState();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var sut = new DialogService(serviceProvider,
            (_, _, _, buttons, icon) =>
            {
                state.LastIcon = icon;
                state.LastButtons = buttons;
                return MessageBoxResult.OK;
            });
        return (sut, state);
    }

    private sealed class TestMessageBoxState
    {
        public MessageBoxImage LastIcon { get; set; } = MessageBoxImage.None;
        public MessageBoxButton LastButtons { get; set; } = MessageBoxButton.OK;
    }

}
