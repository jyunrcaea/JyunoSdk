using Jyuno.Language;

namespace JyunoSdk.Analysis;

public enum DiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

/// <summary>진단 코드 (편집기에서 문제를 구분하거나 끌때 사용)</summary>
public static class DiagnosticCodes
{
    /// <summary>토큰화 오류 (닫히지 않은 문자열, long 범위를 넘는 정수)</summary>
    public const string TokenError = "JY1001";
    /// <summary>블록 구조 오류 (end/endfunc 짝, else, break, param 위치)</summary>
    public const string BlockError = "JY1002";
    /// <summary>닫히지 않은 괄호</summary>
    public const string Parenthesis = "JY1003";
    /// <summary>키워드 형식 오류 (func/param/for/local)</summary>
    public const string KeywordSyntax = "JY1004";
    /// <summary>줄 맨 앞이 아닌 곳에 쓴 키워드</summary>
    public const string MisplacedKeyword = "JY1005";
    /// <summary>goto로 함수 안팎을 넘나듦</summary>
    public const string GotoAcrossFunction = "JY1006";
    /// <summary>연산자를 잘못 씀 (값이 없는 연산자, ':'가 없는 조건 연산자 등, Jyuno 1.6.0부터)</summary>
    public const string OperatorSyntax = "JY1007";
    /// <summary>정의되지 않은 이름</summary>
    public const string UndefinedName = "JY2001";
    /// <summary>함수를 괄호 없이 인자로 사용 (값을 읽을수 없음)</summary>
    public const string FunctionAsValue = "JY2002";
    /// <summary>실행되지 않고 무시되는 토큰</summary>
    public const string Ignored = "JY2003";
    /// <summary>키워드 인자 개수 (if/while/repeat/goto/return)</summary>
    public const string KeywordArguments = "JY2004";
    /// <summary>프로그램이 제공하지 않을수 있는 내장 명령어 묶음 (file 등)</summary>
    public const string CommandGroup = "JY2005";
}

/// <summary>분석 중에 발견한 문제</summary>
/// <param name="Unnecessary">실행되지 않는 코드인지 (편집기에서 흐리게 표시)</param>
public sealed record Diagnostic(string Code , DiagnosticSeverity Severity , string Message , int Line , int Start , int End , bool Unnecessary = false)
{
    public override string ToString() => $"{Line + 1}:{Start + 1}: {Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
}

public enum BlockKind
{
    If,
    While,
    Repeat,
    For,
    Func,
    /// <summary>struct ~ endstruct (Jyuno 1.5.0부터)</summary>
    Struct
}

/// <summary>if/while/repeat/for ... end, func ... endfunc 또는 struct ... endstruct 블록</summary>
public sealed class Block
{
    internal Block(BlockKind kind , int open , Block? parent)
    {
        Kind = kind;
        OpenLine = open;
        Parent = parent;
    }
    public BlockKind Kind { get; }
    public int OpenLine { get; }
    /// <summary>if 블록의 else 줄 (없다면 -1)</summary>
    public int ElseLine { get; internal set; } = -1;
    /// <summary>블록을 닫는 end/endfunc/endstruct 줄 (닫히지 않았다면 -1)</summary>
    public int CloseLine { get; internal set; } = -1;
    /// <summary>블록에 속하는 마지막 줄 (닫혔다면 CloseLine, 아니라면 블록이 끝나는 것으로 보는 줄)</summary>
    public int EndLine { get; internal set; }
    public bool IsClosed => CloseLine >= 0;
    public bool IsLoop => Kind is BlockKind.While or BlockKind.Repeat or BlockKind.For;
    /// <summary>본문을 따로 실행하는 블록인지 (func 또는 struct)</summary>
    public bool IsBody => Kind is BlockKind.Func or BlockKind.Struct;
    public Block? Parent { get; }
    /// <summary>블록이 만드는 변수 영역 (func, struct, for만)</summary>
    public Scope? Scope { get; internal set; }
    /// <summary>func/struct 블록의 파라미터 이름 (param이 나온 순서)</summary>
    public IReadOnlyList<string> Parameters => parameters;
    internal readonly List<string> parameters = new();
    /// <summary>func 블록의 함수 이름 또는 struct 블록의 구조체 이름 (형식이 잘못되었다면 null)</summary>
    public string? FunctionName { get; internal set; }
    public bool Contains(int line) => OpenLine < line && line <= EndLine;
}

public enum ScopeKind
{
    /// <summary>스크립트 최상위</summary>
    Script,
    /// <summary>함수 호출 한번</summary>
    Function,
    /// <summary>for 반복 한번</summary>
    Loop,
    /// <summary>구조체 값 하나 (struct와 endstruct 사이의 생성자, Jyuno 1.5.0부터)</summary>
    Struct
}

/// <summary>변수 영역 (스크립트 최상위, 함수, for 반복, 구조체)</summary>
public sealed class Scope
{
    internal Scope(ScopeKind kind , Scope? parent , Block? block)
    {
        Kind = kind;
        Parent = parent;
        Block = block;
    }
    public ScopeKind Kind { get; }
    public Scope? Parent { get; }
    public Block? Block { get; }
    public IEnumerable<Scope> Chain()
    {
        for (Scope? scope = this ; scope is not null ; scope = scope.Parent)
            yield return scope;
    }
    /// <summary>가장 가까운 함수 또는 구조체 생성자 영역 (밖이라면 null)</summary>
    public Scope? Function => Chain().FirstOrDefault(s => s.Kind is ScopeKind.Function or ScopeKind.Struct);
    internal readonly Dictionary<string , Symbol> symbols = new();
    public IReadOnlyDictionary<string , Symbol> Symbols => symbols;
}

