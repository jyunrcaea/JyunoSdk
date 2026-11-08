using System.Globalization;
using System.Text;
using Jyuno.Analysis;

namespace JyunoSdk.NativeJs;

//구문 트리와 분석 결과로 JS 코드를 만듦
//- 이름 해석: 영역마다 JS 지역 변수(칸)를 두고, 칸이 아직 없다는 표시는 U. "칸이 U가 아닌 가장 가까운 영역"이 인터프리터의 이름 해석과 같음
//- 확정 분석: 어느 지점에서 칸이 반드시 있는지(D)/반드시 없는지(U)/모르는지(M)를 추적해서 대부분의 검사를 없앰
//- 내장 함수는 다시 정의할수 없으므로(Native JS 제약) 호출을 인자 개수에 맞는 빠른 함수로 바꿈
//- goto: 대상이 있는 블록을 "for(;;) { switch(pc) { case ... } }" 디스패치 루프로 만들어 continue로 이동
sealed class Emitter
{
    readonly ScriptAnalysis analysis;
    readonly IReadOnlyDictionary<string , GlobalName> globals;
    readonly NativeJsOptions options;

    public Emitter(ScriptAnalysis analysis , IReadOnlyDictionary<string , GlobalName> globals , NativeJsOptions options)
    {
        this.analysis = analysis;
        this.globals = globals;
        this.options = options;
    }

    //─────────── 출력 ───────────
    //줄 번호: Jyuno 줄(0부터), -1: 대응하는 줄 없음, -2: 함수 머리(오류는 호출한 줄)
    readonly List<(string Code, int Line)> output = new();
    void emit(string code , int line) => output.Add((code , line));

    readonly Dictionary<string , (string Var, string What, string Name)> host_refs = new();
    string host_ref(string name , string what)
    {
        string key = what + ":" + name;
        if (!host_refs.TryGetValue(key , out var r))
            host_refs[key] = r = ($"$h{host_refs.Count}" , what , name);
        return r.Var;
    }
    readonly List<string> consts = new();
    string const_ref(string expression)
    {
        consts.Add(expression);
        return $"$k{consts.Count - 1}";
    }

    //─────────── 이름과 리터럴 ───────────
    static string mangle(string name)
    {
        StringBuilder sb = new();
        foreach (char ch in name)
        {
            if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')
                sb.Append(ch);
            else if (ch is '_')
                sb.Append("__");
            else
                sb.Append('_').Append(((int)ch).ToString("x4"));
        }
        return sb.ToString();
    }
    static string slot(Scope scope , string name) => $"${scope.Id}_{mangle(name)}";

    public static string Quote(string text)
    {
        StringBuilder sb = new("\"");
        foreach (char c in text)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20 || c == 0x2028 || c == 0x2029 || c == 0x7f || char.IsSurrogate(c))
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    const long MaxSafe = 9007199254740991;
    string literal(object? value)
    {
        switch (value)
        {
            case null: return "null";
            case bool b: return b ? "true" : "false";
            case string s: return Quote(s);
            case long l:
                if (l is >= -MaxSafe and <= MaxSafe)
                    return l < 0 ? $"({l})" : l.ToString(CultureInfo.InvariantCulture);
                return l < 0 ? $"({l}n)" : $"{l}n";
            case double d:
                if (double.IsNaN(d)) return "NaN";
                if (double.IsPositiveInfinity(d)) return "Infinity";
                if (double.IsNegativeInfinity(d)) return "(-Infinity)";
                //정수 값인 double은 상자에 담아 int와 구별
                if (Math.Floor(d) == d)
                    return const_ref($"new $R.JDouble({(d == 0 && double.IsNegative(d) ? "-0" : d.ToString("R" , CultureInfo.InvariantCulture))})");
                string text = d.ToString("R" , CultureInfo.InvariantCulture);
                return d < 0 ? $"({text})" : text;
            case char c: return Quote(c.ToString());
            default: throw new NotSupportedException($"Native JS로 바꿀수 없는 상수 값입니다: {value.GetType().Name}");
        }
    }

