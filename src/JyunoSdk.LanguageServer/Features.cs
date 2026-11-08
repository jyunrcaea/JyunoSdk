using System.Text.Json.Nodes;
using Jyuno.Compiler;
using Jyuno.Language;
using JyunoSdk.Analysis;

namespace JyunoSdk.LanguageServer;

public sealed partial class JyunoLanguageServer
{
    //LSP CompletionItemKind / SymbolKind
    const int CompletionFunction = 3, CompletionVariable = 6, CompletionKeyword = 14, CompletionSnippet = 15, CompletionConstant = 21, CompletionValue = 12;
    const int SymbolFunction = 12, SymbolVariable = 13, SymbolConstant = 14, SymbolKey = 20;

    public static readonly string[] SemanticTokenTypes = { "keyword" , "function" , "variable" , "parameter" , "string" , "number" , "operator" , "label" };
    public static readonly string[] SemanticTokenModifiers = { "declaration" , "readonly" , "defaultLibrary" };

    #region 설명

    /// <summary>이름에 대한 마크다운 설명 (hover)</summary>
    public static string? Describe(JyunoDocument document , NameOccurrence name)
    {
        if (name.Symbol is Symbol symbol)
            return DescribeSymbol(document , symbol);
        if (name.Builtin is Builtin builtin)
            return builtin.ToMarkdown();
        if (name.IsHostName)
            return $"```jyuno\n{name.Name}\n```\n*프로그램이 제공하는 함수/변수*";
        return null;
    }

    static string Signature(Symbol symbol)
    {
        var parameters = symbol.FunctionBlock?.Parameters ?? Array.Empty<string>();
        return "func " + symbol.Name + string.Concat(parameters.Select(p => $" ({p})"));
    }

    static string DescribeSymbol(JyunoDocument document , Symbol symbol)
    {
        var def = symbol.FirstDefinition;
        string where = def is null ? "" : $"{def.Line}번째 줄";
        string scope = symbol.Scope.Kind switch {
            ScopeKind.Script => "스크립트 최상위",
            ScopeKind.Function => $"함수 '{symbol.Scope.Block?.FunctionName ?? "?"}'",
            _ => "for 반복"
        };
        string code, kind;
        switch (symbol.Kind)
        {
            case SymbolKind.Function:
                code = Signature(symbol);
                kind = "함수";
                break;
            case SymbolKind.Parameter:
                int index = symbol.Scope.Block?.Parameters.ToList().IndexOf(symbol.Name) ?? -1;
                code = $"param {symbol.Name}";
                kind = index >= 0 ? $"파라미터 ({index + 1}번째 인자)" : "파라미터";
                break;
            case SymbolKind.Label:
                code = $"{symbol.Name}:";
                kind = $"레이블 (goto {symbol.Name} → {where})";
                break;
            case SymbolKind.Constant:
                var constant = symbol.Definitions.FirstOrDefault(d => d.Role is OccurrenceRole.Constant);
                string value = constant is null ? "?" : document.Lines[constant.Line].Tokens.TakeWhile(t => t != constant.Token).FirstOrDefault(t => t.Type is TokenType.Constant) is SourceToken c ? document.Lines[constant.Line].Text[c.Start..c.End] : "?";
                code = $"{value} = {symbol.Name}";
                kind = "상수";
                break;
            case SymbolKind.Local:
                code = $"local {symbol.Name}";
                kind = "지역 변수";
                break;
            case SymbolKind.LoopVariable:
                code = $"for {symbol.Name}";
                kind = "for 반복 변수";
                break;
            default:
                code = symbol.Name;
                kind = "변수";
                break;
        }
        return $"```jyuno\n{code}\n```\n*{kind} · {scope} · {where}에서 정의*";
    }

    #endregion

    #region 자동 완성

