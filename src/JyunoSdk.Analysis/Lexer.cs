using System.Globalization;
using System.Text;
using Jyuno.Compiler;
using Jyuno.Language;

namespace JyunoSdk.Analysis;

/// <summary>
/// 위치 정보가 있는 토큰. <see cref="TokenType"/>과 <see cref="Value"/>는 Jyuno의 <see cref="Parser.Tokenizer"/>와 같습니다.
/// </summary>
/// <param name="Type">토큰 종류 (Jyuno와 같음)</param>
/// <param name="Value">토큰 값 (Jyuno와 같음: 상수는 long/double/string, 연산자는 char, 키워드는 KeywordType, 이름은 string, 오류는 메시지)</param>
/// <param name="Start">줄 안에서 토큰이 시작하는 위치 (UTF-16 단위)</param>
/// <param name="End">줄 안에서 토큰이 끝나는 위치 (포함하지 않음)</param>
public sealed record SourceToken(TokenType Type , object Value , int Start , int End)
{
    public int Length => End - Start;
    public bool IsPrefix(char prefix) => Type is TokenType.Prefix && Value is char c && c == prefix;
    public bool IsKeyword(KeywordType keyword) => Type is TokenType.Keyword && Value is KeywordType k && k == keyword;
    public string? Name => Type is TokenType.Name ? (string)Value : null;
    public bool Contains(int column) => Start <= column && column < End;
    /// <summary>커서가 토큰 안 또는 바로 뒤에 있는지</summary>
    public bool Touches(int column) => Start <= column && column <= End;
}

/// <summary>
/// Jyuno 토큰화기를 위치 정보와 함께 다시 구현한 것입니다.
/// 결과 토큰의 종류와 값은 항상 <see cref="Parser.Tokenizer(string)"/>와 같아야 합니다. (테스트로 검증)
/// </summary>
public static class Lexer
{
    public static IReadOnlyList<SourceToken> Tokenize(string line)
    {
        List<SourceToken> tokens = new();
        //Jyuno는 줄 양 끝의 공백(탭 포함)을 먼저 지움
        int begin = 0, finish = line.Length;
        while (begin < finish && char.IsWhiteSpace(line[begin]))
            begin++;
        while (finish > begin && char.IsWhiteSpace(line[finish - 1]))
            finish--;

        for (int index = begin ; index < finish ; index++)
        {
            char start = line[index];
            //줄 중간에서는 ' '만 공백으로 취급함 (탭은 이름의 일부가 됨)
            if (start is ' ')
                continue;
            //숫자 (숫자 바로 앞에 붙은 '-'는 음수 부호)
            if (IsDigit(start) || (start is '-' && index + 1 < finish && IsDigit(line[index + 1])))
            {
                int from = index;
                while (++index < finish && IsDigit(line[index])) { }
                bool real = index < finish && line[index] is '.';
                if (real)
                {
                    while (++index < finish && IsDigit(line[index])) { }
                }
                string number = line[from..index];
                int to = index;
                index--;
                if (real)
                {
                    tokens.Add(new(TokenType.Constant , double.Parse(number , NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint , CultureInfo.InvariantCulture) , from , to));
                    continue;
                }
                if (!long.TryParse(number , NumberStyles.AllowLeadingSign , CultureInfo.InvariantCulture , out long num))
                {
                    tokens.Add(new(TokenType.Error , $"정수 {number}은/는 long 범위를 벗어납니다." , from , to));
                    return tokens;
                }
                tokens.Add(new(TokenType.Constant , num , from , to));
                continue;
            }
            //연산자
            if (Grammar.Prefixes.Contains(start))
            {
                tokens.Add(new(TokenType.Prefix , start , index , index + 1));
                continue;
            }
            //문자열
            if (start is '"' or '\'')
            {
                int from = index;
                StringBuilder sb = new();
                bool broken = false;
                while (++index < finish && line[index] != start)
                {
                    if (line[index] == '\\')
                    {
                        if (++index >= finish)
                        {
                            broken = true;
                            break;
                        }
                        if (line[index] is 'n') { sb.Append('\n'); continue; }
                        if (line[index] is 'r') { sb.Append('\r'); continue; }
                    }
                    sb.Append(line[index]);
                }
                if (broken || index >= finish)
                {
                    tokens.Add(new(TokenType.Error , "문자열은 항상 따옴표로 끝나야 합니다." , from , finish));
                    return tokens;
                }
                tokens.Add(new(TokenType.Constant , sb.ToString() , from , index + 1));
                continue;
            }
            //이름 또는 키워드
            {
                int from = index;
                while (index < finish && !Grammar.Prefixes.Contains(line[index]) && line[index] is not (' ' or '"' or '\''))
                    index++;
                string name = line[from..index];
                int to = index;
                index--;
                if (Grammar.Keywords.TryGetValue(name , out KeywordType keyword))
                    tokens.Add(new(TokenType.Keyword , keyword , from , to));
                else
                    tokens.Add(new(TokenType.Name , name , from , to));
            }
        }
        return tokens;
    }

    static bool IsDigit(char c) => c is >= '0' and <= '9';

    /// <summary>이름에 쓸수 있는 글자인지 (자동 완성에서 커서 앞의 단어를 찾을 때 사용)</summary>
    public static bool IsNameChar(char c) => !Grammar.Prefixes.Contains(c) && c is not (' ' or '"' or '\'') && !char.IsWhiteSpace(c);

    /// <summary>새 이름으로 쓸수 있는지 (이름 바꾸기에서 사용)</summary>
    public static bool IsValidName(string name)
    {
        var tokens = Tokenize(name);
        return tokens.Count is 1 && tokens[0].Type is TokenType.Name && tokens[0].Start is 0 && tokens[0].End == name.Length && name.All(IsNameChar);
    }

    /// <summary>Jyuno의 File.ReadAllLines와 같은 방식으로 줄을 나눕니다. (\r\n, \n, \r)</summary>
    public static string[] SplitLines(string text)
    {
        List<string> lines = new();
        int start = 0;
        for (int i = 0 ; i < text.Length ; i++)
        {
            char c = text[i];
            if (c is '\r' or '\n')
            {
                lines.Add(text[start..i]);
                if (c is '\r' && i + 1 < text.Length && text[i + 1] is '\n')
                    i++;
                start = i + 1;
            }
        }
        lines.Add(text[start..]);
        return lines.ToArray();
    }
}