    //─────────── 전역 ───────────
    enum G { None, Builtin, Func, Var, Const }
    G glob(string name , out object? value)
    {
        value = null;
        if (!globals.TryGetValue(name , out GlobalName? global))
            return G.None;
        switch (global.Kind)
        {
            case GlobalKind.Function: return global.Builtin && Builtins.Contains(name) ? G.Builtin : G.Func;
            case GlobalKind.Variable: return G.Var;
            default:
                value = global.Value;
                return G.Const;
        }
    }
    //JS 런타임의 builtins 객체에 있는 이름 (Jyuno의 필수 명령어)
    static readonly HashSet<string> Builtins = new() { "int" , "double" , "string" , "array" , "list" , "length" , "add" , "sub" , "mul" , "div" , "mod" , "equal" , "less" , "greater" , "and" , "or" , "not" , "bool" };
    static readonly Dictionary<(string, int) , string> Fast = new() {
        [("add", 2)] = "add2", [("add", 3)] = "add3", [("sub", 2)] = "sub2", [("mul", 2)] = "mul2", [("mul", 3)] = "mul3",
        [("div", 2)] = "div2", [("mod", 2)] = "mod2", [("equal", 2)] = "equal2", [("less", 2)] = "less2", [("greater", 2)] = "greater2",
        [("and", 2)] = "and2", [("or", 2)] = "or2", [("not", 1)] = "not1", [("length", 1)] = "length1",
    };
    static readonly HashSet<string> BoolResult = new() { "equal" , "less" , "greater" , "bool" };

    string global_value(string name)
    {
        switch (glob(name , out object? value))
        {
            case G.Builtin or G.Func: return $"$noValue({Quote(name)})";
            case G.Const: return literal(value);
            case G.Var: return $"{host_ref(name , "var")}.get()";
            default: return $"$unknownVar({Quote(name)})";
        }
    }

    //─────────── 확정 분석 ───────────
    //상태: 'D' 반드시 있음, 'U' 반드시 없음, 'M' 모름
    sealed class State : Dictionary<Scope , Dictionary<string , char>>
    {
        public State Clone()
        {
            State s = new();
            foreach (var (k , v) in this)
                s[k] = new Dictionary<string , char>(v);
            return s;
        }
        public static State Join(State a , State b)
        {
            State m = a.Clone();
            foreach (var (scope , mb) in b)
            {
                if (!m.TryGetValue(scope , out var ma))
                    continue;
                foreach (var (name , sb) in mb)
                    if (ma.TryGetValue(name , out char sa) && sa != sb)
                        ma[name] = 'M';
            }
            return m;
        }
    }
    sealed class Fn { public int Temps; }
    sealed class Cx
    {
        public Cx(Scope scope , Fn fn , State state) { Scope = scope; Fn = fn; St = state; }
        public readonly Scope Scope;
        public readonly Fn Fn;
        public State St;
        public int Line;
    }

    static char get_status(Cx cx , Scope scope , string name)
    {
        if (!scope.Names.ContainsKey(name))
            return 'U';
        return cx.St.TryGetValue(scope , out var m) && m.TryGetValue(name , out char s) ? s : 'M';
    }
    static void set_status(Cx cx , Scope scope , string name , char s)
    {
        if (scope.Names.ContainsKey(name) && cx.St.TryGetValue(scope , out var m))
            m[name] = s;
    }
    bool trackable(Scope scope) => (scope.Function?.Gotos ?? analysis.TopGotos).Count is 0;
    Dictionary<string , char> fresh(Scope scope , string? for_variable)
    {
        Dictionary<string , char> m = new();
        bool t = trackable(scope);
        foreach (string name in scope.NameOrder)
            m[name] = name == for_variable ? 'D' : t ? 'U' : 'M';
        return m;
    }
    static string temp(Cx cx) => $"$t{cx.Fn.Temps++}";

