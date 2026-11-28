using LecturePlayer.Data;
using LecturePlayer.UI;

namespace LecturePlayer;

/// <summary>«📂 افتح حصة» للطالب: يختار ملف الحصة المضغوط وتنزل له محتوياتها وتتفتح.</summary>
internal partial class MainForm
{
    internal void OpenLessonFileDialog()
    {
        if (!EnsureLibrary()) return;
        ReceiveSharedShelfDialog(null);
    }
}