public enum SymbolKind
{
    Variable,
    Function,
    Parameter,
    Label,
    Constant,
    Local,
    LoopVariable,
    /// <summary>struct로 만든 구조체 (Jyuno 1.5.0부터)</summary>
    Struct
}

/// <summary>스크립트에서 만든 이름 하나 (같은 영역의 같은 이름은 하나의 심볼)</summary>
public sealed class Symbol
{
    internal Symbol(string name , Scope scope)
    {
        Name = name;
        Scope = scope;
    }
    public string Name { get; }
    public Scope Scope { get; }
    public SymbolKind Kind { get; internal set; } = SymbolKind.Variable;
    public List<NameOccurrence> Definitions { get; } = new();
    public List<NameOccurrence> References { get; } = new();
    public IEnumerable<NameOccurrence> Occurrences => Definitions.Concat(References).OrderBy(o => o.Line).ThenBy(o => o.Start);
    /// <summary>함수라면 func 블록, 구조체라면 struct 블록</summary>
    public Block? FunctionBlock { get; internal set; }
    /// <summary>모든 정의가 func 또는 struct인지 (값을 읽을수 없는 이름)</summary>
    public bool IsOnlyFunction => Definitions.Count > 0 && Definitions.All(d => d.Role is OccurrenceRole.Function or OccurrenceRole.Struct);
    public NameOccurrence? FirstDefinition => Definitions.OrderBy(d => d.Line).ThenBy(d => d.Start).FirstOrDefault();
}

public enum OccurrenceRole
{
    /// <summary>(이름) = (값)</summary>
    Assignment,
    /// <summary>(이름) =</summary>
    Deletion,
    /// <summary>(이름):</summary>
    Label,
    /// <summary>(상수) = (이름)</summary>
    Constant,
    /// <summary>func (이름)</summary>
    Function,
    /// <summary>param (이름)</summary>
    Parameter,
    /// <summary>local (이름)</summary>
    Local,
    /// <summary>for (이름) ...</summary>
    LoopVariable,
    /// <summary>struct (이름)</summary>
    Struct,
    /// <summary>식 맨 앞의 이름 (함수 호출, 변수 읽기, 배열/리스트 인덱스)</summary>
    Call,
    /// <summary>인자 자리의 이름 (변수의 값 읽기)</summary>
    Read
}

/// <summary>코드에 나온 이름 하나</summary>
public sealed class NameOccurrence
{
    internal NameOccurrence(string name , int line , SourceToken token , OccurrenceRole role , Scope scope)
    {
        Name = name;
        Line = line;
        Token = token;
        Role = role;
        Scope = scope;
    }
    public string Name { get; }
    public int Line { get; }
    public SourceToken Token { get; }
    public int Start => Token.Start;
    public int End => Token.End;
    public OccurrenceRole Role { get; }
    public Scope Scope { get; }
    /// <summary>이름이 가리키는 심볼 (내장 명령어 또는 알수 없는 이름이라면 null)</summary>
    public Symbol? Symbol { get; internal set; }
    /// <summary>이름이 가리키는 내장 명령어</summary>
    public Builtin? Builtin { get; internal set; }
    /// <summary>프로그램이 제공한다고 설정한 이름인지</summary>
    public bool IsHostName { get; internal set; }
    /// <summary>
    /// 구조체 값의 멤버 이름(변수.필드, 변수.구조체 함수)으로 보이는 이름이라면, 점 앞의 변수 심볼
    /// (예: me.name의 me, l.from.x의 l.from 또는 l) 구조체 값을 대입하면 Jyuno가 멤버 이름을 선언하므로 정의되지 않은 이름으로 보지 않음
    /// </summary>
    public Symbol? MemberOf { get; internal set; }
    public bool IsDefinition => Role is not (OccurrenceRole.Call or OccurrenceRole.Read or OccurrenceRole.Deletion);
    /// <summary>바깥 변수를 바꿀수도 있는 정의 (일반 대입)</summary>
    internal bool IsWeakDefinition => Role is OccurrenceRole.Assignment;
}

/// <summary>함수 호출(또는 변수 읽기) 하나: 이름과 인자들의 위치 (시그니처 도움말에서 사용)</summary>
public sealed class CallSite
{
    internal CallSite(NameOccurrence head , int end)
    {
        Head = head;
        ExtentEnd = end;
    }
    public NameOccurrence Head { get; }
    /// <summary>인자들의 (시작, 끝) 위치</summary>
    public List<(int Start, int End)> Arguments { get; } = new();
    /// <summary>호출이 끝나는 위치 (닫는 괄호의 위치 또는 줄 끝)</summary>
    public int ExtentEnd { get; internal set; }
}

/// <summary>한 줄의 분석 결과</summary>
public sealed class LineInfo
{
    internal LineInfo(int index , string text , IReadOnlyList<SourceToken> tokens)
    {
        Index = index;
        Text = text;
        Tokens = tokens;
    }
    public int Index { get; }
    public string Text { get; }
    public IReadOnlyList<SourceToken> Tokens { get; }
    /// <summary>줄 맨 앞의 키워드</summary>
    public KeywordType? Keyword => Tokens.Count > 0 && Tokens[0].Type is Jyuno.Compiler.TokenType.Keyword ? (KeywordType)Tokens[0].Value : null;
    public Scope Scope { get; internal set; } = null!;
    /// <summary>들여쓰기 깊이 (포맷팅에서 사용)</summary>
    public int Depth { get; internal set; }
    /// <summary>이 줄에서 열거나 닫는 블록 (if/else/end/func/endfunc/struct/endstruct 등의 줄)</summary>
    public Block? Block { get; internal set; }
    public List<NameOccurrence> Names { get; } = new();
    public List<CallSite> Calls { get; } = new();
}