    JsonNode? Completion(JyunoDocument document , JsonNode p)
    {
        var (line_index, column) = ReadPosition(p);
        if (line_index >= document.Lines.Count)
            return new JsonArray();
        var line = document.Lines[line_index];
        column = Math.Min(column , line.Text.Length);
        //문자열 안에서는 자동 완성하지 않음
        foreach (var token in line.Tokens)
        {
            if (token.Type is TokenType.Constant && token.Value is string && token.Start < column && column < token.End)
                return new JsonArray();
            if (token.Type is TokenType.Error && token.Start < column)
                return new JsonArray();
        }
        int start = column;
        while (start > 0 && Lexer.IsNameChar(line.Text[start - 1]))
            start--;
        bool first = line.Text[..start].Trim().Length is 0;
        var range = Range(line_index , start , line_index , column);

        JsonArray items = new();
        HashSet<string> seen = new();
        void add(string label , int kind , string? detail , string? documentation , string sort , string? snippet = null)
        {
            JsonObject item = new() {
                ["label"] = label ,
                ["kind"] = kind ,
                ["sortText"] = sort + label ,
                ["textEdit"] = new JsonObject { ["range"] = range.DeepClone() , ["newText"] = snippet ?? label } ,
            };
            if (snippet is not null)
                item["insertTextFormat"] = 2;
            if (detail is not null)
                item["detail"] = detail;
            if (documentation is not null)
                item["documentation"] = MarkupContent(documentation);
            items.Add(item);
        }

        if (first)
        {
            foreach (var keyword in Keywords.All)
            {
                if (keyword.Type is KeywordType.Param or KeywordType.EndFunc && line.Scope.Function is null)
                    continue;
                add(keyword.Name , CompletionKeyword , "키워드" , keyword.ToMarkdown() , "2");
                if (snippet_support && keyword.Snippet is string snippet)
                    add(keyword.Name + " … " + (keyword.Type is KeywordType.Func ? "endfunc" : "end") , CompletionSnippet , "블록" , keyword.ToMarkdown() , "2" , snippet);
            }
        }
        foreach (var symbol in document.VisibleSymbols(line_index))
        {
            if (!seen.Add(symbol.Name))
                continue;
            int kind = symbol.Kind switch {
                SymbolKind.Function => CompletionFunction,
                SymbolKind.Constant or SymbolKind.Label => CompletionConstant,
                _ => CompletionVariable
            };
            string detail = symbol.Kind is SymbolKind.Function ? Signature(symbol) : symbol.Kind switch {
                SymbolKind.Parameter => "파라미터",
                SymbolKind.Label => "레이블",
                SymbolKind.Constant => "상수",
                SymbolKind.Local => "지역 변수",
                SymbolKind.LoopVariable => "for 반복 변수",
                _ => "변수"
            };
            add(symbol.Name , kind , detail , DescribeSymbol(document , symbol) , "0");
        }
        foreach (var builtin in Builtins.In(document.Options.Commands))
        {
            if (!seen.Add(builtin.Name))
                continue;
            int kind = builtin.Kind switch {
                BuiltinKind.Function => CompletionFunction,
                BuiltinKind.Constant => CompletionConstant,
                _ => CompletionVariable
            };
            add(builtin.Name , kind , builtin.Signatures.Count > 0 ? builtin.Signatures[0].Label(builtin.Name) : builtin.Name , builtin.ToMarkdown() , "1");
        }
        foreach (var host in document.Options.HostNames)
        {
            if (seen.Add(host))
                add(host , CompletionValue , "프로그램이 제공하는 함수/변수" , null , "1");
        }
        return new JsonObject { ["isIncomplete"] = false , ["items"] = items };
    }

    #endregion

    #region hover, 시그니처

