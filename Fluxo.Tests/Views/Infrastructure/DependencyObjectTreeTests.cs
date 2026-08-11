using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Fluxo.Resources.Infrastructure;
using Xunit;

namespace Fluxo.Tests.Views.Infrastructure;

public sealed class DependencyObjectTreeTests
{
    [Fact]
    public void DependencyObjectTree_GetParent_UsesVisualTreeForVisualElement()
    {
        RunOnStaThread(() =>
        {
            var parent = new Grid();
            var child = new Button();
            parent.Children.Add(child);

            Assert.Same(parent, DependencyObjectTree.GetParent(child));
        });
    }

    [Fact]
    public void DependencyObjectTree_GetParent_UsesLogicalTreeForNonVisualElement()
    {
        RunOnStaThread(() =>
        {
            var parent = new Span();
            var child = new Run("source");
            parent.Inlines.Add(child);

            Assert.Same(parent, DependencyObjectTree.GetParent(child));
        });
    }

    [Fact]
    public void DependencyObjectTree_GetChildren_UsesVisualTreeForVisualElement()
    {
        RunOnStaThread(() =>
        {
            var parent = new Grid();
            var child = new Button();
            parent.Children.Add(child);

            Assert.Same(child, Assert.Single(DependencyObjectTree.GetChildren(parent)));
        });
    }

    [Fact]
    public void DependencyObjectTree_GetChildren_UsesLogicalTreeForNonVisualElement()
    {
        RunOnStaThread(() =>
        {
            var parent = new Span();
            var child = new Run("source");
            parent.Inlines.Add(child);

            Assert.Contains(child, DependencyObjectTree.GetChildren(parent));
        });
    }

    [Fact]
    public void DependencyObjectTree_IsDescendantOf_WalksSharedParentChain()
    {
        RunOnStaThread(() =>
        {
            var parent = new TextBlock();
            var child = new Run("source");
            parent.Inlines.Add(child);

            Assert.True(DependencyObjectTree.IsDescendantOf(child, parent));
            Assert.True(DependencyObjectTree.IsDescendantOf(parent, parent));
            Assert.False(DependencyObjectTree.IsDescendantOf(parent, child));
        });
    }

    [Fact]
    public void DependencyObjectTree_FindAncestor_WalksFromRunInlineWithoutVisualParentException()
    {
        RunOnStaThread(() =>
        {
            var textBlock = new TextBlock();
            var run = new Run("source");
            textBlock.Inlines.Add(run);

            Assert.Same(textBlock, DependencyObjectTree.FindAncestor<TextBlock>(run));
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }
}
