using JyunoSdk.Analysis;

namespace JyunoSdk.LanguageServer;

/// <summary>편집기에서 열린 문서 하나 (텍스트와 분석 결과)</summary>
public sealed class TextDocument
{
    public TextDocument(string uri , string text , int version)
    {
        Uri = uri;
        Text = text;
        Version = version;
    }
    public string Uri { get; }
    public string Text { get; private set; }
    public int Version { get; private set; }
    JyunoDocument? analysis;
    AnalyzerOptions? analyzed_with;

    public JyunoDocument Analyze(AnalyzerOptions options)
    {
        if (analysis is null || !ReferenceEquals(analyzed_with , options))
        {
            analysis = JyunoDocument.Parse(Text , options);
            analyzed_with = options;
        }
        return analysis;
    }

    public void Replace(string text , int version)
    {
        Text = text;
        Version = version;
        analysis = null;
    }

    /// <summary>범위를 바꿈 (LSP 위치: 줄, UTF-16 열)</summary>
    public void Apply(int start_line , int start_character , int end_line , int end_character , string text , int version)
    {
        int start = OffsetAt(Text , start_line , start_character);
        int end = OffsetAt(Text , end_line , end_character);
        if (end < start)
            (start, end) = (end, start);
        Replace(string.Concat(Text.AsSpan(0 , start) , text , Text.AsSpan(end)) , version);
    }

    /// <summary>LSP 위치를 텍스트 안의 위치로 바꿈 (줄바꿈: \r\n, \n, \r)</summary>
    public static int OffsetAt(string text , int line , int character)
    {
        int offset = 0;
        for (int current = 0 ; current < line ; current++)
        {
            int next = text.IndexOfAny(new[] { '\r' , '\n' } , offset);
            if (next < 0)
                return text.Length;
            offset = next + (text[next] is '\r' && next + 1 < text.Length && text[next + 1] is '\n' ? 2 : 1);
        }
        int line_end = text.IndexOfAny(new[] { '\r' , '\n' } , offset);
        if (line_end < 0)
            line_end = text.Length;
        return Math.Min(offset + Math.Max(0 , character) , line_end);
    }
}