    JsonNode? Hover(JyunoDocument document , JsonNode p)
    {
        var (line, column) = ReadPosition(p);
        if (document.NameAt(line , column) is NameOccurrence name)
        {
            if (Describe(document , name) is not string text)
                return null;
            return new JsonObject { ["contents"] = MarkupContent(text) , ["range"] = Range(name) };
        }
        var token = document.TokenAt(line , column);
        if (token is null)
            return null;
        string? content = token.Type switch {
            TokenType.Keyword => Keywords.Get((KeywordType)token.Value)?.ToMarkdown(),
            TokenType.Constant => token.Value switch {
                long => "*정수 (long)*",
                double => "*실수 (double)*",
                string s => $"*문자열 (string) · {s.Length}글자*",
                _ => null
            },
            _ => null
        };
        if (content is null)
            return null;
        return new JsonObject { ["contents"] = MarkupContent(content) , ["range"] = Range(line , token) };
    }

    JsonNode? SignatureHelp(JyunoDocument document , JsonNode p)
    {
        var (line, column) = ReadPosition(p);
        var call = document.CallAt(line , column);
        if (call is null)
            return null;
        //커서 앞에서 시작한 인자의 개수 (커서가 인자에 붙어 있다면 그 인자를 쓰는 중)
        int started = call.Arguments.Count(a => a.Start < column);
        int active = started;
        if (started > 0 && column <= call.Arguments[started - 1].End)
            active = started - 1;

        JsonArray signatures = new();
        if (call.Head.Symbol is Symbol { Kind: SymbolKind.Function } symbol)
        {
            var parameters = symbol.FunctionBlock?.Parameters ?? Array.Empty<string>();
            signatures.Add(new JsonObject {
                ["label"] = Signature(symbol) ,
                ["documentation"] = MarkupContent(DescribeSymbol(document , symbol)) ,
                ["parameters"] = new JsonArray(parameters.Select(name => (JsonNode)new JsonObject { ["label"] = $"({name})" }).ToArray()) ,
            });
        }
        else if (call.Head.Symbol is null && call.Head.Builtin is Builtin { IsFunction: true } builtin)
        {
            foreach (var signature in builtin.Signatures)
            {
                signatures.Add(new JsonObject {
                    ["label"] = signature.Label(builtin.Name) ,
                    ["documentation"] = MarkupContent(builtin.Description) ,
                    ["parameters"] = new JsonArray(signature.Parameters.Select(param => (JsonNode)new JsonObject {
                        ["label"] = $"({param.Name}:{param.Type})"
                    }).ToArray()) ,
                });
            }
        }
        if (signatures.Count is 0)
            return null;
        //인자 개수에 맞는 첫 시그니처 (가변 인자라면 마지막 파라미터를 반복)
        int chosen = signatures.Count - 1;
        for (int i = 0 ; i < signatures.Count ; i++)
        {
            int count = signatures[i]!["parameters"]!.AsArray().Count;
            bool variadic = signatures[i]!["label"]!.GetValue<string>().Contains("...");
            if (active < count || variadic)
            {
                chosen = i;
                break;
            }
        }
        int parameter_count = signatures[chosen]!["parameters"]!.AsArray().Count;
        bool repeat = signatures[chosen]!["label"]!.GetValue<string>().Contains("...");
        int active_parameter = repeat && parameter_count > 0 ? Math.Min(active , parameter_count - 1) : active;
        return new JsonObject {
            ["signatures"] = signatures ,
            ["activeSignature"] = chosen ,
            ["activeParameter"] = active_parameter ,
        };
    }

    #endregion

    #region 정의, 참조, 강조, 심볼

    JsonNode? Definition(JyunoDocument document , JsonNode p)
    {
        var (line, column) = ReadPosition(p);
        if (document.NameAt(line , column)?.Symbol is not Symbol symbol)
            return null;
        string uri = UriOf(p);
        return new JsonArray(symbol.Definitions.OrderBy(d => d.Line).ThenBy(d => d.Start).Select(d => (JsonNode)Location(uri , d)).ToArray());
    }

