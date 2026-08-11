using Xunit;

namespace Fluxo.Tests.Views.CustomControls;

public class MoneyTextBoxTests
{
    [Theory]
    [InlineData("0", 0, 0, true, 0, 1)]
    [InlineData("0.00", 1, 0, true, 0, 4)]
    [InlineData("0", 0, 1, true, 0, 1)]
    [InlineData("", 0, 0, true, 0, 0)]
    [InlineData("500000", 3, 0, false, 3, 0)]
    public void MoneyTextBox_NormalizeSelectionForZeroAmount_ReturnsExpectedSelection(
        string currentText,
        int selectionStart,
        int selectionLength,
        bool isZeroAmount,
        int expectedSelectionStart,
        int expectedSelectionLength)
    {
        var actual = MoneyTextBox.NormalizeSelectionForZeroAmount(
            currentText,
            selectionStart,
            selectionLength,
            isZeroAmount);

        Assert.Equal(expectedSelectionStart, actual.SelectionStart);
        Assert.Equal(expectedSelectionLength, actual.SelectionLength);
    }
}
