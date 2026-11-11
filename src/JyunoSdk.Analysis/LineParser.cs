using Jyuno.Compiler;
using Jyuno.Language;

namespace JyunoSdk.Analysis;

/// <summary>
/// 한 줄을 Jyuno의 줄 컴파일러(Interpreter.compile_line)와 같은 규칙으로 훑어서
/// 이름(정의/참조), 함수 호출 위치, 문법 문제를 찾습니다.
/// </summary>
sealed class LineParser
{
    readonly JyunoDocument document;
    readonly LineInfo line;
    readonly IReadOnlyList<SourceToken> tokens;
    readonly int count;
    //토큰화 오류가 있는 줄은 실행되지 않으므로 다른 문법 문제는 알리지 않음
    readonly bool broken;

    public LineParser(JyunoDocument document , LineInfo line)
    {
        this.document = document;
        this.line = line;
        tokens = line.Tokens;
        broken = tokens.Count > 0 && tokens[^1].Type is TokenType.Error;
        count = broken ? tokens.Count - 1 : tokens.Count;
    }

    void Report(string code , DiagnosticSeverity severity , string message , int start , int end , bool unnecessary = false)
    {
        if (broken && code != DiagnosticCodes.TokenError)
            return;
        document.Report(code , severity , message , line.Index , start , end , unnecessary);
    }
    void Report(string code , DiagnosticSeverity severity , string message , SourceToken token , bool unnecessary = false)
        => Report(code , severity , message , token.Start , token.End , unnecessary);

    //from부터 줄 끝까지 무시되는 토큰
    void Ignored(int from , string message)
    {
        if (from >= count)
            return;
        Report(DiagnosticCodes.Ignored , DiagnosticSeverity.Warning , message , tokens[from].Start , tokens[count - 1].End , true);
    }

    bool IsPrefix(int index , char prefix) => index < count && tokens[index].IsPrefix(prefix);
    bool IsName(int index) => index < count && tokens[index].Type is TokenType.Name;

    NameOccurrence Name(int index , OccurrenceRole role , Scope? scope = null)
    {
        SourceToken token = tokens[index];
        NameOccurrence occurrence = new((string)token.Value , line.Index , token , role , scope ?? line.Scope);
        document.AddName(line , occurrence);
        return occurrence;
    }

    public void Parse()
    {
        if (broken)
        {
            SourceToken error = tokens[^1];
            document.Report(DiagnosticCodes.TokenError , DiagnosticSeverity.Error , (string)error.Value , line.Index , error);
        }
        if (count is 0)
            return;
        if (tokens[0].Type is TokenType.Keyword)
        {
            ParseKeyword((KeywordType)tokens[0].Value);
            return;
        }
        int end = ParseExpression(0 , false);
        Ignored(end , "이 부분은 실행되지 않습니다. (식이 이미 끝났습니다)");
    }