    JsonNode? References(JyunoDocument document , JsonNode p)
    {
        var (line, column) = ReadPosition(p);
        if (document.NameAt(line , column) is not NameOccurrence name)
            return null;
        bool declarations = p["context"]?["includeDeclaration"]?.GetValue<bool>() ?? true;
        string uri = UriOf(p);
        return new JsonArray(document.OccurrencesOf(name)
            .Where(o => declarations || !o.IsDefinition)
            .Select(o => (JsonNode)Location(uri , o)).ToArray());
    }

    JsonNode? DocumentHighlight(JyunoDocument document , JsonNode p)
    {
        var (line, column) = ReadPosition(p);
        if (document.NameAt(line , column) is not NameOccurrence name)
            return null;
        return new JsonArray(document.OccurrencesOf(name).Select(o => (JsonNode)new JsonObject {
            ["range"] = Range(o) ,
            ["kind"] = o.IsDefinition || o.Role is OccurrenceRole.Deletion ? 3 : 2 ,
        }).ToArray());
    }

    JsonNode? DocumentSymbols(JyunoDocument document , JsonNode p)
    {
        if (!hierarchical_symbols)
        {
            string uri = UriOf(p);
            JsonArray flat = new();
            foreach (var symbol in document.Symbols)
            {
                foreach (var def in symbol.Kind is SymbolKind.Function ? symbol.Definitions.Where(d => d.Role is OccurrenceRole.Function) : symbol.Definitions.Take(1))
                {
                    flat.Add(new JsonObject {
                        ["name"] = symbol.Name ,
                        ["kind"] = SymbolKindOf(symbol) ,
                        ["location"] = Location(uri , def) ,
                        ["containerName"] = symbol.Scope.Function?.Block?.FunctionName ,
                    });
                }
            }
            return flat;
        }
        return SymbolsIn(document , document.Script);
    }

    static int SymbolKindOf(Symbol symbol) => symbol.Kind switch {
        SymbolKind.Function => SymbolFunction,
        SymbolKind.Constant => SymbolConstant,
        SymbolKind.Label => SymbolKey,
        _ => SymbolVariable
    };

    //영역(과 그 안의 for 영역)의 심볼들
    JsonArray SymbolsIn(JyunoDocument document , Scope scope)
    {
        JsonArray result = new();
        IEnumerable<Scope> scopes = document.Scopes.Where(s => s == scope || (s.Kind is ScopeKind.Loop && s.Chain().Skip(1).FirstOrDefault(c => c.Kind is not ScopeKind.Loop) == scope));
        List<(int Line, int Start, JsonObject Item)> items = new();
        foreach (var s in scopes)
        {
            foreach (var symbol in s.Symbols.Values)
            {
                if (symbol.Kind is SymbolKind.Function)
                {
                    foreach (var def in symbol.Definitions.Where(d => d.Role is OccurrenceRole.Function))
                    {
                        var block = document.Lines[def.Line].Block;
                        int end_line = block?.EndLine ?? def.Line;
                        JsonObject item = new() {
                            ["name"] = symbol.Name ,
                            ["detail"] = Signature(symbol) ,
                            ["kind"] = SymbolFunction ,
                            ["range"] = Range(def.Line , 0 , end_line , document.Lines[end_line].Text.Length) ,
                            ["selectionRange"] = Range(def) ,
                        };
                        if (block?.Scope is Scope inner)
                            item["children"] = SymbolsIn(document , inner);
                        items.Add((def.Line , def.Start , item));
                    }
                    continue;
                }
                var first = symbol.FirstDefinition;
                if (first is null)
                    continue;
                items.Add((first.Line , first.Start , new JsonObject {
                    ["name"] = symbol.Name ,
                    ["kind"] = SymbolKindOf(symbol) ,
                    ["range"] = Range(first.Line , 0 , first.Line , document.Lines[first.Line].Text.Length) ,
                    ["selectionRange"] = Range(first) ,
                }));
            }
        }
        foreach (var item in items.OrderBy(i => i.Line).ThenBy(i => i.Start))
            result.Add(item.Item);
        return result;
    }

