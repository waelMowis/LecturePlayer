namespace LecturePlayer.Data;

/// <summary>
/// تراجع / إعادة لتعديلات القايمة (حذف، نقل، تقسيم، ترتيب، استيراد…).
/// بيحفظ نسخة نصية (JSON) من القايمة قبل كل تعديل في الهيكل.
/// حالة المشاهدة والعلامات مش بترجع لورا مع التراجع (الأحدث بيفضل).
/// </summary>
public sealed class UndoStack
{
    private readonly int _capacity;
    private readonly LinkedList<(string Label, string Snapshot)> _undo = new();
    private readonly Stack<(string Label, string Snapshot)> _redo = new();

    public UndoStack(int capacity = 30) => _capacity = capacity;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoLabel => _undo.Last?.Value.Label;
    public string? RedoLabel => _redo.Count > 0 ? _redo.Peek().Label : null;

    /// <summary>قبل أي تعديل: احفظ الحالة الحالية.</summary>
    public void Push(string label, string snapshotBefore)
    {
        _undo.AddLast((label, snapshotBefore));
        while (_undo.Count > _capacity) _undo.RemoveFirst();
        _redo.Clear();
    }

    /// <summary>بيرجّع الحالة اللي قبل آخر تعديل، وبيحفظ الحالية للإعادة.</summary>
    public (string Label, string Snapshot)? Undo(string currentSnapshot)
    {
        if (_undo.Last == null) return null;
        var item = _undo.Last.Value;
        _undo.RemoveLast();
        _redo.Push((item.Label, currentSnapshot));
        return item;
    }

    public (string Label, string Snapshot)? Redo(string currentSnapshot)
    {
        if (_redo.Count == 0) return null;
        var item = _redo.Pop();
        _undo.AddLast((item.Label, currentSnapshot));
        return item;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
