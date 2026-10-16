namespace RosterGuard.Core.History;

/// <summary>有容量上限的復原／重做堆疊；超過上限時丟棄最舊的項目，新項目推入時清空重做堆疊。</summary>
public sealed class UndoRedoStack<T>
{
    public const int MinimumCapacity = 20;

    private readonly LinkedList<T> _undo = new();
    private readonly Stack<T> _redo = new();
    private readonly int _capacity;

    public UndoRedoStack(int capacity = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, MinimumCapacity);
        _capacity = capacity;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(T item)
    {
        _undo.AddLast(item);
        if (_undo.Count > _capacity)
        {
            _undo.RemoveFirst();
        }
        _redo.Clear();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public bool TryPeekUndo(out T item)
    {
        item = CanUndo ? _undo.Last!.Value : default!;
        return CanUndo;
    }

    public bool TryPeekRedo(out T item) => _redo.TryPeek(out item!);

    public bool TryUndo(out T item)
    {
        if (!TryPeekUndo(out item))
        {
            return false;
        }

        _undo.RemoveLast();
        _redo.Push(item);
        return true;
    }

    public bool TryRedo(out T item)
    {
        if (!_redo.TryPop(out item!))
        {
            return false;
        }

        _undo.AddLast(item);
        return true;
    }
}