    #endregion

    #region 이름 바꾸기

    JsonNode? PrepareRename(JyunoDocument document , JsonNode p)
    {
        var (line, column) = ReadPosition(p);
        if (document.NameAt(line , column) is not { Symbol: not null } name)
            return null;
        return new JsonObject { ["range"] = Range(name) , ["placeholder"] = name.Name };
    }

    JsonNode? Rename(JsonNode? p , out string? error)
    {
        error = null;
        string uri = UriOf(p!);
        if (!documents.TryGetValue(uri , out var text))
            return null;
        var document = text.Analyze(options);
        var (line, column) = ReadPosition(p!);
        string new_name = p!["newName"]!.GetValue<string>();
        if (document.NameAt(line , column) is not { Symbol: Symbol symbol })
        {
            error = "내장 명령어나 프로그램이 제공하는 이름은 바꿀수 없습니다.";
            return null;
        }
        if (!Lexer.IsValidName(new_name))
        {
            error = $"'{new_name}'은/는 Jyuno 이름으로 쓸수 없습니다. (공백, 따옴표, 연산자 기호, 키워드, 숫자로 시작하는 이름은 쓸수 없습니다)";
            return null;
        }
        JsonArray edits = new(symbol.Occurrences.Select(o => (JsonNode)new JsonObject {
            ["range"] = Range(o) ,
            ["newText"] = new_name ,
        }).ToArray());
        return new JsonObject { ["changes"] = new JsonObject { [uri] = edits } };
    }

    #endregion

    #region 접기, 포맷팅, 의미 토큰, 빠른 수정

    JsonNode? FoldingRanges(JyunoDocument document , JsonNode p)
    {
        JsonArray ranges = new();
        void add(int start , int end)
        {
            if (end > start)
                ranges.Add(new JsonObject { ["startLine"] = start , ["endLine"] = end , ["kind"] = "region" });
        }
        foreach (var block in document.Blocks)
        {
            int last = block.IsClosed ? block.CloseLine - 1 : block.EndLine;
            if (block.ElseLine >= 0)
            {
                add(block.OpenLine , block.ElseLine - 1);
                add(block.ElseLine , last);
            }
            else
                add(block.OpenLine , last);
        }
        return ranges;
    }

    JsonNode? Formatting(JyunoDocument document , JsonNode p)
    {
        int tab_size = p["options"]?["tabSize"]?.GetValue<int>() ?? 4;
        bool spaces = p["options"]?["insertSpaces"]?.GetValue<bool>() ?? false;
        return new JsonArray(Formatter.Format(document , tab_size , spaces).Select(edit => (JsonNode)new JsonObject {
            ["range"] = Range(edit.Line , 0 , edit.Line , document.Lines[edit.Line].Text.Length) ,
            ["newText"] = edit.NewText ,
        }).ToArray());
    }

