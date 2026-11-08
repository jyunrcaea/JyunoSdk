using Jyuno;
using Jyuno.Language;

namespace JyunoSdk.Analysis;

/// <summary>
/// 내장 명령어 묶음. Jyuno의 <see cref="Runtime.AddJyunoCommandType"/>과 같습니다.
/// </summary>
[Flags]
public enum CommandGroups
{
    Essential = 0,
    Console = 1,
    Math = 2,
    File = 4,
    Default = Essential | Console | Math,
    All = Essential | Console | Math | File
}

public enum BuiltinKind
{
    Function,
    Variable,
    Constant
}

public sealed record BuiltinParameter(string Name , string Type , string? Description = null);

/// <summary>Jyuno가 기본으로 제공하는 함수/변수 하나</summary>
public sealed record Builtin(string Name , BuiltinKind Kind , CommandGroups Group , string Description , IReadOnlyList<BuiltinSignature> Signatures)
{
    public bool IsFunction => Kind is BuiltinKind.Function;
    /// <summary>Windows에서만 제공되는지 (console.title)</summary>
    public bool WindowsOnly { get; init; }
    /// <summary>문서가 없는 명령어 (Jyuno에 새로 추가되었지만 SDK에 아직 설명이 없는 경우)</summary>
    public bool Undocumented { get; init; }

    public string GroupName => Group switch {
        CommandGroups.Console => "console",
        CommandGroups.Math => "math",
        CommandGroups.File => "file",
        _ => "기본"
    };

    /// <summary>마크다운 설명 (hover, 자동 완성 문서)</summary>
    public string ToMarkdown()
    {
        System.Text.StringBuilder sb = new();
        sb.AppendLine("```jyuno");
        if (Signatures.Count is 0)
            sb.AppendLine(Name);
        foreach (var signature in Signatures)
            sb.AppendLine(signature.Label(Name));
        sb.AppendLine("```");
        sb.AppendLine(Description);
        sb.AppendLine();
        string kind = Kind switch {
            BuiltinKind.Function => "내장 함수",
            BuiltinKind.Constant => "내장 상수",
            _ => "내장 변수"
        };
        sb.Append($"*{kind} · {GroupName} 명령어*");
        if (Group is CommandGroups.File)
            sb.Append(" · 파일을 다룰수 있는 프로그램에서만 제공됨");
        if (WindowsOnly)
            sb.Append(" · Windows에서만 제공됨");
        return sb.ToString();
    }
}

/// <summary>함수의 호출 형태 하나 (예: <c>add (any) (any) ...</c>)</summary>
public sealed record BuiltinSignature(IReadOnlyList<BuiltinParameter> Parameters , string? Returns = null , bool Variadic = false , string? Description = null)
{
    public string Label(string name)
    {
        System.Text.StringBuilder sb = new(name);
        foreach (var parameter in Parameters)
            sb.Append($" ({parameter.Name}:{parameter.Type})");
        if (Variadic)
            sb.Append(" ...");
        if (Returns is not null)
            sb.Append($" → {Returns}");
        return sb.ToString();
    }
}

/// <summary>
/// 내장 명령어 목록. 이름과 묶음은 참조한 Jyuno 라이브러리에서 직접 읽고, 설명은 SDK가 가지고 있습니다.
/// </summary>
public static class Builtins
{
    static BuiltinParameter P(string name , string type , string? description = null) => new(name , type , description);
    static BuiltinSignature S(string? returns , params BuiltinParameter[] parameters) => new(parameters , returns);
    static BuiltinSignature V(string? returns , params BuiltinParameter[] parameters) => new(parameters , returns , true);