    sealed record Part(Scope Scope , string Slot , bool Definite , NameKinds Kinds);
    static (List<Part> Parts, bool Definite) chain(Cx cx , string name)
    {
        List<Part> parts = new();
        for (Scope? x = cx.Scope ; x is not null ; x = x.Parent)
        {
            if (!x.Names.TryGetValue(name , out NameKinds kinds))
                continue;
            char s = get_status(cx , x , name);
            if (s is 'U')
                continue;
            parts.Add(new(x , slot(x , name) , s is 'D' , kinds));
            if (s is 'D')
                return (parts, true);
        }
        return (parts, false);
    }
    static bool only_var(NameKinds kinds) => kinds is NameKinds.Variable;

    string read_variable(Cx cx , string name)
    {
        var (parts , definite) = chain(cx , name);
        string? code = definite ? null : global_value(name);
        for (int i = parts.Count - 1 ; i >= 0 ; i--)
        {
            Part p = parts[i];
            string v = only_var(p.Kinds) ? p.Slot : $"$rv({p.Slot}, {Quote(name)})";
            code = code is null ? v : $"({p.Slot} !== U ? {v} : {code})";
        }
        return code!;
    }

    //대입: 지금 영역부터 찾아서 있으면 바꾸고, 없으면 (전역 변수가 아니라면) 지금 영역에 만듦
    string assign_code(Cx cx , string name , string value)
    {
        var (parts , definite) = chain(cx , name);
        string set(Part p) => only_var(p.Kinds) ? $"{p.Slot} = {value}" : $"{p.Slot} = $chkSet({p.Slot}, {Quote(name)}, {value})";
        string? code = null;
        G g = glob(name , out _);
        if (!definite)
        {
            string current = slot(cx.Scope , name);
            code = g switch {
                G.Var => $"{host_ref(name , "var")}.set({value})",
                G.Const => "$constAssign()",
                G.Builtin or G.Func => $"$notVar({Quote(name)})",
                _ => $"{current} = {value}"
            };
        }
        for (int i = parts.Count - 1 ; i >= 0 ; i--)
        {
            Part p = parts[i];
            code = code is null ? set(p) : $"({p.Slot} !== U ? {set(p)} : {code})";
        }
        if (!definite)
        {
            char cs = get_status(cx , cx.Scope , name);
            if (parts.Count is 0 && g is G.None)
                set_status(cx , cx.Scope , name , 'D');
            else if (cs is 'U' && g is G.None)
                set_status(cx , cx.Scope , name , 'M');
        }
        return code!;
    }

    //─────────── 식 ───────────
    readonly record struct Code(string Text , bool Bool = false , bool Early = false , string? Statement = null);

    Code gen(Cx cx , Expression e)
    {
        switch (e)
        {
            case ConstantExpression c: return new(literal(c.Value));
            case VariableExpression v: return new(read_variable(cx , v.Name));
            case InvalidExpression: return new("$cannotProcess()");
            case UnclosedParenthesisExpression u: return new($"({gen(cx , u.Inner).Text}, $parenError())");
            case ParenthesisExpression p:
            {
                Code inner = gen(cx , p.Inner);
                if (!inner.Early)
                    return inner;
                string t = temp(cx);
                return new($"(({t} = {inner.Text}) instanceof Early ? $parenError() : {t})");
            }
            case ConstantDefinitionExpression d:
            {
                string code = $"({slot(cx.Scope , d.Name)} = new JConst({literal(d.Value)}), null)";
                set_status(cx , cx.Scope , d.Name , 'D');
                return new(code);
            }
            case LabelExpression l:
            {
                string code = $"({slot(cx.Scope , l.Name)} = {cx.Line}, null)";
                set_status(cx , cx.Scope , l.Name , 'D');
                return new(code);
            }
            case AssignExpression a:
            {
                if (options.BlockSubstitute)
                    return new("$noSubstitute()");
                Code rhs = gen(cx , a.Value);
                string t = temp(cx);
                if (rhs.Early)
                {
                    string a1 = assign_code(cx , a.Name , $"{t}.v");
                    string a2 = assign_code(cx , a.Name , t);
                    return new($"(({t} = {rhs.Text}) instanceof Early ? ({a1}, EARLY_NULL) : ({a2}, null))" , Early: true);
                }
                var (parts , definite) = chain(cx , a.Name);
                if (definite && parts.Count is 1 && only_var(parts[0].Kinds))
                    return new($"({parts[0].Slot} = {rhs.Text}, null)" , Statement: $"{parts[0].Slot} = {rhs.Text}");
                string assign = assign_code(cx , a.Name , t);
                return new($"({t} = {rhs.Text}, {assign}, null)");
            }
            case CallExpression call: return gen_call(cx , call);
            //삭제는 Native JS 제약으로 미리 걸러짐
            case RemoveExpression: throw new InvalidOperationException("삭제 연산은 Native JS로 바꿀수 없습니다.");
        }
        throw new InvalidOperationException("알수없는 식 " + e.GetType().Name);
    }

