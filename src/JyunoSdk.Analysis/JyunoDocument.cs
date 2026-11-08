using Jyuno.Compiler;
using Jyuno.Language;

namespace JyunoSdk.Analysis;

/// <summary>분석 설정</summary>
public sealed record AnalyzerOptions
{
    /// <summary>스크립트를 실행할 프로그램이 제공하는 내장 명령어 묶음 (Jyuno Runtime의 기본값과 같은 console, math)</summary>
    public CommandGroups Commands { get; init; } = CommandGroups.Default;
    /// <summary>스크립트를 실행할 프로그램이 따로 추가하는 함수/변수 이름 (Runtime.AddFunction/AddVariable)</summary>
    public IReadOnlyCollection<string> HostNames { get; init; } = Array.Empty<string>();
    /// <summary>정의되지 않은 이름을 경고할지</summary>
    public bool ReportUndefinedNames { get; init; } = true;

    public static AnalyzerOptions Default { get; } = new();
}

/// <summary>
/// Jyuno 스크립트 하나를 실행하지 않고 분석한 결과입니다.
/// 토큰, 블록 구조(if/while/repeat/for/func), 변수 영역, 이름 해석, 진단을 제공합니다.
/// </summary>
public sealed class JyunoDocument
{
    public static JyunoDocument Parse(string text , AnalyzerOptions? options = null) => new(Lexer.SplitLines(text) , options ?? AnalyzerOptions.Default);
    public static JyunoDocument Parse(IEnumerable<string> lines , AnalyzerOptions? options = null) => new(lines.ToArray() , options ?? AnalyzerOptions.Default);