    void ParseKeyword(KeywordType keyword)
    {
        SourceToken head = tokens[0];
        string name = keyword.ToString().ToLowerInvariant();
        switch (keyword)
        {
            case KeywordType.Return:
            case KeywordType.Goto:
            case KeywordType.If:
            case KeywordType.While:
            case KeywordType.Repeat:
            {
                var (arguments, end) = ParseArguments(1 , null);
                Ignored(end , "짝이 없는 ')' 뒤의 내용은 실행되지 않습니다.");
                if (arguments.Count is 0)
                {
                    switch (keyword)
                    {
                        case KeywordType.Goto:
                            Report(DiagnosticCodes.KeywordArguments , DiagnosticSeverity.Error , "goto 다음에는 이동할 줄 번호 또는 레이블이 와야 합니다." , head);
                            break;
                        case KeywordType.If:
                        case KeywordType.While:
                            Report(DiagnosticCodes.KeywordArguments , DiagnosticSeverity.Warning , $"{name} 다음에 조건이 없으므로 항상 거짓입니다." , head);
                            break;
                        case KeywordType.Repeat:
                            Report(DiagnosticCodes.KeywordArguments , DiagnosticSeverity.Warning , "repeat 다음에 반복 횟수가 없으므로 실행되지 않습니다." , head);
                            break;
                    }
                }
                else if (arguments.Count > 1)
                {
                    Report(DiagnosticCodes.KeywordArguments , DiagnosticSeverity.Warning , $"{name}은/는 첫번째 값만 사용합니다. 함수를 호출한 결과를 쓰려면 괄호로 감싸세요. (예: {name} (함수 인자...))" , arguments[1].Start , arguments[^1].End , true);
                }
                break;
            }
            case KeywordType.For:
            {
                if (!IsName(1))
                {
                    Report(DiagnosticCodes.KeywordSyntax , DiagnosticSeverity.Error , "for는 'for (이름) (배열/리스트/문자열)' 형식으로만 쓸수 있습니다." , head);
                    break;
                }
                Name(1 , OccurrenceRole.LoopVariable , line.Block?.Scope);
                var (arguments, end) = ParseArguments(2 , null);
                Ignored(end , "짝이 없는 ')' 뒤의 내용은 실행되지 않습니다.");
                if (arguments.Count is not 1)
                    Report(DiagnosticCodes.KeywordSyntax , DiagnosticSeverity.Error , "for 다음에는 변수 이름과 순회할 값 하나만 와야 합니다." , head.Start , tokens[count - 1].End);
                break;
            }
            case KeywordType.Local:
            {
                if (!IsName(1))
                {
                    Report(DiagnosticCodes.KeywordSyntax , DiagnosticSeverity.Error , "local 다음에는 변수 이름이 와야 합니다." , head);
                    break;
                }
                Name(1 , OccurrenceRole.Local);
                if (count is 2)
                    break;
                if (count > 3 && IsPrefix(2 , '='))
                {
                    int end = ParseExpression(3 , false);
                    Ignored(end , "이 부분은 실행되지 않습니다. (식이 이미 끝났습니다)");
                    break;
                }
                Report(DiagnosticCodes.KeywordSyntax , DiagnosticSeverity.Error , "local은 'local (이름)' 또는 'local (이름) = (값)' 형식으로만 쓸수 있습니다." , head.Start , tokens[count - 1].End);
                break;
            }
            case KeywordType.Param:
            case KeywordType.Func:
            case KeywordType.Struct:
            {
                OccurrenceRole role = keyword switch {
                    KeywordType.Func => OccurrenceRole.Function,
                    KeywordType.Struct => OccurrenceRole.Struct,
                    _ => OccurrenceRole.Parameter
                };
                if (count != 2 || !IsName(1))
                {
                    string message = keyword switch {
                        KeywordType.Func => "func 다음에는 함수 이름 하나만 와야 합니다.",
                        KeywordType.Struct => "struct 다음에는 구조체 이름 하나만 와야 합니다.",
                        _ => "param 다음에는 파라미터 이름 하나만 와야 합니다."
                    };
                    Report(DiagnosticCodes.KeywordSyntax , DiagnosticSeverity.Error , message , head.Start , tokens[count - 1].End);
                    if (IsName(1))
                        Name(1 , role);
                    break;
                }
                Name(1 , role);
                break;
            }
            default:
                //else, end, endfunc, endstruct, break: 뒤의 내용은 사용하지 않음
                Ignored(1 , $"{name} 뒤의 내용은 무시됩니다.");
                break;
        }
    }

