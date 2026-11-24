using LecturePlayer.Data;

namespace LecturePlayer;

internal sealed partial class MainForm
{
    /// <summary>فاصل بين الموضوعات: فوق الموضوع اللي عليه علامة بس، ومع «فواصل بين كل الموضوعات» فوق كل موضوع ماعدا الأول.</summary>
    internal void RunTopicSeparatorCheck()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception("topic separator: " + what); }
        bool before = _settings.TopicSeparators;
        var added = new List<Topic>();
        try
        {
            _settings.TopicSeparators = false;
            while (_library.Topics.Count < 3) { var t = new Topic { Name = "فاصل " + _library.Topics.Count }; _library.Topics.Add(t); added.Add(t); }
            var first = _library.Topics[0];
            var second = _library.Topics[1];
            var third = _library.Topics[2];
            first.SeparatorBefore = true; // أول موضوع: مفيش خط حتى لو عليه علامة
            ToggleTopicSeparator(second);
            Check(second.SeparatorBefore, "toggle sets the flag");
            bool Line(Topic t) => _topicNodes.TryGetValue(t, out var n) && _tree.RowOf(n)?.LineAbove == true;
            Check(!Line(first) && Line(second) && !Line(third), "line only above the marked topic");
            ToggleAllTopicSeparators();
            Check(!Line(first) && Line(second) && Line(third), "all topics separated except the first");
            SaveSmokeShot(this, "topic-separators");
            ToggleAllTopicSeparators();
            ToggleTopicSeparator(second);
            Check(!Line(second), "toggle removes the line");
            Console.WriteLine("SMOKE topic separators: ok");
        }
        finally
        {
            foreach (var t in _library.Topics) t.SeparatorBefore = false;
            foreach (var t in added) _library.Topics.Remove(t);
            _settings.TopicSeparators = before;
            RebuildTree();
        }
    }
}
