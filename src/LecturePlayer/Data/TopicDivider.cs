namespace LecturePlayer.Data;

/// <summary>صف عنوان فاصل في الشجرة (فوق موضوعه) — مش عنصر في المكتبة، بيترسم من <see cref="Topic.SeparatorTitle"/>.</summary>
public sealed record TopicDivider(Topic Topic);