    List<string> gen_args(Cx cx , IReadOnlyList<Expression> args) => args.Select(a => gen(cx , a).Text).ToList();

    //조건부로만 계산되는 코드 (상태를 합침)
    T conditional<T>(Cx cx , Func<T> f)
    {
        State before = cx.St.Clone();
        T r = f();
        cx.St = State.Join(before , cx.St);
        return r;
    }

    //변수 값에 대한 호출: 인자가 있으면 배열/리스트의 원소 읽기/쓰기
    Code variable_call(Cx cx , CallExpression e , string value)
    {
        if (!e.HasArguments)
            return new(value);
        string t = temp(cx);
        string list_part = conditional(cx , () => {
            var a = gen_args(cx , e.Arguments);
            return a.Count switch {
                0 => t,
                1 => $"$lget({t}, {a[0]})",
                2 => options.BlockSubstitute ? $"({a[0]}, {a[1]}, $noSubstitute())" : $"$lset({t}, {a[0]}, {a[1]})",
                _ => $"({string.Join(", " , a)}, $badIndexArgs())"
            };
        });
        string other = e.Closed ? $"new Early({t})" : t;
        return new($"(({t} = {value}) instanceof JList ? {list_part} : {other})" , Early: e.Closed);
    }

    Code gen_call(Cx cx , CallExpression e)
    {
        var (parts , definite) = chain(cx , e.Name);
        object? value = null;
        G g = definite ? G.None : glob(e.Name , out value);
        if (parts.Count is 0)
        {
            switch (g)
            {
                case G.Builtin:
                {
                    var a = gen_args(cx , e.Arguments);
                    if (Fast.TryGetValue((e.Name, a.Count) , out string? fast))
                        return new($"{fast}({string.Join(", " , a)})" , Bool: BoolResult.Contains(e.Name));
                    if (e.Name is "bool" && a.Count is 1)
                        return new($"truthy({a[0]})" , Bool: true);
                    return new($"$B[{Quote(e.Name)}]([{string.Join(", " , a)}])" , Bool: BoolResult.Contains(e.Name));
                }
                case G.Func: return new($"{host_ref(e.Name , "fn")}([{string.Join(", " , gen_args(cx , e.Arguments))}])");
                case G.Var: return variable_call(cx , e , $"{host_ref(e.Name , "var")}.get()");
                case G.Const: return variable_call(cx , e , literal(value));
                default: return new($"$unknownCall({Quote(e.Name)})");
            }
        }
        //이름이 함수인지 변수인지 실행할때 정해지는 경우
        NameKinds kinds = NameKinds.None;
        foreach (Part p in parts)
            kinds |= p.Kinds;
        bool holder_fallback = false;
        string? fallback = null;
        if (!definite)
        {
            switch (g)
            {
                case G.Builtin or G.Func:
                    //Native JS 제약으로 전역 함수 이름은 다시 정의할수 없으므로 여기에 오지 않음
                    kinds |= NameKinds.Function;
                    fallback = host_ref(e.Name , "holder");
                    break;
                case G.Var or G.Const:
                    kinds |= NameKinds.Variable;
                    holder_fallback = true;
                    fallback = host_ref(e.Name , "holder");
                    break;
                default:
                    fallback = $"$unknownCall({Quote(e.Name)})";
                    break;
            }
        }
        string? holder = fallback;
        for (int i = parts.Count - 1 ; i >= 0 ; i--)
        {
            Part p = parts[i];
            holder = holder is null ? p.Slot : $"({p.Slot} !== U ? {p.Slot} : {holder})";
        }
        if (kinds is NameKinds.Function && !holder_fallback)
            return new($"{holder}({string.Join(", " , gen_args(cx , e.Arguments))})");
        if (!kinds.HasFlag(NameKinds.Function) && !kinds.HasFlag(NameKinds.Constant) && !holder_fallback)
            return variable_call(cx , e , holder!);
        //일반형: 함수면 호출, 변수면 값 (인자는 필요할 때만 계산)
        var args = conditional(cx , () => gen_args(cx , e.Arguments));
        return new($"$call({holder}, {(e.HasArguments ? "true" : "false")}, {(e.Closed ? "true" : "false")}, () => [{string.Join(", " , args)}])" , Early: e.Closed && e.HasArguments);
    }

