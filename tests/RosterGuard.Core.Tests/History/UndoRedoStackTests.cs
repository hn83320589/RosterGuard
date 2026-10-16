using RosterGuard.Core.History;

namespace RosterGuard.Core.Tests.History;

public class UndoRedoStackTests
{
    [Fact]
    public void Undo_then_redo_returns_the_same_item()
    {
        var stack = new UndoRedoStack<string>(capacity: 20);
        stack.Push("a");

        Assert.True(stack.TryUndo(out var undone));
        Assert.True(stack.TryRedo(out var redone));
        Assert.Equal("a", undone);
        Assert.Equal("a", redone);
    }

    [Fact]
    public void Push_after_undo_clears_redo()
    {
        var stack = new UndoRedoStack<string>(capacity: 20);
        stack.Push("a");
        stack.TryUndo(out _);

        stack.Push("b");

        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Oldest_items_are_dropped_beyond_capacity()
    {
        var stack = new UndoRedoStack<int>(capacity: 20);
        for (var i = 0; i < 25; i++)
        {
            stack.Push(i);
        }

        var undone = new List<int>();
        while (stack.TryUndo(out var item))
        {
            undone.Add(item);
        }

        Assert.Equal(20, undone.Count);
        Assert.Equal(24, undone[0]);
        Assert.Equal(5, undone[^1]);
    }

    [Fact]
    public void Capacity_below_20_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UndoRedoStack<int>(capacity: 19));
    }
}
