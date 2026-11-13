using System.Globalization;
using System.Text;
using Jyuno.Analysis;
using Jyuno.Language;

namespace JyunoSdk.NativeJs;

//구문 트리와 분석 결과로 JS 코드를 만듦
//- 이름 해석: 영역마다 JS 지역 변수(칸)를 두고, 칸이 아직 없다는 표시는 U. "칸이 U가 아닌 가장 가까운 영역"이 인터프리터의 이름 해석과 같음
//- 확정 분석: 어느 지점에서 칸이 반드시 있는지(D)/반드시 없는지(U)/모르는지(M)를 추적해서 대부분의 검사를 없앰
//- 내장 함수는 다시 정의할수 없으므로(Native JS 제약) 호출을 인자 개수에 맞는 빠른 함수로 바꿈
//- goto: 대상이 있는 블록을 "for(;;) { switch(pc) { case ... } }" 디스패치 루프로 만들어 continue로 이동
//- 연산자 (Jyuno 1.6.0): 런타임의 함수(add2, opLt 등)로 바꾸고, &&, ||, ??, ?:는 JS의 같은 연산자로 단락 평가
//  구조체가 다시 정의한 연산자(func +)는 구조체 함수처럼 구조체 값의 속성 (f_ + 기호를 바꾼 이름)
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
        //구조체 (Jyuno 1.5.0): 구조체가 없는 스크립트는 이전과 똑같은 코드를 만듦
        foreach (FunctionStatement f in analysis.Functions)
        {
            if (!f.IsStruct || f.Name is null)
                continue;
            structs.Add(f);
            foreach (var (name , kinds) in f.InnerScope.Names)
                member_kinds[name] = member_kinds.TryGetValue(name , out NameKinds k) ? k | kinds : kinds;
        }
        //구조체가 다시 정의한 연산자 (Jyuno 1.6.0): 구조체 생성자에서 'func +' 등으로 정의한 것만 연산자로 쓰임
        foreach (FunctionStatement f in analysis.Functions)
        {
            if (f.IsStruct || f.Operator is not string symbol || f.TokenError is not null || !Grammar.IsOverloadable(symbol))
                continue;
            if (f.InnerScope.Parent is not { Kind: ScopeKind.Struct } owner)
                continue;
            if (!operators.TryGetValue(owner , out var symbols))
                operators[owner] = symbols = new();
            if (!symbols.Contains(symbol))
                symbols.Add(symbol);
            defined_operators.Add(symbol);
        }
    }
    //구조체 영역 → 그 구조체가 다시 정의한 연산자 기호들, 어느 구조체든 다시 정의한 연산자 기호
    readonly Dictionary<Scope , List<string>> operators = new();
    readonly HashSet<string> defined_operators = new();
    bool has_operators => defined_operators.Count > 0;
    //연산자 식을 썼는지 (머리말에 런타임 함수를 가져옴)
    bool uses_operators;
    IEnumerable<string> operator_props(Scope inner) => operators.TryGetValue(inner , out var symbols) ? symbols.Select(prop) : Enumerable.Empty<string>();
    //struct 문장들과, 구조체 영역에 만들어질수 있는 이름(멤버) → 종류 (모든 구조체를 합침)
    readonly List<FunctionStatement> structs = new();
    readonly Dictionary<string , NameKinds> member_kinds = new();
    bool has_structs => structs.Count > 0;

    //─────────── 출력 ───────────
    //줄 번호: Jyuno 줄(0부터), -1: 대응하는 줄 없음, -2: 함수 머리(오류는 호출한 줄)
    List<(string Code, int Line)> output = new();
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
    //구조체 영역(구조체 값 하나)의 이름은 구조체 값 객체의 속성
    static string slot(Scope scope , string name) => scope.Kind is ScopeKind.Struct ? $"{self_of(scope)}.{prop(name)}" : $"${scope.Id}_{mangle(name)}";
    static string self_of(Scope scope) => $"$s{scope.Id}";
    static string prop(string name) => "f_" + mangle(name);

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
    static readonly HashSet<string> Builtins = create_builtins();
    static HashSet<string> create_builtins()
    {
        HashSet<string> names = new() { "int" , "double" , "string" , "array" , "list" , "length" , "add" , "sub" , "mul" , "div" , "mod" , "equal" , "less" , "greater" , "and" , "or" , "not" , "bool" };
        //컬렉션 함수 (Jyuno 1.4.1~1.4.2)
        foreach (string kind in new[] { "array" , "list" })
            foreach (string name in new[] { "sort" , "reverse" , "shuffle" , "contains" , "indexof" , "slice" , "join" , "new" ,
                "copy" , "lastindexof" , "count" , "distinct" , "binary_search" , "fill" , "min" , "max" , "sum" , "range" })
                names.Add($"{kind}.{name}");
        foreach (string name in new[] { "push" , "pop" , "insert" , "removeat" , "remove" , "clear" })
            names.Add($"list.{name}");
        //문자열 함수 (Jyuno 1.4.2)
        foreach (string name in new[] { "concat" , "join" , "format" , "compare" , "equals" , "isnullorempty" , "isnullorwhitespace" ,
            "indexof" , "lastindexof" , "contains" , "startswith" , "endswith" , "replace" , "substring" , "insert" , "remove" ,
            "toupper" , "tolower" , "trim" , "trimstart" , "trimend" , "padleft" , "padright" , "split" , "tochararray" , "charcode" , "fromcharcode" })
            names.Add($"string.{name}");
        return names;
    }
    static readonly Dictionary<(string, int) , string> Fast = new() {
        [("add", 2)] = "add2", [("add", 3)] = "add3", [("sub", 2)] = "sub2", [("mul", 2)] = "mul2", [("mul", 3)] = "mul3",
        [("div", 2)] = "div2", [("mod", 2)] = "mod2", [("equal", 2)] = "equal2", [("less", 2)] = "less2", [("greater", 2)] = "greater2",
        [("and", 2)] = "and2", [("or", 2)] = "or2", [("not", 1)] = "not1", [("length", 1)] = "length1",
    };
    static readonly HashSet<string> BoolResult = new() { "equal" , "less" , "greater" , "bool" , "array.contains" , "list.contains" , "list.remove" ,
        "string.equals" , "string.isnullorempty" , "string.isnullorwhitespace" , "string.contains" , "string.startswith" , "string.endswith" };

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
    sealed class Fn
    {
        public int Temps;
        public string? Self;
        //지금 만들고 있는 코드를 감싼 반복문의 수 (다시 실행될수 있는 param을 구별)
        public int Loops;
    }
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

    //Test: 이 영역에 이름이 있는지 검사하는 식 (null이면 'Slot !== U'), Self: 구조체 함수라면 this로 넘길 구조체 값
    sealed record Part(Scope Scope , string Slot , bool Definite , NameKinds Kinds , string? Test = null , string? Self = null);
    static string test(Part p) => p.Test ?? $"{p.Slot} !== U";
    (List<Part> Parts, bool Definite) chain(Cx cx , string name)
    {
        List<Part> parts = new();
        for (Scope? x = cx.Scope ; x is not null ; x = x.Parent)
        {
            //구조체 값을 담은 변수의 멤버 이름 (prefix.path): 인터프리터에서는 같은 영역에 선언된 이름
            if (has_structs && name.Contains('.'))
                alias_parts(cx , x , name , parts);
            if (!x.Names.TryGetValue(name , out NameKinds kinds))
                continue;
            char s = get_status(cx , x , name);
            if (s is 'U')
                continue;
            parts.Add(new(x , slot(x , name) , s is 'D' , kinds , Self: x.Kind is ScopeKind.Struct ? self_of(x) : null));
            if (s is 'D')
                return (parts, true);
        }
        return (parts, false);
    }

    //x 영역의 변수(prefix)에 구조체 값이 있다면 prefix.m1.m2...는 그 구조체 값의 멤버
    //짧은 prefix부터 (같은 이름을 직접 만든 것보다 먼저: 구조체 값을 넣을때 직접 만든 같은 이름은 지워짐)
    void alias_parts(Cx cx , Scope x , string name , List<Part> parts)
    {
        for (int k = name.IndexOf('.') ; k > 0 ; k = name.IndexOf('.' , k + 1))
        {
            string prefix = name[..k];
            string[] path = name[(k + 1)..].Split('.');
            if (!x.Names.ContainsKey(prefix) || get_status(cx , x , prefix) is 'U')
                continue;
            if (path.Any(m => m.Length is 0 || !member_kinds.ContainsKey(m)) || path[..^1].Any(m => !member_kinds[m].HasFlag(NameKinds.Variable)))
                continue;
            string o = temp(cx), h = temp(cx);
            StringBuilder t = new($"(({o} = {slot(x , prefix)}) instanceof JStruct && ");
            foreach (string m in path[..^1])
                t.Append($"({o} = {o}.{prop(m)}) instanceof JStruct && ");
            t.Append($"({h} = {o}.{prop(path[^1])}) !== undefined && {h} !== U)");
            parts.Add(new(x , $"{o}.{prop(path[^1])}" , false , member_kinds[path[^1]] , t.ToString() , o));
        }
    }
    static bool callable(NameKinds kinds) => kinds is not NameKinds.None && (kinds & ~(NameKinds.Function | NameKinds.Struct)) is NameKinds.None;
    static bool only_var(NameKinds kinds) => kinds is NameKinds.Variable;

    string read_variable(Cx cx , string name)
    {
        var (parts , definite) = chain(cx , name);
        string? code = definite ? null : global_value(name);
        for (int i = parts.Count - 1 ; i >= 0 ; i--)
        {
            Part p = parts[i];
            string v = only_var(p.Kinds) ? p.Slot : $"$rv({p.Slot}, {Quote(name)})";
            code = code is null ? v : $"({test(p)} ? {v} : {code})";
        }
        return code!;
    }

    //대입: 지금 영역부터 찾아서 있으면 바꾸고, 없으면 (전역 변수가 아니라면) 지금 영역에 만듦
    string assign_code(Cx cx , string name , string value)
    {
        var (parts , definite) = chain(cx , name);
        string set(Part p) => bind_clear(cx , p.Scope , name , p.Slot , only_var(p.Kinds) ? $"{p.Slot} = {value}" : $"{p.Slot} = $chkSet({p.Slot}, {Quote(name)}, {value})");
        string? code = null;
        G g = glob(name , out _);
        if (!definite)
        {
            string current = slot(cx.Scope , name);
            code = g switch {
                G.Var => $"{host_ref(name , "var")}.set({value})",
                G.Const => "$constAssign()",
                G.Builtin or G.Func => $"$notVar({Quote(name)})",
                _ => bind_clear(cx , cx.Scope , name , current , $"{order_push(cx , cx.Scope , name , 'U')}{current} = {value}")
            };
        }
        for (int i = parts.Count - 1 ; i >= 0 ; i--)
        {
            Part p = parts[i];
            //뒤쪽 코드는 쉼표로 이어진 식일수 있으므로 괄호로 묶음 (묶지 않으면 조건과 관계없이 실행됨)
            code = code is null ? set(p) : $"({test(p)} ? {set(p)} : ({code}))";
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

    //구조체 영역에 이름이 새로 만들어지는 지점: 만들어진 순서(필드 순서)를 기록. status는 만들기 전 상태
    string order_push(Cx cx , Scope scope , string name , char? status = null)
    {
        if (scope.Kind is not ScopeKind.Struct)
            return "";
        char s = status ?? get_status(cx , scope , name);
        string push = $"{self_of(scope)}.$o.push({Quote(prop(name))})";
        return s switch { 'D' => "" , 'U' => push + ", " , _ => $"({slot(scope , name)} === U && {push}), " };
    }
    //변수(name)에 값을 넣는 코드(code) 뒤에: 구조체 값이라면 같은 영역에 직접 만든 'name.멤버' 이름을 지움 (인터프리터에서 덮어써짐)
    string bind_clear(Cx cx , Scope scope , string name , string target , string code)
    {
        if (!has_structs)
            return code;
        List<string> clears = new();
        foreach (string other in scope.NameOrder)
        {
            if (other.Length <= name.Length + 1 || !other.StartsWith(name + "."))
                continue;
            string[] path = other[(name.Length + 1)..].Split('.');
            if (path.Any(m => !member_kinds.ContainsKey(m)))
                continue;
            string s = slot(scope , other);
            clears.Add($"{s} = $unbindIf({target}, {const_ref($"[{string.Join(", " , path.Select(m => Quote(prop(m))))}]")}, {s})");
            if (get_status(cx , scope , other) is 'D')
                set_status(cx , scope , other , 'M');
        }
        return clears.Count is 0 ? code : $"({code}, {string.Join(", " , clears)})";
    }
    //이름(name)을 지금 영역에 직접 정의(func, param, local, struct)할 때: 그 이름이 지금 구조체 값의 멤버 이름이라면 Native JS 제약
    //(인터프리터는 멤버 이름 하나만 덮어쓰지만, Native JS는 멤버 이름을 사용하는 곳에서 찾으므로 지원하지 않음)
    string alias_guard(Cx cx , string name , int line)
    {
        if (!has_structs || !name.Contains('.'))
            return "";
        List<Part> parts = new();
        alias_parts(cx , cx.Scope , name , parts);
        if (parts.Count is 0)
            return "";
        emit($"if ({string.Join(" || " , parts.Select(test))}) $aliasDefine({Quote(name)});" , line);
        return "";
    }
    //변수/필드/컬렉션에 넣는 값: 구조체 값이라면 복사본 (구조체 값이 될수 없는 식은 그대로)
    string own(Expression e , string code) => has_structs && may_struct(e) ? $"own({code})" : code;
    static readonly HashSet<string> ElementResults = new() { "list.pop" , "list.removeat" , "array.min" , "array.max" , "list.min" , "list.max" };
    static readonly HashSet<string> OperatorResults = new() { "add" , "sub" , "mul" , "div" , "mod" , "and" , "or" , "not" };
    bool may_struct(Expression e)
    {
        switch (e)
        {
            case ConstantExpression: return false;
            case ParenthesisExpression p: return may_struct(p.Inner);
            //내장 함수는 원소를 꺼내는 함수만 구조체 값을 돌려줄수 있음 (다시 정의한 연산자로 계산하는 add 등은 그 결과)
            case CallExpression call when glob(call.Name , out _) is G.Builtin: return ElementResults.Contains(call.Name) || (has_operators && OperatorResults.Contains(call.Name));
            //&&, ||는 항상 bool
            case BinaryExpression { Operator: "&&" or "||" }: return false;
            default: return true;
        }
    }
    //사용자 코드를 실행할수 있는 식 (구조체 값을 바꿀수 있음): 대입, 내장 함수가 아닌 호출
    //구조체가 연산자를 다시 정의했다면 연산자와 내장 함수도 구조체 함수를 실행할수 있음
    bool effects(Expression e) => e switch {
        AssignExpression or CompoundAssignExpression => true ,
        CallExpression call => glob(call.Name , out _) switch { G.Func => false , G.Builtin => has_operators , _ => true } || call.Arguments.Any(effects) ,
        ParenthesisExpression p => effects(p.Inner) ,
        UnclosedParenthesisExpression u => effects(u.Inner) ,
        BinaryExpression b => (has_operators && !b.IsShortCircuit) || effects(b.Left) || effects(b.Right) ,
        UnaryExpression u => has_operators || effects(u.Operand) ,
        ConditionalExpression c => effects(c.Condition) || effects(c.WhenTrue) || effects(c.WhenFalse) ,
        _ => false
    };

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
                string code = $"({order_push(cx , cx.Scope , d.Name)}{slot(cx.Scope , d.Name)} = new JConst({literal(d.Value)}), null)";
                set_status(cx , cx.Scope , d.Name , 'D');
                return new(code);
            }
            case LabelExpression l:
            {
                string code = $"({order_push(cx , cx.Scope , l.Name)}{slot(cx.Scope , l.Name)} = {cx.Line}, null)";
                set_status(cx , cx.Scope , l.Name , 'D');
                return new(code);
            }
            case AssignExpression a:
            {
                if (options.BlockSubstitute)
                    return new("$noSubstitute()");
                Code rhs = gen(cx , a.Value);
                if (rhs.Early)
                {
                    string t = temp(cx);
                    string a1 = assign_code(cx , a.Name , own(a.Value , $"{t}.v"));
                    string a2 = assign_code(cx , a.Name , own(a.Value , t));
                    return new($"(({t} = {rhs.Text}) instanceof Early ? ({a1}, EARLY_NULL) : ({a2}, null))" , Early: true);
                }
                return assign_value(cx , a.Name , own(a.Value , rhs.Text));
            }
            //x += e: x = x + e (x를 먼저 읽음, 대입 금지는 계산하기 전에 확인)
            case CompoundAssignExpression c:
            {
                if (options.BlockSubstitute)
                    return new("$noSubstitute()");
                string current = gen_call(cx , new Call(c.Name , Array.Empty<Expression>() , false , false)).Text;
                string value;
                //x ??= e: x = x ?? e (x가 null일 때만 e를 계산)
                if (c.Operator is "??")
                {
                    uses_operators = true;
                    Code fallback = conditional(cx , () => gen(cx , c.Value));
                    value = $"({current} ?? {fallback.Text})";
                }
                else
                {
                    Code rhs = gen(cx , c.Value);
                    if (has_structs && effects(c.Value))
                        current = $"own({current})";
                    value = binary_code(c.Operator , current , rhs.Text).Text;
                }
                return assign_value(cx , c.Name , has_structs ? $"own({value})" : value);
            }
            case BinaryExpression b: return gen_binary(cx , b);
            case UnaryExpression u: return gen_unary(cx , u);
            case ConditionalExpression c:
            {
                uses_operators = true;
                string test = truthy(gen(cx , c.Condition));
                State before = cx.St.Clone();
                Code when_true = gen(cx , c.WhenTrue);
                State after_true = cx.St;
                cx.St = before;
                Code when_false = gen(cx , c.WhenFalse);
                cx.St = State.Join(after_true , cx.St);
                return new($"({test} ? {when_true.Text} : {when_false.Text})" , Bool: when_true.Bool && when_false.Bool);
            }
            case SyntaxErrorExpression error:
                uses_operators = true;
                return new($"$fail({Quote(error.Message)})");
            case CallExpression call: return gen_call(cx , new Call(call.Name , call.Arguments , call.HasArguments , call.Closed , call.IsKeywordArgument));
            //삭제는 Native JS 제약으로 미리 걸러짐
            case RemoveExpression: throw new InvalidOperationException("삭제 연산은 Native JS로 바꿀수 없습니다.");
        }
        throw new InvalidOperationException("알수없는 식 " + e.GetType().Name);
    }

    //이름에 값을 넣는 식 (value는 이미 복사한 값)
    Code assign_value(Cx cx , string name , string value)
    {
        var (parts , definite) = chain(cx , name);
        if (definite && parts.Count is 1 && only_var(parts[0].Kinds))
        {
            string direct = bind_clear(cx , parts[0].Scope , name , parts[0].Slot , $"{parts[0].Slot} = {value}");
            return new($"({direct}, null)" , Statement: direct);
        }
        string t = temp(cx);
        string assign = assign_code(cx , name , t);
        return new($"({t} = {value}, {assign}, null)");
    }

    static string truthy(Code c) => c.Bool ? c.Text : $"truthy({c.Text})";

    //─────────── 연산자 (Jyuno 1.6.0) ───────────
    //이항 연산자: +, -, *, /, %, &, |는 같은 계산을 하는 내장 함수의 빠른 경로, 나머지는 런타임의 연산자 함수
    //다시 정의한 비교 연산자의 결과는 bool로 바꾸지 않으므로, 그 연산자를 다시 정의한 구조체가 있다면 결과가 bool이라고 가정하지 않음
    Code binary_code(string symbol , string left , string right)
    {
        uses_operators = true;
        bool comparison = !defined_operators.Contains(symbol);
        return symbol switch {
            "+" => new($"add2({left}, {right})"),
            "-" => new($"sub2({left}, {right})"),
            "*" => new($"mul2({left}, {right})"),
            "/" => new($"div2({left}, {right})"),
            "%" => new($"mod2({left}, {right})"),
            "&" => new($"and2({left}, {right})"),
            "|" => new($"or2({left}, {right})"),
            "^" => new($"opXor({left}, {right})"),
            "<<" => new($"opShl({left}, {right})"),
            ">>" => new($"opShr({left}, {right})"),
            "==" => new(has_structs ? $"opEq({left}, {right})" : $"equal2({left}, {right})" , Bool: comparison),
            "!=" => new($"opNe({left}, {right})" , Bool: comparison),
            "<" => new(has_structs ? $"opLt({left}, {right})" : $"less2({left}, {right})" , Bool: comparison),
            ">" => new(has_structs ? $"opGt({left}, {right})" : $"greater2({left}, {right})" , Bool: comparison),
            "<=" => new($"opLe({left}, {right})" , Bool: comparison),
            ">=" => new($"opGe({left}, {right})" , Bool: comparison),
            _ => throw new InvalidOperationException("알수없는 연산자 " + symbol)
        };
    }

    Code gen_binary(Cx cx , BinaryExpression b)
    {
        uses_operators = true;
        Code left = gen(cx , b.Left);
        switch (b.Operator)
        {
            //단락 평가: 오른쪽은 필요할 때만 계산
            case "&&":
            {
                Code right = conditional(cx , () => gen(cx , b.Right));
                return new($"({truthy(left)} && {truthy(right)})" , Bool: true);
            }
            case "||":
            {
                Code right = conditional(cx , () => gen(cx , b.Right));
                return new($"({truthy(left)} || {truthy(right)})" , Bool: true);
            }
            case "??":
            {
                Code right = conditional(cx , () => gen(cx , b.Right));
                return new($"({left.Text} ?? {right.Text})" , Bool: left.Bool && right.Bool);
            }
        }
        Code r = gen(cx , b.Right);
        string l = left.Text;
        //왼쪽 값이 변수의 구조체 값을 가리키는데 오른쪽 식이 그 값을 바꿀수 있다면 미리 복사 (인터프리터는 읽을때 복사)
        if (has_structs && may_struct(b.Left) && effects(b.Right))
            l = $"own({l})";
        return binary_code(b.Operator , l , r.Text);
    }

    Code gen_unary(Cx cx , UnaryExpression u)
    {
        uses_operators = true;
        //음수 상수는 미리 계산 (- 5)
        if (u.Operator is "-" && u.Operand is ConstantExpression { Value: long or double } constant)
        {
            object negated = constant.Value is long l ? (object)unchecked(-l) : (object)(-(double)constant.Value!);
            return new(literal(negated));
        }
        Code operand = gen(cx , u.Operand);
        return u.Operator switch {
            "-" => new($"opNeg({operand.Text})"),
            "+" => new($"opPlus({operand.Text})"),
            "!" => new($"opNot({operand.Text})" , Bool: !defined_operators.Contains("!")),
            "~" => new($"opCom({operand.Text})"),
            _ => throw new InvalidOperationException("알수없는 연산자 " + u.Operator)
        };
    }

    List<string> gen_args(Cx cx , IReadOnlyList<Expression> args)
    {
        List<string> r = args.Select(a => gen(cx , a).Text).ToList();
        //앞 인자가 변수의 구조체 값을 가리키는데 뒤 인자가 그 구조체 값을 바꿀수 있다면, 앞 인자를 미리 복사 (인터프리터는 읽을때 복사)
        if (has_structs)
            for (int i = 0 ; i < args.Count - 1 ; i++)
                if (may_struct(args[i]) && args.Skip(i + 1).Any(effects))
                    r[i] = $"own({r[i]})";
        return r;
    }

    //조건부로만 계산되는 코드 (상태를 합침)
    T conditional<T>(Cx cx , Func<T> f)
    {
        State before = cx.St.Clone();
        T r = f();
        cx.St = State.Join(before , cx.St);
        return r;
    }

    //함수 호출 자리 (CallExpression, 또는 복합 대입에서 이름 읽기)
    //KeywordArgument: 키워드(if, return 등) 인자 자리에 괄호 없이 쓴 이름 (인자 없이 쓴 이름이 없을 때의 오류 메시지가 다름, Jyuno 1.6.0)
    sealed record Call(string Name , IReadOnlyList<Expression> Arguments , bool HasArguments , bool Closed , bool KeywordArgument = false)
    {
        public string Missing => KeywordArgument && !HasArguments ? $"$unknownVar({Quote(Name)})" : $"$unknownCall({Quote(Name)})";
    }

    //변수 값에 대한 호출: 인자가 있으면 배열/리스트의 원소 읽기/쓰기
    Code variable_call(Cx cx , Call e , string value)
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

    Code gen_call(Cx cx , Call e)
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
                    {
                        //구조체가 있다면 equal은 구조체 값끼리도 비교하는 함수
                        if (fast is "equal2" && has_structs)
                            fast = "equalS2";
                        return new($"{fast}({string.Join(", " , a)})" , Bool: BoolResult.Contains(e.Name));
                    }
                    if (e.Name is "bool" && a.Count is 1)
                        return new($"truthy({a[0]})" , Bool: true);
                    return new($"$B[{Quote(e.Name)}]([{string.Join(", " , a)}])" , Bool: BoolResult.Contains(e.Name));
                }
                case G.Func: return new($"{host_ref(e.Name , "fn")}([{string.Join(", " , gen_args(cx , e.Arguments))}])");
                case G.Var: return variable_call(cx , e , $"{host_ref(e.Name , "var")}.get()");
                case G.Const: return variable_call(cx , e , literal(value));
                default: return new(e.Missing);
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
                    fallback = e.Missing;
                    break;
            }
        }
        //value: 영역 하나의 값 (구조체 함수라면 구조체 값을 this로 묶어야 하는 경우가 있음)
        string holder_of(Func<Part , string> value)
        {
            string? h = fallback;
            for (int i = parts.Count - 1 ; i >= 0 ; i--)
            {
                Part p = parts[i];
                h = h is null ? value(p) : $"({test(p)} ? {value(p)} : {h})";
            }
            return h!;
        }
        if (callable(kinds) && !holder_fallback)
        {
            if (!parts.Any(p => p.Self is not null))
                return new($"{holder_of(p => p.Slot)}({string.Join(", " , gen_args(cx , e.Arguments))})");
            //구조체 함수: 'obj.f_name(...)' 형식이면 JS가 this를 넘김
            if (definite && parts.Count is 1 && parts[0].Test is null)
                return new($"{parts[0].Slot}({string.Join(", " , gen_args(cx , e.Arguments))})");
            //후보가 적다면 갈래마다 'obj.f_name(인자)'로 직접 부름 (인자 코드는 갈래 수만큼 복제되지만 하나만 실행됨, .call보다 빠름)
            if (parts.Count <= 2)
            {
                string arg_text = string.Join(", " , gen_args(cx , e.Arguments));
                string? direct = fallback is null ? null : $"{fallback}({arg_text})";
                for (int i = parts.Count - 1 ; i >= 0 ; i--)
                {
                    Part p = parts[i];
                    direct = direct is null ? $"{p.Slot}({arg_text})" : $"({test(p)} ? {p.Slot}({arg_text}) : {direct})";
                }
                return new(direct!);
            }
            string f = temp(cx), self = temp(cx);
            string? pick = fallback is null ? null : $"({f} = {fallback}, {self} = undefined)";
            for (int i = parts.Count - 1 ; i >= 0 ; i--)
            {
                Part p = parts[i];
                string one = $"({f} = {p.Slot}, {self} = {p.Self ?? "undefined"})";
                pick = pick is null ? one : $"({test(p)} ? {one} : {pick})";
            }
            var call_args = gen_args(cx , e.Arguments);
            return new($"({pick}, {f}.call({self}{string.Concat(call_args.Select(a => ", " + a))}))");
        }
        string holder = holder_of(p => p.Slot);
        if (!kinds.HasFlag(NameKinds.Function) && !kinds.HasFlag(NameKinds.Struct) && !kinds.HasFlag(NameKinds.Constant) && !holder_fallback)
            return variable_call(cx , e , holder);
        holder = holder_of(p => p.Self is null ? p.Slot : $"$bm({p.Self}, {p.Slot})");
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

    void plan_gotos(IEnumerable<GotoStatement> gotos , string exit = "return null;")
    {
        foreach (GotoStatement g in gotos)
        {
            List<(long, string)> list = new();
            foreach (GotoTarget target in g.Targets)
            {
                string jump;
                switch (target.Kind)
                {
                    case GotoTargetKind.Exit: jump = exit; break;
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
                cx.Fn.Loops++;
                gen_block(cx , loop.Body);
                cx.Fn.Loops--;
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
                string item = has_structs ? $"own({it}[{i}])" : $"{it}[{i}]";
                var declarations = inner.NameOrder.Select(n => n == each.VariableName ? $"{slot(inner , n)} = {item}" : $"{slot(inner , n)} = U");
                emit($"{{ const {it} = forItems({source}); {label}: for (let {i} = 0; {i} < {it}.length; {i}++) {{ let {string.Join(", " , declarations)};" , s.Line);
                cx.Fn.Loops++;
                gen_block(icx , each.Body);
                cx.Fn.Loops--;
                if (each.EndTokenError is string end_error)
                    emit(grammar(each.EndLine , end_error) , each.EndLine);
                emit("} }" , each.EndLine);
                return;
            }
            case FunctionStatement { IsStruct: true } structure:
                gen_struct(cx , structure);
                return;
            case FunctionStatement function:
            {
                string function_slot, order, fname;
                if (function.Name is not null)
                {
                    alias_guard(cx , function.Name , s.Line);
                    function_slot = slot(cx.Scope , function.Name);
                    order = order_push(cx , cx.Scope , function.Name);
                    set_status(cx , cx.Scope , function.Name , 'D');
                    fname = mangle(function.Name);
                }
                else if (function.Operator is string symbol)
                {
                    //func (연산자): 구조체 생성자 안에서만 연산자를 다시 정의할수 있음 (Jyuno 1.6.0)
                    if (!Grammar.IsOverloadable(symbol))
                    {
                        emit($"$fail({Quote($"연산자 '{symbol}'은/는 다시 정의할수 없습니다.")});" , s.Line);
                        uses_operators = true;
                        return;
                    }
                    if (cx.Scope.Function is not { IsStruct: true })
                    {
                        emit($"$fail({Quote($"연산자 '{symbol}'은/는 구조체(struct) 안에서만 다시 정의할수 있습니다.")});" , s.Line);
                        uses_operators = true;
                        return;
                    }
                    //구조체 안의 for 반복에서 정의하면 그 반복의 영역에 만들어지므로 연산자로 쓰이지 않음
                    if (cx.Scope.Kind is not ScopeKind.Struct)
                        return;
                    //연산자 함수는 필드가 아니므로 만들어진 순서($o)에 넣지 않음
                    function_slot = $"{self_of(cx.Scope)}.{prop(symbol)}";
                    order = "";
                    fname = "op" + mangle(symbol);
                }
                else
                {
                    emit("$funcSyntax();" , s.Line);
                    return;
                }
                //구조체 함수(생성자 영역에서 정의한 함수): 구조체 정의마다 한번 만들고, 구조체 값은 this로 받음
                bool method = cx.Scope.Kind is ScopeKind.Struct && function.InnerScope.Parent == cx.Scope;
                //함수 본문: 바깥 영역의 '없음'은 호출할 때 생길수 있으므로 '모름'
                State st = new();
                foreach (var (scope , m) in cx.St)
                    st[scope] = m.ToDictionary(o => o.Key , o => o.Value is 'U' ? 'M' : o.Value);
                st[function.InnerScope] = fresh(function.InnerScope , null);
                Fn fn = new();
                Cx fcx = new(function.InnerScope , fn , st);
                var parameters = Enumerable.Range(0 , function.ParameterCount).Select(i => $"$a{i}");
                var declarations = function.InnerScope.NameOrder.Select(n => $"{slot(function.InnerScope , n)} = U").ToList();
                List<(string, int)>? saved = null;
                if (method)
                {
                    saved = output;
                    output = new();
                    emit($"const $m{s.Line} = function $f_{(fname.Length > 40 ? fname[..40] : fname)}({string.Join(", " , parameters)}) {{" , s.Line);
                    emit($"const {self_of(cx.Scope)} = this; if ($depth >= 200) $tooDeep(); $depth++; try {{{own_parameters(function)}" , -2);
                }
                else
                {
                    emit($"{function_slot} = function $f_{(fname.Length > 40 ? fname[..40] : fname)}({string.Join(", " , parameters)}) {{" , s.Line);
                    emit($"if ($depth >= 200) $tooDeep(); $depth++; try {{{own_parameters(function)}" , -2);
                }
                int temp_at = output.Count;
                emit("" , s.Line);
                if (declarations.Count > 0)
                    emit($"let {string.Join(", " , declarations)};" , s.Line);
                gen_block(fcx , function.Body);
                emit("return null; } finally { $depth--; } };" , function.EndLine);
                if (fn.Temps > 0)
                    output[temp_at] = ($"let {string.Join(", " , Enumerable.Range(0 , fn.Temps).Select(i => $"$t{i}"))};" , s.Line);
                if (saved is not null)
                {
                    pending_methods.Peek().AddRange(output);
                    output = saved;
                    emit($"{order}{function_slot} = $m{s.Line};" , s.Line);
                }
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
                alias_guard(cx , p.Name , s.Line);
                //다시 실행될수 있는 param(반복문 안, goto가 있는 함수)은 실행할 때마다 인자의 원래 값을 받아야 하므로 복사본을 넣음
                string argument = $"$a{p.ParameterIndex}";
                bool repeats = has_structs && (cx.Fn.Loops > 0 || !trackable(cx.Scope));
                emit($"{order_push(cx , cx.Scope , p.Name)}{slot(cx.Scope , p.Name)} = ({argument} === undefined ? null : {(repeats ? $"own({argument})" : argument)});" , s.Line);
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
                string v = local.Value is not null ? own(local.Value , gen(cx , local.Value).Text) : "null";
                alias_guard(cx , local.Name , s.Line);
                string local_slot = slot(cx.Scope , local.Name);
                string order = order_push(cx , cx.Scope , local.Name);
                if (only_var(cx.Scope.Names[local.Name]))
                    emit($"{bind_clear(cx , cx.Scope , local.Name , local_slot , $"{order}{local_slot} = {v}")};" , s.Line);
                else
                    emit($"{bind_clear(cx , cx.Scope , local.Name , local_slot , $"{order}{local_slot} = $localSet({local_slot}, {Quote(local.Name)}, {v})")};" , s.Line);
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
                //생성자의 return은 생성자를 끝내기만 함 (값은 계산하지만 무시)
                if (cx.Fn.Self is string self)
                    emit(s.Arguments.Count is 0 ? $"return {self};" : $"return ({first_of(cx , s.Arguments)}, {self});" , s.Line);
                else
                    emit($"return {first_of(cx , s.Arguments)};" , s.Line);
                return;
        }
        throw new InvalidOperationException("알수없는 문장 " + s.Kind);
    }

    //─────────── 구조체 ───────────
    readonly Stack<List<(string, int)>> pending_methods = new();
    //함수/생성자에 들어올 때 인자로 받은 구조체 값을 복사 (인터프리터는 인자를 읽을때 복사하므로, 함수 안에서 원래 값이 바뀌어도 영향 없음)
    string own_parameters(FunctionStatement function)
        => has_structs ? string.Concat(Enumerable.Range(0 , function.ParameterCount).Select(i => $" $a{i} = own($a{i});")) : "";

    //struct ~ endstruct: 구조체 이름에 생성자 함수를 넣음. 구조체 함수는 생성자 밖(정의마다 한번)에 만듦
    void gen_struct(Cx cx , FunctionStatement structure)
    {
        int line = structure.Line;
        if (structure.Name is null)
        {
            emit("$structSyntax();" , line);
            return;
        }
        alias_guard(cx , structure.Name , line);
        string ctor_slot = slot(cx.Scope , structure.Name);
        set_status(cx , cx.Scope , structure.Name , 'D');
        Scope inner = structure.InnerScope;
        string self = self_of(inner);
        State st = new();
        foreach (var (scope , m) in cx.St)
            st[scope] = m.ToDictionary(o => o.Key , o => o.Value is 'U' ? 'M' : o.Value);
        st[inner] = fresh(inner , null);
        Fn fn = new() { Self = self };
        Cx fcx = new(inner , fn , st);
        var parameters = Enumerable.Range(0 , structure.ParameterCount).Select(i => $"$a{i}");
        string fname = mangle(structure.Name);
        emit($"{ctor_slot} = (() => {{" , line);
        int methods_at = output.Count;
        pending_methods.Push(new());
        emit($"return function $c_{(fname.Length > 40 ? fname[..40] : fname)}({string.Join(", " , parameters)}) {{" , line);
        emit($"if ($depth >= 200) $tooDeep(); $depth++; try {{{own_parameters(structure)}" , -2);
        int temp_at = output.Count;
        emit("" , line);
        emit($"const {self} = new $T{inner.Id}({string.Join(", " , inner.NameOrder.Select(prop).Concat(operator_props(inner)).Select(_ => "U").Prepend("[]"))});" , line);
        gen_block(fcx , structure.Body);
        emit($"return {self}; }} finally {{ $depth--; }} }};" , structure.EndLine);
        if (fn.Temps > 0)
            output[temp_at] = ($"let {string.Join(", " , Enumerable.Range(0 , fn.Temps).Select(i => $"$t{i}"))};" , line);
        emit("})();" , structure.EndLine);
        output.InsertRange(methods_at , pending_methods.Pop());
    }
    //구조체마다 JS 클래스 (필드 모양이 고정되어 V8이 빠르게 다룸)
    IEnumerable<string> struct_classes()
    {
        foreach (FunctionStatement f in structs)
        {
            Scope inner = f.InnerScope;
            string t = $"$T{inner.Id}";
            var fields = inner.NameOrder.Select(prop).ToList();
            //다시 정의한 연산자(구조체 함수)도 속성이지만 필드가 아니므로 비교하지 않음
            var props = fields.Concat(operator_props(inner)).ToList();
            yield return $"class {t} extends JStruct {{ constructor({string.Join(", " , props.Prepend("o"))}) {{ super(); this.$o = o;{string.Concat(props.Select(p => $" this.{p} = {p};"))} }} }}";
            yield return $"{t}.prototype.$name = {Quote(f.Name!)}; {t}.prototype.$line = {f.Line}; {t}.prototype.$jn = {{ {string.Join(", " , inner.NameOrder.Select(n => $"{prop(n)}: {Quote(n)}"))} }};";
            yield return $"{t}.prototype.$copy = function (m) {{ return new {t}({string.Join(", " , props.Select(p => $"copyValue(this.{p}, m)").Prepend("this.$o"))}); }};";
            yield return $"{t}.prototype.$same = function (b) {{ return sameOrder(this, b){string.Concat(fields.Select(p => $" && fieldSame(this.{p}, b.{p})"))}; }};";
        }
    }

    //─────────── 전체 ───────────
    public string Emit()
    {
        Block root = analysis.Root!;
        plan_gotos(analysis.TopGotos);
        foreach (FunctionStatement f in analysis.Functions)
            plan_gotos(f.Gotos , f.IsStruct ? $"return {self_of(f.InnerScope)};" : "return null;");

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
        if (has_structs)
        {
            head.Add("const { JStruct, copyValue, own, equalS2 } = $R;");
            head.Add("const { $structSyntax, $bm, $unbindIf, $aliasDefine } = $X;");
        }
        if (uses_operators)
        {
            head.Add("const { opEq, opNe, opLt, opGt, opLe, opGe, opXor, opShl, opShr, opNeg, opPlus, opNot, opCom } = $R;");
            head.Add("const { $fail } = $X;");
        }
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
        //구조체 클래스는 실행(run)마다 새로 만들지 않도록 프로그램 함수 밖에 둠 (여러번 실행해도 V8의 인라인 캐시가 같은 모양을 봄)
        foreach (string code in struct_classes())
            write(code);
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