    string condition(Cx cx , IReadOnlyList<Expression> args)
    {
        if (args.Count is 0)
            return "false";
        var a = args.Select(x => gen(cx , x)).ToList();
        if (a.Count is 1)
            return a[0].Bool ? a[0].Text : $"truthy({a[0].Text})";
        string t = temp(cx);
        return $"truthy(({t} = {a[0].Text}, {string.Join(", " , a.Skip(1).Select(x => x.Text))}, {t}))";
    }
    string first_of(Cx cx , IReadOnlyList<Expression> args)
    {
        if (args.Count is 0)
            return "null";
        var a = gen_args(cx , args);
        if (a.Count is 1)
            return a[0];
        string t = temp(cx);
        return $"({t} = {a[0]}, {string.Join(", " , a.Skip(1))}, {t})";
    }

    //─────────── goto 디스패치 ───────────
    sealed class Dispatch { public int Id; public readonly Dictionary<int , int> Cases = new(); }
    readonly Dictionary<Block , Dispatch> dispatches = new();
    readonly Dictionary<GotoStatement , List<(long Line, string Jump)>> jumps = new();

    void plan_gotos(IEnumerable<GotoStatement> gotos)
    {
        foreach (GotoStatement g in gotos)
        {
            List<(long, string)> list = new();
            foreach (GotoTarget target in g.Targets)
            {
                string jump;
                switch (target.Kind)
                {
                    case GotoTargetKind.Exit: jump = "return null;"; break;
                    case GotoTargetKind.Continue: jump = $"continue $L{target.Statement!.Line};"; break;
                    default:
                    {
                        Block block = target.Statement!.Parent;
                        if (!dispatches.TryGetValue(block , out Dispatch? d))
                            dispatches[block] = d = new() { Id = dispatches.Count + 1 };
                        int k = target.Kind is GotoTargetKind.Before ? target.Statement.Index : target.Statement.Index + 1;
                        if (!d.Cases.TryGetValue(k , out int c))
                            d.Cases[k] = c = d.Cases.Count + 1;
                        jump = $"{{ $pc{d.Id} = {c}; continue $D{d.Id}; }}";
                        break;
                    }
                }
                list.Add((target.Line, jump));
            }
            jumps[g] = list;
        }
    }

    //─────────── 문장 ───────────
    void gen_block(Cx cx , Block block)
    {
        var statements = block.Statements;
        if (dispatches.TryGetValue(block , out Dispatch? d))
        {
            string pc = $"$pc{d.Id}", label = $"$D{d.Id}";
            emit($"{{ let {pc} = 0; {label}: for (;;) {{ switch ({pc}) {{ case 0:" , statements.Count > 0 ? statements[0].Line : block.EndLine);
            for (int i = 0 ; i < statements.Count ; i++)
            {
                if (d.Cases.TryGetValue(i , out int c))
                    emit($"case {c}:" , statements[i].Line);
                gen_statement(cx , statements[i]);
            }
            if (d.Cases.TryGetValue(statements.Count , out int last))
                emit($"case {last}:" , block.EndLine);
            emit("} break; } }" , block.EndLine);
        }
        else
        {
            foreach (Statement s in statements)
                gen_statement(cx , s);
        }
    }