    /// <summary>의미 토큰 (LSP 형식으로 인코딩하기 전): (줄, 시작, 길이, 종류, 수식자)</summary>
    public static IEnumerable<(int Line, int Start, int Length, int Type, int Modifiers)> ClassifyTokens(JyunoDocument document)
    {
        const int Keyword = 0, Function = 1, Variable = 2, Parameter = 3, String = 4, Number = 5, Operator = 6, Label = 7;
        const int Declaration = 1, Readonly = 2, DefaultLibrary = 4;
        foreach (var line in document.Lines)
        {
            foreach (var token in line.Tokens)
            {
                switch (token.Type)
                {
                    case TokenType.Keyword:
                        yield return (line.Index , token.Start , token.Length , Keyword , 0);
                        break;
                    case TokenType.Constant:
                        yield return (line.Index , token.Start , token.Length , token.Value is string ? String : Number , 0);
                        break;
                    case TokenType.Prefix:
                        yield return (line.Index , token.Start , token.Length , Operator , 0);
                        break;
                    case TokenType.Name:
                        var name = line.Names.FirstOrDefault(n => n.Token == token);
                        if (name is null)
                            break;
                        int modifiers = name.IsDefinition ? Declaration : 0;
                        int type = Variable;
                        if (name.Symbol is Symbol symbol)
                        {
                            switch (symbol.Kind)
                            {
                                case SymbolKind.Function: type = Function; break;
                                case SymbolKind.Parameter: type = Parameter; break;
                                case SymbolKind.Label: type = Label; break;
                                case SymbolKind.Constant: modifiers |= Readonly; break;
                            }
                        }
                        else if (name.Builtin is Builtin builtin)
                        {
                            modifiers |= DefaultLibrary;
                            if (builtin.IsFunction)
                                type = Function;
                            else if (builtin.Kind is BuiltinKind.Constant)
                                modifiers |= Readonly;
                        }
                        else if (name.IsHostName && name.Role is OccurrenceRole.Call)
                            type = Function;
                        yield return (line.Index , token.Start , token.Length , type , modifiers);
                        break;
                }
            }
        }
    }

    JsonNode? SemanticTokens(JyunoDocument document , JsonNode p)
    {
        JsonArray data = new();
        int previous_line = 0, previous_start = 0;
        foreach (var (line, start, length, type, modifiers) in ClassifyTokens(document))
        {
            int delta_line = line - previous_line;
            int delta_start = delta_line is 0 ? start - previous_start : start;
            data.Add(delta_line);
            data.Add(delta_start);
            data.Add(length);
            data.Add(type);
            data.Add(modifiers);
            previous_line = line;
            previous_start = start;
        }
        return new JsonObject { ["data"] = data };
    }

    JsonNode? CodeActions(JyunoDocument document , JsonNode p)
    {
        string uri = UriOf(p);
        JsonArray actions = new();
        foreach (var node in p["context"]?["diagnostics"]?.AsArray() ?? new JsonArray())
        {
            if (node is not JsonObject diagnostic)
                continue;
            string? code = diagnostic["code"]?.ToString();
            int line = diagnostic["range"]!["start"]!["line"]!.GetValue<int>();
            int start = diagnostic["range"]!["start"]!["character"]!.GetValue<int>();
            int end = diagnostic["range"]!["end"]!["character"]!.GetValue<int>();
            if (line >= document.Lines.Count)
                continue;
            var info = document.Lines[line];
            string? title = null;
            int from = start, to = end;
            switch (code)
            {
                //함수를 인자로 사용: (f)
                case DiagnosticCodes.FunctionAsValue:
                    title = $"괄호로 감싸서 '{info.Text[start..end]}'의 호출 결과를 사용";
                    break;
                //if f a b → if (f a b)
                case DiagnosticCodes.KeywordArguments when info.Tokens.Count > 1 && info.Tokens[1].Type is TokenType.Name && end > start:
                    from = info.Tokens[1].Start;
                    title = $"괄호로 감싸서 '{info.Tokens[1].Value}'의 호출 결과를 사용";
                    break;
            }
            if (title is null || to > info.Text.Length)
                continue;
            string replaced = "(" + info.Text[from..to] + ")";
            actions.Add(new JsonObject {
                ["title"] = title ,
                ["kind"] = "quickfix" ,
                ["diagnostics"] = new JsonArray(diagnostic.DeepClone()) ,
                ["isPreferred"] = true ,
                ["edit"] = new JsonObject {
                    ["changes"] = new JsonObject {
                        [uri] = new JsonArray(new JsonObject {
                            ["range"] = Range(line , from , line , to) ,
                            ["newText"] = replaced ,
                        })
                    }
                } ,
            });
        }
        return actions;
    }

    #endregion
}
