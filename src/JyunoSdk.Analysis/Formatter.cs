namespace JyunoSdk.Analysis;

/// <summary>한 줄의 바뀐 내용 (줄 전체를 바꿈)</summary>
public sealed record LineEdit(int Line , string NewText);

/// <summary>
/// 블록 구조에 맞춰 들여쓰기를 정리합니다.
/// Jyuno는 줄 양 끝의 공백을 무시하므로, 줄 앞뒤의 공백만 바꾸며 실행 결과는 바뀌지 않습니다.
/// </summary>
public static class Formatter
{
    public static IReadOnlyList<LineEdit> Format(JyunoDocument document , int tabSize = 4 , bool insertSpaces = false)
    {
        string unit = insertSpaces ? new string(' ' , Math.Max(1 , tabSize)) : "\t";
        List<LineEdit> edits = new();
        foreach (var line in document.Lines)
        {
            string content = line.Text.Trim();
            string formatted = content.Length is 0 ? string.Empty : string.Concat(Enumerable.Repeat(unit , line.Depth)) + content;
            if (formatted != line.Text)
                edits.Add(new(line.Index , formatted));
        }
        return edits;
    }

    /// <summary>정리한 전체 텍스트 (줄바꿈은 \n)</summary>
    public static string FormatText(string text , int tabSize = 4 , bool insertSpaces = false)
    {
        var document = JyunoDocument.Parse(text);
        string[] lines = document.Lines.Select(l => l.Text).ToArray();
        foreach (var edit in Format(document , tabSize , insertSpaces))
            lines[edit.Line] = edit.NewText;
        return string.Join('\n' , lines);
    }
}