    static string grammar(int line , string message) => $"$grammar({line}, {Quote(message)});";

    void gen_statement(Cx cx , Statement s)
    {
        cx.Line = s.Line;
        if (s.TokenError is string error)
        {
            //키워드 줄의 토큰화 오류는 그 줄을 실행할때 문법 오류 (블록이라면 본문은 실행하지 않음)
            emit(grammar(s.Line , error) , s.Line);
            return;
        }
        switch (s)
        {
            case ExpressionStatement expression:
            {
                Code r = gen(cx , expression.Expression);
                emit((r.Statement ?? r.Text) + ";" , s.Line);
                return;
            }
            case IfStatement branch:
            {
                emit($"if ({condition(cx , branch.Arguments)}) {{" , s.Line);
                State entry = cx.St.Clone();
                gen_block(cx , branch.Then);
                if (branch.ElseLine >= 0 && branch.ElseTokenError is string else_error)
                    emit(grammar(branch.ElseLine , else_error) , branch.ElseLine);
                if (branch.ElseLine < 0 && branch.EndTokenError is string end_error)
                    emit(grammar(branch.EndLine , end_error) , branch.EndLine);
                State after_then = cx.St;
                if (branch.Else is not null)
                {
                    emit("} else {" , branch.ElseLine);
                    cx.St = entry.Clone();
                    gen_block(cx , branch.Else);
                    if (branch.EndTokenError is string end_error2)
                        emit(grammar(branch.EndLine , end_error2) , branch.EndLine);
                    cx.St = State.Join(after_then , cx.St);
                }
                else
                    cx.St = State.Join(after_then , entry);
                emit("}" , branch.EndLine);
                return;
            }
            case LoopStatement loop:
            {
                //반복문 머리: 본문에서 만들수 있는 이름은 '모름'
                foreach (string name in ScriptAnalysis.NamesCreatedIn(loop.Body.Statements))
                    if (get_status(cx , cx.Scope , name) is 'U')
                        set_status(cx , cx.Scope , name , 'M');
                State head = cx.St.Clone();
                string label = $"$L{s.Line}";
                if (loop.Kind is StatementKind.While)
                    emit($"{label}: while ({condition(cx , loop.Arguments)}) {{" , s.Line);
                else
                {
                    string n = $"$n{s.Line}";
                    var a = gen_args(cx , loop.Arguments);
                    string count = a.Count is 1 ? $"$rc1({a[0]})" : $"repeatCount([{string.Join(", " , a)}])";
                    emit($"{{ let {n} = {count}; {label}: for (; {n} > 0; {n}--) {{" , s.Line);
                }
                State after_head = cx.St.Clone();
                gen_block(cx , loop.Body);
                if (loop.EndTokenError is string end_error)
                    emit(grammar(loop.EndLine , end_error) , loop.EndLine);
                emit(loop.Kind is StatementKind.While ? "}" : "} }" , loop.EndLine);
                cx.St = State.Join(head , after_head);
                return;
            }
            case ForStatement each:
            {
                if (each.VariableName is null)
                {
                    emit("$forSyntax();" , s.Line);
                    return;
                }
                string label = $"$L{s.Line}";
                var a = gen_args(cx , each.Arguments);
                string it = $"$it{s.Line}", i = $"$i{s.Line}";
                string source = a.Count is 1 ? a[0] : $"({(a.Count > 0 ? string.Join(", " , a) + ", " : "")}$forArity())";
                Scope inner = each.InnerScope;
                State inner_state = cx.St.Clone();
                inner_state[inner] = fresh(inner , each.VariableName);
                Cx icx = new(inner , cx.Fn , inner_state);
                var declarations = inner.NameOrder.Select(n => n == each.VariableName ? $"{slot(inner , n)} = {it}[{i}]" : $"{slot(inner , n)} = U");
                emit($"{{ const {it} = forItems({source}); {label}: for (let {i} = 0; {i} < {it}.length; {i}++) {{ let {string.Join(", " , declarations)};" , s.Line);
                gen_block(icx , each.Body);
                if (each.EndTokenError is string end_error)
                    emit(grammar(each.EndLine , end_error) , each.EndLine);
                emit("} }" , each.EndLine);
                return;
            }
            case FunctionStatement function:
            {
                if (function.Name is null)
                {
                    emit("$funcSyntax();" , s.Line);
                    return;
                }
                string function_slot = slot(cx.Scope , function.Name);
                set_status(cx , cx.Scope , function.Name , 'D');
                //함수 본문: 바깥 영역의 '없음'은 호출할 때 생길수 있으므로 '모름'
                State st = new();
                foreach (var (scope , m) in cx.St)
                    st[scope] = m.ToDictionary(o => o.Key , o => o.Value is 'U' ? 'M' : o.Value);
                st[function.InnerScope] = fresh(function.InnerScope , null);
                Fn fn = new();
                Cx fcx = new(function.InnerScope , fn , st);
                var parameters = Enumerable.Range(0 , function.ParameterCount).Select(i => $"$a{i}");
                var declarations = function.InnerScope.NameOrder.Select(n => $"{slot(function.InnerScope , n)} = U").ToList();
                string fname = mangle(function.Name);
                emit($"{function_slot} = function $f_{(fname.Length > 40 ? fname[..40] : fname)}({string.Join(", " , parameters)}) {{" , s.Line);
                emit("if ($depth >= 200) $tooDeep(); $depth++; try {" , -2);
                int temp_at = output.Count;
                emit("" , s.Line);
                if (declarations.Count > 0)
                    emit($"let {string.Join(", " , declarations)};" , s.Line);
                gen_block(fcx , function.Body);
                emit("return null; } finally { $depth--; } };" , function.EndLine);
                if (fn.Temps > 0)
                    output[temp_at] = ($"let {string.Join(", " , Enumerable.Range(0 , fn.Temps).Select(i => $"$t{i}"))};" , s.Line);
                return;
            }
            case GotoStatement g:
            {
                if (g.Arguments.Count is 0)
                {
                    emit("$gotoMissing();" , s.Line);
                    return;
                }
                string v = first_of(cx , g.Arguments);
                string tv = $"$g{s.Line}";
                StringBuilder code = new($"{{ const {tv} = {v}; ");
                foreach (var (line , jump) in jumps[g])
                    code.Append($"if ({tv} === {line}) {jump} ");
                code.Append($"$gotoFail({tv}, {s.Line}); }}");
                emit(code.ToString() , s.Line);
                return;
            }
            case BreakStatement b:
                emit($"break $L{b.Loop.Line};" , s.Line);
                return;
            case ParameterStatement p:
                if (p.Name is null)
                {
                    emit("$paramSyntax();" , s.Line);
                    return;
                }
                emit($"{slot(cx.Scope , p.Name)} = ($a{p.ParameterIndex} === undefined ? null : $a{p.ParameterIndex});" , s.Line);
                set_status(cx , cx.Scope , p.Name , 'D');
                return;
            case LocalStatement local:
            {
                if (local.Name is null)
                {
                    emit("$localSyntax();" , s.Line);
                    return;
                }
                if (options.BlockSubstitute)
                {
                    emit("$noSubstitute();" , s.Line);
                    return;
                }
                if (local.Malformed)
                {
                    emit("$localForm();" , s.Line);
                    return;
                }
                string v = local.Value is not null ? gen(cx , local.Value).Text : "null";
                string local_slot = slot(cx.Scope , local.Name);
                if (only_var(cx.Scope.Names[local.Name]))
                    emit($"{local_slot} = {v};" , s.Line);
                else
                    emit($"{local_slot} = $localSet({local_slot}, {Quote(local.Name)}, {v});" , s.Line);
                set_status(cx , cx.Scope , local.Name , 'D');
                return;
            }
            case TokenErrorStatement:
                return; //위에서 처리
        }
        switch (s.Kind)
        {
            case StatementKind.Empty:
                return;
            case StatementKind.Return:
                emit($"return {first_of(cx , s.Arguments)};" , s.Line);
                return;
        }
        throw new InvalidOperationException("알수없는 문장 " + s.Kind);
    }