    JyunoDocument(string[] lines , AnalyzerOptions options)
    {
        Options = options;
        Lines = lines.Select((text , i) => new LineInfo(i , text , Lexer.Tokenize(text))).ToArray();
        Script = new Scope(ScopeKind.Script , null , null);
        AnalyzeBlocks();
        AssignScopes();
        foreach (var line in Lines)
            new LineParser(this , line).Parse();
        Resolve();
        CheckNames();
        diagnostics.Sort((a , b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Start.CompareTo(b.Start));
    }

    public AnalyzerOptions Options { get; }
    public IReadOnlyList<LineInfo> Lines { get; }
    public Scope Script { get; }
    public IReadOnlyList<Block> Blocks => blocks;
    public IReadOnlyList<Scope> Scopes => scopes;
    public IReadOnlyList<Diagnostic> Diagnostics => diagnostics;
    public IReadOnlyList<NameOccurrence> Names => names;
    public IEnumerable<Symbol> Symbols => scopes.SelectMany(s => s.Symbols.Values);

    readonly List<Block> blocks = new();
    readonly List<Scope> scopes = new();
    readonly List<Diagnostic> diagnostics = new();
    readonly List<NameOccurrence> names = new();

    internal void Report(string code , DiagnosticSeverity severity , string message , int line , int start , int end , bool unnecessary = false)
        => diagnostics.Add(new(code , severity , message , line , start , Math.Max(end , start)  , unnecessary));
    internal void Report(string code , DiagnosticSeverity severity , string message , int line , SourceToken token , bool unnecessary = false)
        => Report(code , severity , message , line , token.Start , token.End , unnecessary);

    internal void AddName(LineInfo line , NameOccurrence occurrence)
    {
        line.Names.Add(occurrence);
        names.Add(occurrence);
    }

    //Jyuno의 Interpreter.BlockMap과 같은 방식으로 블록의 짝과 오류를 찾음
    void AnalyzeBlocks()
    {
        Stack<Block> opened = new();
        List<(int line, Block loop)> breaks = new();
        string?[] errors = new string?[Lines.Count];
        int last = Math.Max(0 , Lines.Count - 1);

        void unclosed(Block block , int end)
        {
            block.EndLine = end;
            if (block.Kind is BlockKind.Func)
            {
                errors[block.OpenLine] = "func 키워드는 endfunc 키워드로 종료 표시가 있어야 합니다.";
                return;
            }
            errors[block.OpenLine] = "모든 조건문(if, while 등) 키워드는 end 키워드로 종료 표시가 있어야 합니다.";
            if (block.ElseLine >= 0)
                errors[block.ElseLine] = errors[block.OpenLine];
        }
        Block? innermost_function() => opened.FirstOrDefault(b => b.Kind is BlockKind.Func);

        for (int line = 0 ; line < Lines.Count ; line++)
        {
            LineInfo info = Lines[line];
            KeywordType? keyword = info.Keyword;
            if (keyword is null)
                continue;
            BlockKind? kind = keyword switch {
                KeywordType.If => BlockKind.If,
                KeywordType.While => BlockKind.While,
                KeywordType.Repeat => BlockKind.Repeat,
                KeywordType.For => BlockKind.For,
                KeywordType.Func => BlockKind.Func,
                _ => null
            };
            if (kind is BlockKind k)
            {
                Block block = new(k , line , opened.TryPeek(out var parent) ? parent : null);
                if (k is BlockKind.Func && info.Tokens.Count > 1 && info.Tokens[1].Type is TokenType.Name)
                    block.FunctionName = (string)info.Tokens[1].Value;
                blocks.Add(block);
                info.Block = block;
                opened.Push(block);
                continue;
            }
            switch (keyword)
            {
                case KeywordType.Else:
                    if (opened.TryPeek(out var owner) && owner.Kind is BlockKind.If && owner.ElseLine < 0)
                    {
                        owner.ElseLine = line;
                        info.Block = owner;
                    }
                    else
                        errors[line] = "else는 if와 end 사이에 한번만 쓸수 있습니다.";
                    break;
                case KeywordType.End:
                    if (!opened.TryPeek(out var opener) || opener.Kind is BlockKind.Func)
                    {
                        errors[line] = "end와 짝이 되는 if, while, repeat, for 키워드가 없습니다.";
                        break;
                    }
                    opened.Pop();
                    opener.CloseLine = opener.EndLine = line;
                    info.Block = opener;
                    break;
                case KeywordType.EndFunc:
                    Block? func = innermost_function();
                    if (func is null)
                    {
                        errors[line] = "endfunc와 짝이 되는 func 키워드가 없습니다.";
                        break;
                    }
                    while (opened.Peek() != func)
                        unclosed(opened.Pop() , line - 1);
                    opened.Pop();
                    func.CloseLine = func.EndLine = line;
                    info.Block = func;
                    break;
                case KeywordType.Break:
                    Block? loop = null;
                    foreach (Block o in opened)
                    {
                        if (o.Kind is BlockKind.Func)
                            break;
                        if (o.Kind is not BlockKind.If)
                        {
                            loop = o;
                            break;
                        }
                    }
                    if (loop is null)
                        errors[line] = "break는 반복문(while, repeat, for) 안에서만 쓸수 있습니다.";
                    else
                        breaks.Add((line , loop));
                    break;
                case KeywordType.Param:
                    Block? function = innermost_function();
                    if (function is null)
                    {
                        errors[line] = "param은 func와 endfunc 사이에서만 쓸수 있습니다.";
                        break;
                    }
                    if (info.Tokens.Count > 1 && info.Tokens[1].Type is TokenType.Name)
                        function.parameters.Add((string)info.Tokens[1].Value);
                    else
                        function.parameters.Add("?");
                    break;
            }
        }
        foreach (Block block in opened)
            unclosed(block , last);
        foreach (var (line , loop) in breaks)
        {
            if (errors[loop.OpenLine] is string error)
                errors[line] = error;
        }
        for (int line = 0 ; line < Lines.Count ; line++)
        {
            if (errors[line] is string message)
                Report(DiagnosticCodes.BlockError , DiagnosticSeverity.Error , message , line , Lines[line].Tokens[0]);
        }
    }

    //각 줄의 변수 영역과 들여쓰기 깊이
    void AssignScopes()
    {
        scopes.Add(Script);
        Stack<Block> open = new();
        foreach (LineInfo line in Lines)
        {
            while (open.TryPeek(out var top) && top.EndLine < line.Index)
                open.Pop();
            Block? scope_block = open.FirstOrDefault(b => b.Scope is not null);
            line.Scope = scope_block?.Scope ?? Script;
            int depth = open.Count;
            if (open.TryPeek(out var inner) && (inner.CloseLine == line.Index || inner.ElseLine == line.Index))
                depth--;
            line.Depth = depth;
            if (line.Block is Block block && block.OpenLine == line.Index)
            {
                if (block.Kind is BlockKind.Func or BlockKind.For)
                {
                    block.Scope = new Scope(block.Kind is BlockKind.Func ? ScopeKind.Function : ScopeKind.Loop , line.Scope , block);
                    scopes.Add(block.Scope);
                }
                //한 줄짜리 블록(닫히지 않고 마지막 줄에서 끝남)이 아니라면 안쪽 줄이 있음
                if (block.EndLine > line.Index)
                    open.Push(block);
            }
        }
    }

    static bool IsStrong(OccurrenceRole role) => role is OccurrenceRole.Function or OccurrenceRole.Label or OccurrenceRole.Constant or OccurrenceRole.Parameter or OccurrenceRole.Local or OccurrenceRole.LoopVariable;

    //이름 해석
    //Jyuno에서 func/param/local/레이블/상수/for 변수는 항상 지금 영역에 이름을 만들고,
    //일반 대입은 바깥 영역에 같은 이름이 있다면 그 변수를 바꾸고 없다면 지금 영역에 새로 만듦
    //실행 순서는 알수 없으므로, 영역 안에서 한번이라도 만든 이름은 그 영역 전체에서 있는 것으로 봄
    void Resolve()
    {
        Dictionary<Scope , HashSet<string>> strong = new(), weak = new();
        foreach (var occurrence in names)
        {
            if (!occurrence.IsDefinition)
                continue;
            var target = IsStrong(occurrence.Role) ? strong : weak;
            if (!target.TryGetValue(occurrence.Scope , out var set))
                target[occurrence.Scope] = set = new();
            set.Add(occurrence.Name);
        }
        Scope? bind(Scope from , string name)
        {
            Scope? outermost = null;
            foreach (Scope scope in from.Chain())
            {
                if (strong.TryGetValue(scope , out var s) && s.Contains(name))
                    return scope;
                if (weak.TryGetValue(scope , out var w) && w.Contains(name))
                    outermost = scope;
            }
            return outermost;
        }
        HashSet<string> host = new(Options.HostNames);
        foreach (var occurrence in names)
        {
            Scope? scope = IsStrong(occurrence.Role) ? occurrence.Scope : bind(occurrence.Scope , occurrence.Name);
            if (scope is null)
            {
                if (Builtins.TryGet(occurrence.Name , out var builtin))
                    occurrence.Builtin = builtin;
                occurrence.IsHostName = host.Contains(occurrence.Name);
                continue;
            }
            if (!scope.symbols.TryGetValue(occurrence.Name , out var symbol))
                scope.symbols[occurrence.Name] = symbol = new Symbol(occurrence.Name , scope);
            occurrence.Symbol = symbol;
            if (occurrence.IsDefinition)
                symbol.Definitions.Add(occurrence);
            else
                symbol.References.Add(occurrence);
        }
        foreach (var symbol in Symbols)
        {
            var roles = symbol.Definitions.Select(d => d.Role).ToHashSet();
            symbol.Kind =
                roles.Contains(OccurrenceRole.Function) ? SymbolKind.Function :
                roles.Contains(OccurrenceRole.Parameter) ? SymbolKind.Parameter :
                roles.Contains(OccurrenceRole.LoopVariable) ? SymbolKind.LoopVariable :
                roles.Contains(OccurrenceRole.Label) ? SymbolKind.Label :
                roles.Contains(OccurrenceRole.Constant) ? SymbolKind.Constant :
                roles.Contains(OccurrenceRole.Local) ? SymbolKind.Local :
                SymbolKind.Variable;
            if (symbol.Kind is SymbolKind.Function)
            {
                var def = symbol.Definitions.First(d => d.Role is OccurrenceRole.Function);
                symbol.FunctionBlock = Lines[def.Line].Block;
            }
        }
    }

    void CheckNames()
    {
        foreach (var occurrence in names)
        {
            if (occurrence.IsDefinition || occurrence.Symbol is not null || occurrence.IsHostName)
                continue;
            if (occurrence.Builtin is Builtin builtin)
            {
                if (builtin.Group is not CommandGroups.Essential && !Options.Commands.HasFlag(builtin.Group))
                    Report(DiagnosticCodes.CommandGroup , DiagnosticSeverity.Warning , $"'{occurrence.Name}'은/는 {builtin.GroupName} 명령어 묶음에 있으며, 설정된 프로그램은 이 묶음을 제공하지 않습니다." , occurrence.Line , occurrence.Token);
                continue;
            }
            if (Options.ReportUndefinedNames)
                Report(DiagnosticCodes.UndefinedName , DiagnosticSeverity.Warning , $"'{occurrence.Name}'은/는 정의되지 않은 함수/변수입니다." , occurrence.Line , occurrence.Token);
        }
        foreach (var occurrence in names)
        {
            if (occurrence.Role is not OccurrenceRole.Read)
                continue;
            bool function = occurrence.Symbol is Symbol symbol ? symbol.IsOnlyFunction : occurrence.Builtin is { IsFunction: true } && !occurrence.IsHostName;
            if (function)
                Report(DiagnosticCodes.FunctionAsValue , DiagnosticSeverity.Warning , $"'{occurrence.Name}'은/는 함수이므로 인자 자리에서 값을 읽을수 없습니다. 함수를 호출한 결과를 넘기려면 '({occurrence.Name} ...)'처럼 괄호로 감싸세요." , occurrence.Line , occurrence.Token);
        }
        //goto (레이블): 함수 안팎으로 이동할수 없음
        foreach (var line in Lines)
        {
            if (line.Keyword is not KeywordType.Goto || line.Tokens.Count < 2 || line.Tokens[1].Type is not TokenType.Name)
                continue;
            var target = line.Names.FirstOrDefault(n => n.Token == line.Tokens[1])?.Symbol;
            if (target?.Kind is not SymbolKind.Label)
                continue;
            var label_function = target.Definitions.Where(d => d.Role is OccurrenceRole.Label).Select(d => Lines[d.Line].Scope.Function).ToList();
            var here = line.Scope.Function;
            if (label_function.Count > 0 && label_function.All(f => f != here))
                Report(DiagnosticCodes.GotoAcrossFunction , DiagnosticSeverity.Error , "goto로 함수 안팎을 넘나들수 없습니다." , line.Index , line.Tokens[1]);
        }
    }

    /// <summary>위치에 있는 토큰</summary>
    public SourceToken? TokenAt(int line , int column , bool touching = false)
    {
        if (line < 0 || line >= Lines.Count)
            return null;
        var tokens = Lines[line].Tokens;
        return tokens.FirstOrDefault(t => t.Contains(column)) ?? (touching ? tokens.LastOrDefault(t => t.Touches(column) && t.Type is TokenType.Name or TokenType.Keyword) : null);
    }

    /// <summary>위치에 있는 이름 (커서가 이름 바로 뒤에 있어도 찾음)</summary>
    public NameOccurrence? NameAt(int line , int column)
    {
        if (line < 0 || line >= Lines.Count)
            return null;
        var list = Lines[line].Names;
        return list.FirstOrDefault(n => n.Token.Contains(column)) ?? list.FirstOrDefault(n => n.Token.Touches(column));
    }

    /// <summary>위치에서 보이는 영역 (줄의 영역)</summary>
    public Scope ScopeAt(int line) => line >= 0 && line < Lines.Count ? Lines[line].Scope : Script;

    /// <summary>위치에서 쓸수 있는 스크립트의 이름들 (안쪽 영역부터)</summary>
    public IEnumerable<Symbol> VisibleSymbols(int line)
    {
        HashSet<string> seen = new();
        foreach (var scope in ScopeAt(line).Chain())
        {
            foreach (var symbol in scope.Symbols.Values)
            {
                if (seen.Add(symbol.Name))
                    yield return symbol;
            }
        }
    }

    /// <summary>위치를 감싸는 가장 안쪽 함수 호출</summary>
    public CallSite? CallAt(int line , int column)
    {
        if (line < 0 || line >= Lines.Count)
            return null;
        CallSite? best = null;
        foreach (var call in Lines[line].Calls)
        {
            if (call.Head.End <= column && column <= call.ExtentEnd)
            {
                if (best is null || call.Head.Start >= best.Head.Start)
                    best = call;
            }
        }
        return best;
    }

    /// <summary>심볼의 이름이 나온 모든 위치</summary>
    public IEnumerable<NameOccurrence> OccurrencesOf(NameOccurrence occurrence)
    {
        if (occurrence.Symbol is Symbol symbol)
            return symbol.Occurrences;
        //내장/알수 없는 이름은 같은 이름이면서 스크립트에서 정의하지 않은 이름들
        return names.Where(n => n.Symbol is null && n.Name == occurrence.Name);
    }
}
