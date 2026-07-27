namespace Snapzy.App.Annotate;

public interface IUndoable
{
    void Undo();
    void Redo();
}

public class UndoStack
{
    private readonly List<IUndoable> _undo = new();
    private readonly List<IUndoable> _redo = new();

    public event Action? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(IUndoable action)
    {
        _undo.Add(action);
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var a = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        a.Undo();
        _redo.Add(a);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var a = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        a.Redo();
        _undo.Add(a);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }
}
