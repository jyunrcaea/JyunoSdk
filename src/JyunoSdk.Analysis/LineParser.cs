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
                var (arguments, end) = ParseKeywordArguments(1);
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
                    Report(DiagnosticCodes.KeywordArguments , DiagnosticSeverity.Warning , $"{name}은/는 첫번째 값만 사용합니다. (값 하나를 계산하는 식만 쓰세요)" , arguments[1].Start , arguments[^1].End , true);
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
                var (arguments, end) = ParseKeywordArguments(2);
                Ignored(end , "짝이 없는 ')' 뒤의 내용은 실행되지 않습니다.");
                if (arguments.Count is not 1 && !failed)
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
            case KeywordType.Func when count is 2 && tokens[1].Operator is string symbol:
            {
                //func (연산자): 구조체의 연산자 다시 정의 (구조체 생성자 안에서만, Jyuno 1.6.0부터)
                if (!Grammar.IsOverloadable(symbol))
                    Report(DiagnosticCodes.KeywordSyntax , DiagnosticSeverity.Error , $"연산자 '{symbol}'은/는 다시 정의할수 없습니다." , head.Start , tokens[1].End);
                else if (line.Scope.Function?.Kind is not ScopeKind.Struct)
                    Report(DiagnosticCodes.KeywordSyntax , DiagnosticSeverity.Error , $"연산자 '{symbol}'은/는 구조체(struct) 안에서만 다시 정의할수 있습니다." , head.Start , tokens[1].End);
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

    //─────────── 식 (Jyuno의 LineCompiler.cs/OperatorCompiler.cs와 같은 규칙) ───────────
    //연산자를 잘못 쓴 식은 실행하면 오류가 나고 줄의 나머지는 계산하지 않음 (Jyuno의 SyntaxError, 한 줄에 하나만 알림)
    bool failed;
    int SyntaxError(string message , SourceToken token)
    {
        if (!failed)
            Report(DiagnosticCodes.OperatorSyntax , DiagnosticSeverity.Error , message , token);
        failed = true;
        return count;
    }
    bool IsOperator(int index , string symbol) => index < count && tokens[index].IsOperator(symbol);
    //연산자 다음에 값이 없는지 (줄 끝, ')', ':')
    bool OperandMissing(int index) => index >= count || IsPrefix(index , ')') || IsPrefix(index , ':');

    //Interpreter.compile_expression과 같은 규칙: start부터 식 하나를 훑고, 식 다음 토큰의 위치를 반환
    int ParseExpression(int start , bool closed)
    {
        if (start >= count)
            return count;
        SourceToken token = tokens[start];
        //(상수) = (이름): 상수 만들기
        if (token.Type is TokenType.Constant && IsPrefix(start + 1 , '=') && IsName(start + 2))
        {
            Name(start + 2 , OccurrenceRole.Constant);
            if (!closed)
                Ignored(start + 3 , "상수 선언 뒤의 내용은 실행되지 않습니다.");
            return count;
        }
        if (token.Type is TokenType.Name)
        {
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
            //복합 대입 (x += 1은 x = x + 1, 이름의 값을 먼저 읽음)
            if (start + 1 < count && tokens[start + 1].Operator is string op && Grammar.CompoundAssignment(op) is string symbol)
            {
                Name(start , OccurrenceRole.Call);
                if (OperandMissing(start + 2))
                    return SyntaxError($"연산자 '{symbol}=' 다음에 값이 없습니다." , tokens[start + 1]);
                return ParseExpression(start + 2 , closed);
            }
            if (IsPrefix(start + 1 , ':'))
            {
                Name(start , OccurrenceRole.Label);
                if (!closed)
                    Ignored(start + 2 , "레이블 뒤의 내용은 실행되지 않습니다.");
                return count;
            }
        }
        return ParseOperators(start , closed);
    }

    //연산자 식 하나 (조건 연산자 ?:가 가장 낮은 우선순위)
    int ParseOperators(int start , bool closed)
    {
        int end = ParseBinary(start , closed , 2);
        if (failed || !IsOperator(end , "?"))
            return end;
        int question = end;
        if (OperandMissing(question + 1))
            return SyntaxError("조건 연산자 '?' 다음에 값이 없습니다." , tokens[question]);
        int middle = ParseOperators(question + 1 , closed);
        if (failed)
            return count;
        if (!IsPrefix(middle , ':'))
            return SyntaxError("조건 연산자 '?' 다음에는 ':'가 와야 합니다. (조건 ? 참일때 : 거짓일때)" , tokens[question]);
        if (OperandMissing(middle + 1))
            return SyntaxError("조건 연산자 ':' 다음에 값이 없습니다." , tokens[middle]);
        end = ParseOperators(middle + 1 , closed);
        return failed ? count : end;
    }

    //우선순위가 min 이상인 이항 연산자로 이어진 식
    int ParseBinary(int start , bool closed , int min)
    {
        int end = ParseUnary(start , closed);
        while (!failed && end < count && tokens[end].Operator is string symbol)
        {
            int precedence = Grammar.BinaryPrecedence(symbol);
            if (precedence < min)
                break;
            if (OperandMissing(end + 1))
                return SyntaxError($"연산자 '{symbol}' 다음에 값이 없습니다." , tokens[end]);
            //??는 오른쪽부터 묶음
            end = ParseBinary(end + 1 , closed , symbol is "??" ? precedence : precedence + 1);
        }
        return failed ? count : end;
    }

    int ParseUnary(int start , bool closed)
    {
        if (start < count && tokens[start].Operator is string symbol)
        {
            if (!Grammar.IsUnary(symbol))
                return SyntaxError($"연산자 '{symbol}' 앞에 값이 없습니다." , tokens[start]);
            if (OperandMissing(start + 1))
                return SyntaxError($"연산자 '{symbol}' 다음에 값이 없습니다." , tokens[start]);
            return ParseUnary(start + 1 , closed);
        }
        return ParsePrimary(start , closed);
    }

    //연산자의 피연산자: 상수, 함수 호출(이름 + 인자들), 괄호
    int ParsePrimary(int start , bool closed)
    {
        if (start >= count)
            return count;
        SourceToken token = tokens[start];
        switch (token.Type)
        {
            case TokenType.Constant:
                return start + 1;
            case TokenType.Name:
                return ParseCall(start);
            case TokenType.Prefix:
                if (token.IsPrefix('('))
                {
                    int inner = ParseExpression(start + 1 , true);
                    if (failed)
                        return count;
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

    //Interpreter.compile_arguments와 같은 규칙: ')', ':', 연산자 또는 줄 끝까지의 인자들 (함수 호출은 연산자보다 먼저 묶임)
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
                        if (failed)
                            return (arguments , count);
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
                    if (token.IsPrefix(')') || token.IsPrefix(':'))
                        return (arguments , i);
                    Report(DiagnosticCodes.Ignored , DiagnosticSeverity.Warning , $"Jyuno에는 '{token.Value}' 기호를 인자로 쓸수 없으므로 무시됩니다." , token , true);
                    break;
                case TokenType.Operator:
                    return (arguments , i);
                case TokenType.Keyword:
                    Report(DiagnosticCodes.Ignored , DiagnosticSeverity.Warning , $"키워드 '{((KeywordType)token.Value).ToString().ToLowerInvariant()}'은/는 인자 자리에서 무시됩니다." , token , true);
                    break;
            }
        }
        return (arguments , count);
    }

    //키워드(if, while, return, goto, repeat, for) 다음의 인자들: 식을 차례대로 (보통은 하나, Interpreter.compile_keyword_arguments와 같은 규칙)
    (List<(int Start, int End)> Arguments, int End) ParseKeywordArguments(int start)
    {
        List<(int Start, int End)> arguments = new();
        int i = start;
        while (i < count && !failed)
        {
            SourceToken token = tokens[i];
            if (token.IsPrefix(')'))
                return (arguments , i);
            //그 외의 기호, 키워드, 값 앞에 올수 없는 연산자는 무시
            if (token.Type is TokenType.Prefix && !token.IsPrefix('('))
            {
                Report(DiagnosticCodes.Ignored , DiagnosticSeverity.Warning , $"Jyuno에는 '{token.Value}' 기호를 인자로 쓸수 없으므로 무시됩니다." , token , true);
                i++;
                continue;
            }
            if (token.Type is TokenType.Keyword)
            {
                Report(DiagnosticCodes.Ignored , DiagnosticSeverity.Warning , $"키워드 '{((KeywordType)token.Value).ToString().ToLowerInvariant()}'은/는 인자 자리에서 무시됩니다." , token , true);
                i++;
                continue;
            }
            if (token.Operator is string symbol && !Grammar.IsUnary(symbol))
            {
                Report(DiagnosticCodes.Ignored , DiagnosticSeverity.Warning , $"연산자 '{symbol}' 앞에 값이 없으므로 무시됩니다." , token , true);
                i++;
                continue;
            }
            int end = Math.Max(ParseOperators(i , false) , i + 1);
            arguments.Add((token.Start , tokens[Math.Min(end , count) - 1].End));
            i = end;
        }
        return (arguments , Math.Min(i , count));
    }
}