    //설명 (이름 → 종류, 설명, 호출 형태)
    static readonly Dictionary<string , (BuiltinKind Kind, string Description, BuiltinSignature[] Signatures)> docs = new() {
        ["null"] = (BuiltinKind.Constant , "값이 없음을 나타내는 상수입니다. Jyuno에서는 void 대신 null을 반환합니다." , Array.Empty<BuiltinSignature>()),
        ["true"] = (BuiltinKind.Constant , "참을 나타내는 상수입니다." , Array.Empty<BuiltinSignature>()),
        ["false"] = (BuiltinKind.Constant , "거짓을 나타내는 상수입니다." , Array.Empty<BuiltinSignature>()),
        ["int"] = (BuiltinKind.Function , "값을 정수(long)로 변환합니다. 인자가 없으면 0을 반환합니다." , new[] { S("int") , S("int" , P("value" , "any")) }),
        ["double"] = (BuiltinKind.Function , "값을 실수(double)로 변환합니다. 인자가 없으면 0.0을 반환합니다." , new[] { S("double") , S("double" , P("value" , "any")) }),
        ["string"] = (BuiltinKind.Function , "값을 문자열로 변환합니다. 인자가 없으면 빈 문자열을 반환합니다." , new[] { S("string") , S("string" , P("value" , "any")) }),
        ["bool"] = (BuiltinKind.Function , "값을 참/거짓으로 변환합니다. (null, false, 0, 빈 문자열은 false)" , new[] { S("bool" , P("value" , "any")) }),
        ["array"] = (BuiltinKind.Function , "주어진 인자를 원소로 하는 배열(C#의 T[])을 만듭니다. 원소 타입이 모두 같다면 그 타입의 배열이 됩니다." , new[] { V("array" , P("item" , "any")) }),
        ["list"] = (BuiltinKind.Function , "주어진 인자를 원소로 하는 리스트(C#의 List<T>)를 만듭니다." , new[] { V("list" , P("item" , "any")) }),
        ["length"] = (BuiltinKind.Function , "문자열의 글자 수, 또는 배열/리스트의 원소 개수를 반환합니다." , new[] { S("int" , P("value" , "string|array|list")) }),
        ["add"] = (BuiltinKind.Function , "주어진 인자를 모두 더합니다. 문자열이 하나라도 있다면 문자열로 이어붙이고, 첫 인자가 배열/리스트라면 나머지 인자를 이어붙인 새 배열/리스트를 반환합니다." , new[] { V("any" , P("a" , "any") , P("b" , "any")) , V("array|list" , P("collection" , "array|list") , P("item" , "any")) }),
        ["sub"] = (BuiltinKind.Function , "뺄셈. 문자열이라면 두번째 문자열을 모두 제거하고, 배열/리스트라면 두번째 인자에 있는 원소를 모두 제거한 새 배열/리스트(차집합)를 반환합니다." , new[] { S("int|double" , P("a" , "int|double") , P("b" , "int|double")) , S("string" , P("text" , "string") , P("remove" , "string")) , S("array|list" , P("collection" , "array|list") , P("remove" , "any")) }),
        ["mul"] = (BuiltinKind.Function , "주어진 인자를 모두 곱합니다. 첫 인자가 문자열이라면 문자열을 원하는 횟수만큼 반복합니다." , new[] { V("int|double" , P("a" , "int|double") , P("b" , "int|double")) , S("string" , P("text" , "string") , P("count" , "int")) }),
        ["div"] = (BuiltinKind.Function , "나눗셈. 첫 인자가 문자열이라면 두번째 인자를 구분자로 나눈 문자열 배열을 반환합니다. (C#의 a.Split(b))" , new[] { S("int|double" , P("a" , "int|double") , P("b" , "int|double")) , S("array" , P("text" , "string") , P("separator" , "string")) }),
        ["mod"] = (BuiltinKind.Function , "나머지를 구합니다." , new[] { S("int|double" , P("a" , "int|double") , P("b" , "int|double")) }),
        ["equal"] = (BuiltinKind.Function , "주어진 인자가 모두 같으면 true를 반환합니다. (1과 1.0은 같음)" , new[] { V("bool" , P("a" , "any") , P("b" , "any")) }),
        ["less"] = (BuiltinKind.Function , "a < b. 인자가 셋 이상이라면 모든 인자가 오름차순인지 검사합니다." , new[] { V("bool" , P("a" , "int|double|string") , P("b" , "int|double|string")) }),
        ["greater"] = (BuiltinKind.Function , "a > b. 인자가 셋 이상이라면 모든 인자가 내림차순인지 검사합니다." , new[] { V("bool" , P("a" , "int|double|string") , P("b" , "int|double|string")) }),
        ["and"] = (BuiltinKind.Function , "인자가 모두 정수라면 비트 AND, 그렇지 않으면 논리 AND입니다. (단락 평가를 하지 않음)" , new[] { V("int|bool" , P("a" , "any") , P("b" , "any")) }),
        ["or"] = (BuiltinKind.Function , "인자가 모두 정수라면 비트 OR, 그렇지 않으면 논리 OR입니다. (단락 평가를 하지 않음)" , new[] { V("int|bool" , P("a" , "any") , P("b" , "any")) }),
        ["not"] = (BuiltinKind.Function , "정수라면 비트 NOT(~a), 그렇지 않으면 논리 NOT(!a)입니다. 정수가 0인지 검사하려면 equal x 0 또는 not (bool x)를 사용하세요." , new[] { S("int|bool" , P("value" , "any")) }),

        ["console.write"] = (BuiltinKind.Function , "콘솔에 출력합니다. 인자가 둘 이상이면 C#의 Console.Write(format, args)처럼 서식 문자열을 사용합니다." , new[] { S(null , P("value" , "any")) , V(null , P("format" , "string") , P("arg" , "any")) }),
        ["console.writeline"] = (BuiltinKind.Function , "콘솔에 출력하고 줄을 바꿉니다. 인자가 둘 이상이면 서식 문자열을 사용합니다." , new[] { S(null) , S(null , P("value" , "any")) , V(null , P("format" , "string") , P("arg" , "any")) }),
        ["console.readline"] = (BuiltinKind.Function , "콘솔에서 한 줄을 입력받습니다." , new[] { S("string") }),
        ["console.read"] = (BuiltinKind.Function , "콘솔에서 한 글자를 입력받습니다." , new[] { S("char") }),
        ["console.clear"] = (BuiltinKind.Function , "콘솔 화면을 지웁니다." , new[] { S(null) }),
        ["console.title"] = (BuiltinKind.Variable , "콘솔 창의 제목입니다. (Windows에서만 제공)" , Array.Empty<BuiltinSignature>()),

        ["math.pi"] = (BuiltinKind.Constant , "원주율 π (3.14159...)" , Array.Empty<BuiltinSignature>()),
        ["math.tau"] = (BuiltinKind.Constant , "τ = 2π (6.28318...)" , Array.Empty<BuiltinSignature>()),
        ["math.nan"] = (BuiltinKind.Constant , "숫자가 아님(NaN). 어떤 값과 비교해도 false입니다." , Array.Empty<BuiltinSignature>()),
        ["math.abs"] = (BuiltinKind.Function , "절댓값을 구합니다." , new[] { S("int|double" , P("value" , "int|double")) }),
        ["math.sin"] = (BuiltinKind.Function , "사인 값을 구합니다. (라디안)" , new[] { S("double" , P("radian" , "double")) }),
        ["math.cos"] = (BuiltinKind.Function , "코사인 값을 구합니다. (라디안)" , new[] { S("double" , P("radian" , "double")) }),
        ["math.tan"] = (BuiltinKind.Function , "탄젠트 값을 구합니다. (라디안)" , new[] { S("double" , P("radian" , "double")) }),
        ["math.pow"] = (BuiltinKind.Function , "거듭제곱(밑^지수)을 구합니다." , new[] { S("double" , P("base" , "double") , P("exponent" , "double")) }),
        ["math.log"] = (BuiltinKind.Function , "상용로그(log10)를 구합니다. 두번째 인자가 있으면 그 값을 밑으로 하는 로그를 구합니다." , new[] { S("double" , P("value" , "double")) , S("double" , P("value" , "double") , P("base" , "double")) }),
        ["math.log2"] = (BuiltinKind.Function , "이진로그(log2)를 구합니다." , new[] { S("double" , P("value" , "double")) }),

        ["file.exist"] = (BuiltinKind.Function , "파일이 존재하는지 확인합니다." , new[] { S("bool" , P("path" , "string")) }),
        ["file.read"] = (BuiltinKind.Function , "파일 내용을 읽습니다. 파일이 없으면 null을 반환합니다." , new[] { S("string" , P("path" , "string")) }),
        ["file.write"] = (BuiltinKind.Function , "파일에 내용을 씁니다." , new[] { S(null , P("path" , "string") , P("content" , "string")) }),
        ["file.remove"] = (BuiltinKind.Function , "파일을 삭제합니다. 파일이 없었다면 false를 반환합니다." , new[] { S("bool" , P("path" , "string")) }),
        ["directory.now"] = (BuiltinKind.Function , "현재 작업 디렉터리 경로를 반환합니다." , new[] { S("string") }),
        ["directory.exist"] = (BuiltinKind.Function , "디렉터리가 존재하는지 확인합니다." , new[] { S("bool" , P("path" , "string")) }),
        ["directory.files"] = (BuiltinKind.Function , "디렉터리 안의 파일 경로 배열을 반환합니다. 경로를 생략하면 현재 디렉터리를 사용합니다." , new[] { S("array") , S("array" , P("path" , "string")) }),
        ["directory.list"] = (BuiltinKind.Function , "디렉터리 안의 하위 디렉터리 경로 배열을 반환합니다. 경로를 생략하면 현재 디렉터리를 사용합니다." , new[] { S("array") , S("array" , P("path" , "string")) }),
        ["directory.move"] = (BuiltinKind.Function , "디렉터리를 이동합니다." , new[] { S("bool" , P("source" , "string") , P("target" , "string")) }),
    };