    //─────────── 전체 ───────────
    public string Emit()
    {
        Block root = analysis.Root!;
        plan_gotos(analysis.TopGotos);
        foreach (FunctionStatement f in analysis.Functions)
            plan_gotos(f.Gotos);

        Fn top_fn = new();
        State top_state = new() { [analysis.TopScope] = fresh(analysis.TopScope , null) };
        Cx tcx = new(analysis.TopScope , top_fn , top_state);
        emit("return function $main() {" , -1);
        int temp_at = output.Count;
        emit("" , -1);
        var declarations = analysis.TopScope.NameOrder.Select(n => $"{slot(analysis.TopScope , n)} = U").ToList();
        if (declarations.Count > 0)
            emit($"let {string.Join(", " , declarations)};" , -1);
        gen_block(tcx , root);
        emit("return null; };" , -1);
        if (top_fn.Temps > 0)
            output[temp_at] = ($"let {string.Join(", " , Enumerable.Range(0 , top_fn.Temps).Select(i => $"$t{i}"))};" , -1);

        //머리말: 런타임/호스트 연결
        List<string> head = new() {
            "const { U, JList, JConst, Early, EARLY_NULL, truthy, forItems, repeatCount, add2, add3, sub2, mul2, mul3, div2, mod2, equal2, less2, greater2, and2, or2, not1, length1 } = $R;",
            "const { $B, $rv, $chkSet, $localSet, $call, $lget, $lset, $rc1, $grammar, $noValue, $unknownVar, $unknownCall, $notVar, $constAssign, $noSubstitute, $cannotProcess, $parenError, $badIndexArgs, $forSyntax, $forArity, $funcSyntax, $paramSyntax, $localSyntax, $localForm, $gotoMissing, $gotoFail, $tooDeep } = $X;",
            "let $depth = 0;"
        };
        foreach (var r in host_refs.Values)
            head.Add($"const {r.Var} = $H.{r.What}({Quote(r.Name)});");
        for (int i = 0 ; i < consts.Count ; i++)
            head.Add($"const $k{i} = {consts[i]};");
        var body = head.Select(code => (code, -1)).Concat(output).ToList();

        //파일 구성: 머리 주석, 런타임, 프로그램 함수, 메타 정보
        StringBuilder sb = new();
        int line = 1;
        void write(string text)
        {
            sb.Append(text).Append('\n');
            foreach (char c in text)
                if (c is '\n')
                    line++;
            line++;
        }
        string name = options.Name;
        write($"// {name} → Jyuno Native JS (JyunoSdk {typeof(Emitter).Assembly.GetName().Version?.ToString(3)}, Jyuno {typeof(ScriptAnalysis).Assembly.GetName().Version?.ToString(3)})");
        write("// Node.js: node <이 파일>.js  ·  브라우저/모듈: JyunoProgram.run({ input, write, host })");
        write("var JyunoProgram = (function () {");
        write("\"use strict\";");
        write(NativeJsCompiler.RuntimeSource.TrimEnd('\n'));
        int probe_line = line;
        write("const $probe = new Error();");
        write("return createProgram(function ($R, $X, $H) {");
        int body_line = line;
        foreach (var (code , _) in body)
            write(code);
        write("}, {");
        write($"  name: {Quote(name)}, lineCount: {analysis.LineCount}, blockSubstitute: {(options.BlockSubstitute ? "true" : "false")},");
        write($"  functions: [{string.Join(", " , analysis.Functions.Select(f => $"[{f.Line}, {f.EndLine}]"))}],");
        write($"  probe: $probe, probeLine: {probe_line}, bodyLine: {body_line},");
        write($"  lineMap: [{string.Join("," , body.Select(o => o.Item2))}],");
        write("});");
        write("})();");
        write("if (typeof module === 'object' && module && module.exports) module.exports = JyunoProgram;");
        write("if (typeof require === 'function' && typeof module === 'object' && require.main === module) JyunoProgram.main();");
        return sb.ToString();
    }
}
