using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Interfaces;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Tests.TestDoubles;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Services.History;

public sealed class LogMemoryManagerTests
{
    [Fact]
    public async Task LogMemoryManager_RevertTo_RevertsOnlyNewerActiveEntriesNewestFirst()
    {
        var calls = new List<string>();
        var (manager, messenger, reloads) = CreateManager();
        foreach (var name in new[] { "first", "second", "third", "fourth" })
            messenger.Send(new RecordLogMemoryMessage(new RecordingAction(name, calls)));
        await manager.ToggleAsync(manager.HistoryEntries[2]);
        calls.Clear();
        reloads.Clear();

        Assert.True(await manager.RevertToAsync(manager.HistoryEntries[0]));

        Assert.Equal(["revert:fourth", "revert:second"], calls);
        Assert.False(manager.HistoryEntries[0].IsReverted);
        Assert.All(manager.HistoryEntries.Skip(1), entry => Assert.True(entry.IsReverted));
        Assert.Single(reloads);
    }

    [Fact]
    public async Task LogMemoryManager_RevertTo_BuildsOldestFirstRedoOrder()
    {
        var calls = new List<string>();
        var (manager, messenger, _) = CreateManager();
        foreach (var name in new[] { "first", "second", "third" })
            messenger.Send(new RecordLogMemoryMessage(new RecordingAction(name, calls)));
        await manager.RevertToAsync(manager.HistoryEntries[0]);
        calls.Clear();

        await manager.RedoAsync();
        await manager.RedoAsync();

        Assert.Equal(["reapply:second", "reapply:third"], calls);
    }

    [Fact]
    public async Task LogMemoryManager_RevertTo_WhenBatchFails_PreservesHistoryAndStacks()
    {
        var (manager, messenger, _) = CreateManager();
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("first", [])));
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("broken", [], true)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.RevertToAsync(manager.HistoryEntries[0]));

        Assert.All(manager.HistoryEntries, entry => Assert.False(entry.IsReverted));
        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void LogMemoryManager_CanRevertToHere_IsTrueOnlyWithNewerActiveEntry()
    {
        var (manager, messenger, _) = CreateManager();
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("first", [])));
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("second", [])));

        Assert.True(manager.HistoryEntries[0].CanRevertToHere);
        Assert.False(manager.HistoryEntries[1].CanRevertToHere);
    }

    [Fact]
    public async Task LogMemoryManager_UndoAndRedo_UseLifoOrderAndUpdateHistoryState()
    {
        var calls = new List<string>();
        var (manager, messenger, reloads) = CreateManager();
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("first", calls)));
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("second", calls)));

        Assert.True(await manager.UndoAsync());
        Assert.True(manager.HistoryEntries[1].IsReverted);
        Assert.True(manager.CanRedo);
        Assert.True(await manager.RedoAsync());

        Assert.Equal(["revert:second", "reapply:second"], calls);
        Assert.False(manager.HistoryEntries[1].IsReverted);
        Assert.Equal(2, reloads.Count);
    }

    [Fact]
    public async Task LogMemoryManager_Undo_SkipsEntryAlreadyRevertedThroughHistory()
    {
        var calls = new List<string>();
        var (manager, messenger, _) = CreateManager();
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("first", calls)));
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("second", calls)));
        await manager.ToggleAsync(manager.HistoryEntries[1]);

        Assert.True(await manager.UndoAsync());

        Assert.Equal(["revert:second", "revert:first"], calls);
    }

    [Fact]
    public async Task LogMemoryManager_Toggle_CurrentRedoEntry_MakesItUndoEligibleAgain()
    {
        var (manager, messenger, _) = CreateManager();
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("only", [])));
        await manager.UndoAsync();

        await manager.ToggleAsync(manager.HistoryEntries[0]);

        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public async Task LogMemoryManager_FailedUndo_PreservesHistoryAndAvailability()
    {
        var (manager, messenger, _) = CreateManager();
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("broken", [], true)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UndoAsync());

        Assert.False(manager.HistoryEntries[0].IsReverted);
        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void LogMemoryManager_Record_AppendsEntriesOldestFirst()
    {
        var (manager, messenger, _) = CreateManager();

        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("first", [])));
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("second", [])));

        Assert.Equal(["first", "second"], manager.HistoryEntries.Select(entry => entry.Description));
    }

    [Fact]
    public async Task LogMemoryManager_Toggle_RevertsOnlySelectedEntry_ThenReappliesIt()
    {
        var calls = new List<string>();
        var (manager, messenger, reloads) = CreateManager();
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("first", calls)));
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("second", calls)));
        var selected = manager.HistoryEntries[0];

        Assert.True(await manager.ToggleAsync(selected));
        Assert.True(selected.IsReverted);
        Assert.False(manager.HistoryEntries[1].IsReverted);

        Assert.True(await manager.ToggleAsync(selected));
        Assert.False(selected.IsReverted);
        Assert.Equal(["revert:first", "reapply:first"], calls);
        Assert.Equal(2, reloads.Count);
    }

    [Fact]
    public async Task LogMemoryManager_Toggle_WhenOperationFails_PreservesEntryState()
    {
        var (manager, messenger, _) = CreateManager();
        messenger.Send(new RecordLogMemoryMessage(new RecordingAction("broken", [], true)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.ToggleAsync(manager.HistoryEntries[0]));

        Assert.False(manager.HistoryEntries[0].IsReverted);
    }

    [Fact]
    public async Task LogMemoryManager_Toggle_WithForeignEntry_ReturnsFalse()
    {
        var (manager, _, _) = CreateManager();
        var foreign = new LogMemoryEntry(new RecordingAction("foreign", []));

        Assert.False(await manager.ToggleAsync(foreign));
    }

    private static (LogMemoryManager Manager, StrongReferenceMessenger Messenger, List<bool> Reloads)
        CreateManager()
    {
        var messenger = new StrongReferenceMessenger();
        var reloads = new List<bool>();
        var manager = new LogMemoryManager(
            new InlineDataOperationRunner(Substitute.For<IUnitOfWork>()),
            () =>
            {
                reloads.Add(true);
                return Task.CompletedTask;
            },
            messenger);
        return (manager, messenger, reloads);
    }

    private sealed class RecordingAction(string description, List<string> calls, bool failOnRevert = false)
        : ILogMemoryAction
    {
        public string Description { get; } = description;

        public Task RevertAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            calls.Add($"revert:{Description}");
            return failOnRevert
                ? Task.FromException(new InvalidOperationException("revert failed"))
                : Task.CompletedTask;
        }

        public Task ReapplyAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            calls.Add($"reapply:{Description}");
            return Task.CompletedTask;
        }
    }
}