    //Interpreter.compile_expression과 같은 규칙: start부터 식 하나를 훑고, 식 다음 토큰의 위치를 반환
    int ParseExpression(int start , bool closed)
    {
        if (start >= count)
            return count;
        SourceToken token = tokens[start];
        switch (token.Type)
        {
            case TokenType.Constant:
                //(상수) = (이름): 상수 만들기
                if (IsPrefix(start + 1 , '=') && IsName(start + 2))
                {
                    Name(start + 2 , OccurrenceRole.Constant);
                    if (!closed)
                        Ignored(start + 3 , "상수 선언 뒤의 내용은 실행되지 않습니다.");
                    return count;
                }
                return start + 1;
            case TokenType.Name:
                if (IsPrefix(start + 1 , '='))
                {
                    //뒤에 아무것도 없거나 ')'라면 삭제
                    if (start + 2 >= count || IsPrefix(start + 2 , ')'))
                    {
                        Name(start , OccurrenceRole.Deletion);
                        return start + 2;
                    }
                    Name(start , OccurrenceRole.Assignment);
                    return ParseExpression(start + 2 , closed);
                }
                if (IsPrefix(start + 1 , ':'))
                {
                    Name(start , OccurrenceRole.Label);
                    if (!closed)
                        Ignored(start + 2 , "레이블 뒤의 내용은 실행되지 않습니다.");
                    return count;
                }
                return ParseCall(start);
            case TokenType.Prefix:
                if (token.IsPrefix('('))
                {
                    int inner = ParseExpression(start + 1 , true);
                    if (!IsPrefix(inner , ')'))
                    {
                        Report(DiagnosticCodes.Parenthesis , DiagnosticSeverity.Error , "괄호 '(' 다음에는 ')'로 끝나야만 합니다." , token);
                        return count;
                    }
                    return inner + 1;
                }
                if (token.IsPrefix(')'))
                    return start;
                Report(DiagnosticCodes.Ignored , DiagnosticSeverity.Warning , $"Jyuno에는 '{token.Value}' 연산자가 없습니다. 이 식은 null이 되고, 뒤의 내용은 실행되지 않습니다." , token.Start , tokens[count - 1].End , true);
                return count;
            case TokenType.Keyword:
                Report(DiagnosticCodes.MisplacedKeyword , DiagnosticSeverity.Error , $"키워드 '{((KeywordType)token.Value).ToString().ToLowerInvariant()}'은/는 줄 맨 앞에만 쓸수 있습니다." , token);
                return count;
            default:
                return count;
        }
    }

    //(이름) (인자들)
    int ParseCall(int start)
    {
        NameOccurrence head = Name(start , OccurrenceRole.Call);
        CallSite call = new(head , line.Text.Length);
        line.Calls.Add(call);
        var (arguments, end) = ParseArguments(start + 1 , call);
        call.ExtentEnd = end < count ? tokens[end].Start : line.Text.Length;
        return end;
    }

    //Interpreter.compile_arguments와 같은 규칙: ')' 또는 줄 끝까지의 인자들
    (List<(int Start, int End)> Arguments, int End) ParseArguments(int start , CallSite? call)
    {
        List<(int Start, int End)> arguments = call?.Arguments ?? new();
        for (int i = start ; i < count ; i++)
        {
            SourceToken token = tokens[i];
            switch (token.Type)
            {
                case TokenType.Constant:
                    arguments.Add((token.Start , token.End));
                    break;
                case TokenType.Name:
                    Name(i , OccurrenceRole.Read);
                    arguments.Add((token.Start , token.End));
                    break;
                case TokenType.Prefix:
                    if (token.IsPrefix('('))
                    {
                        int inner = ParseExpression(i + 1 , true);
                        if (!IsPrefix(inner , ')'))
                        {
                            Report(DiagnosticCodes.Parenthesis , DiagnosticSeverity.Error , "괄호 '(' 다음에는 ')'로 끝나야만 합니다." , token);
                            arguments.Add((token.Start , tokens[count - 1].End));
                            return (arguments , count);
                        }
                        arguments.Add((token.Start , tokens[inner].End));
                        i = inner;
                        break;
                    }
                    if (token.IsPrefix(')'))
                        return (arguments , i);
                    Report(DiagnosticCodes.Ignored , DiagnosticSeverity.Warning , $"Jyuno에는 '{token.Value}' 연산자가 없으므로 무시됩니다." , token , true);
                    break;
                case TokenType.Keyword:
                    Report(DiagnosticCodes.Ignored , DiagnosticSeverity.Warning , $"키워드 '{((KeywordType)token.Value).ToString().ToLowerInvariant()}'은/는 인자 자리에서 무시됩니다." , token , true);
                    break;
            }
        }
        return (arguments , count);
    }
}