    static readonly Lazy<IReadOnlyDictionary<string , Builtin>> all = new(Load);

    /// <summary>모든 내장 명령어 (모든 묶음)</summary>
    public static IReadOnlyDictionary<string , Builtin> All => all.Value;

    public static bool TryGet(string name , out Builtin builtin) => All.TryGetValue(name , out builtin!);

    /// <summary>주어진 묶음에서 제공되는 내장 명령어</summary>
    public static IEnumerable<Builtin> In(CommandGroups groups) => All.Values.Where(b => b.Group is CommandGroups.Essential || groups.HasFlag(b.Group));

    static IReadOnlyDictionary<string , Builtin> Load()
    {
        Dictionary<string , Builtin> result = new();
        void add(CommandGroups group , Action<VariableDictionary> fill)
        {
            VariableDictionary dict = new();
            fill(dict);
            foreach (var (name , holder) in dict)
            {
                if (result.ContainsKey(name))
                    continue;
                bool function = holder is FunctionInterface;
                if (docs.TryGetValue(name , out var doc))
                    result[name] = new(name , doc.Kind , group , doc.Description , doc.Signatures);
                else
                    result[name] = new(name , function ? BuiltinKind.Function : BuiltinKind.Variable , group , "Jyuno 내장 명령어입니다." , Array.Empty<BuiltinSignature>()) { Undocumented = true };
            }
        }
        add(CommandGroups.Essential , JyunoCommands.AddDefault);
        add(CommandGroups.Console , JyunoCommands.AddConsole);
        add(CommandGroups.Math , JyunoCommands.AddMath);
        add(CommandGroups.File , JyunoCommands.AddFile);
        //console.title은 Windows에서만 추가되지만, 다른 운영체제에서도 문서는 보여줌
        if (!result.ContainsKey("console.title"))
        {
            var doc = docs["console.title"];
            result["console.title"] = new("console.title" , doc.Kind , CommandGroups.Console , doc.Description , doc.Signatures) { WindowsOnly = true };
        }
        else
        {
            result["console.title"] = result["console.title"] with { WindowsOnly = true };
        }
        return result;
    }

    /// <summary>문서에는 있지만 참조한 Jyuno에는 없는 이름 (테스트용)</summary>
    public static IEnumerable<string> DocumentedNames => docs.Keys;
}
