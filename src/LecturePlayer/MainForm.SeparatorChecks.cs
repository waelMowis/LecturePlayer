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
            bool Line(Topic t) => _topicNodes.TryGetValue(t, out var n) && _tree.RowOf(n)?.LineAbove == true;
            TreeNode? Divider(Topic t) => _dividerNodes.TryGetValue(t, out var n) ? n : null;

            // خط بس (عنوان فاضي)
            SmokeSeparatorTitle = _ => "";
            ToggleTopicSeparator(second);
            Check(second.SeparatorBefore && second.SeparatorTitle == null && Line(second) && Divider(second) == null && !Line(first) && !Line(third), "plain line above the marked topic");

            // بعنوان: صف عنوان فوق الموضوع، والموضوع نفسه مالوش خط
            SmokeSeparatorTitle = _ => "الدراسة";
            EditTopicSeparator(second);
            var dn = Divider(second);
            Check(dn != null && dn.Text == "الدراسة" && _tree.RowOf(dn)?.Divider == true && !Line(second), "titled divider row");
            Check(dn!.Index == _topicNodes[second].Index - 1, "divider sits right above its topic");
            Check(_tree.SelectedNode == dn, "new divider gets selected");
            // عنوان فوق أول موضوع مسموح (عنوان لأول مجموعة)
            SmokeSeparatorTitle = _ => "الشغل";
            EditTopicSeparator(first);
            Check(Divider(first)?.Index == 0, "heading above the first topic");

            // F2 على العنوان بيعدّله، وDel بيشيله، ومع البحث العناوين بتستخبى
            _tree.SelectedNode = Divider(second);
            SmokeSeparatorTitle = cur => cur + " ٢";
            RenameSelected();
            Check(second.SeparatorTitle == "الدراسة ٢", "rename divider: " + second.SeparatorTitle);
            Check(SelectedTopicContext() == second, "divider context is its topic");
            SaveSmokeShot(this, "topic-dividers");
            _searchBox.Text = first.Name;
            RebuildTree();
            Check(Divider(first) == null && Divider(second) == null, "no dividers while searching");
            _searchBox.Text = "";
            RebuildTree();
            _tree.SelectedNode = Divider(second);
            DeleteSelected();
            Check(!second.SeparatorBefore && Divider(second) == null && !Line(second), "delete removes the divider");

            ToggleAllTopicSeparators();
            Check(Divider(first) != null && Line(second) && Line(third), "all-lines mode keeps the titled heading");
            ToggleAllTopicSeparators();
            Console.WriteLine("SMOKE topic separators: ok");
        }
        finally
        {
            SmokeSeparatorTitle = null;
            _searchBox.Text = "";
            foreach (var t in _library.Topics) { t.SeparatorBefore = false; t.SeparatorTitle = null; }
            foreach (var t in added) _library.Topics.Remove(t);
            _settings.TopicSeparators = before;
            RebuildTree();
        }
    }
}
