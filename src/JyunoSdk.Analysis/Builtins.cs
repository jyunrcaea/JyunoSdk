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
    static readonly Dictionary<string , (BuiltinKind Kind, string Description, BuiltinSignature[] Signatures)> docs = with_more_docs(new() {
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
    });

    //Jyuno 1.4.1~1.4.2에서 추가된 난수, 컬렉션(array.*, list.*), 문자열(string.*) 함수
    static Dictionary<string , (BuiltinKind Kind, string Description, BuiltinSignature[] Signatures)> with_more_docs(Dictionary<string , (BuiltinKind Kind, string Description, BuiltinSignature[] Signatures)> d)
    {
        void f(string name , string description , params BuiltinSignature[] signatures) => d[name] = (BuiltinKind.Function , description , signatures);
        const string range_rule = "인자가 없으면 0 이상 1 미만의 실수, max만 있으면 [0, max), min max가 있으면 [min, max)에서 뽑습니다. 인자가 모두 정수면 정수, 실수가 섞이면 실수를 반환합니다.";
        BuiltinSignature[] random = { S("double") , S("int|double" , P("max" , "int|double")) , S("int|double" , P("min" , "int|double") , P("max" , "int|double")) };
        f("math.random" , "빠른 난수(System.Random)를 뽑습니다. " + range_rule , random);
        f("math.csprng" , "암호학적으로 안전한 난수(RandomNumberGenerator)를 뽑습니다. 비밀번호, 토큰처럼 예측할수 없어야 하는 값에 사용하세요. " + range_rule , random);

        foreach (string kind in new[] { "array" , "list" })
        {
            string k = kind , made = kind is "array" ? "배열" : "리스트";
            BuiltinParameter c = P("collection" , "array|list");
            f($"{k}.sort" , "오름차순으로 정렬합니다. 두번째 인자가 참이면 내림차순입니다. 같은 값의 순서는 유지(안정 정렬)하며, 원래 배열/리스트를 바꾸고 그대로 반환합니다." , S("array|list" , c) , S("array|list" , c , P("descending" , "bool")));
            f($"{k}.reverse" , "순서를 뒤집습니다. 원래 배열/리스트를 바꾸고 그대로 반환합니다." , S("array|list" , c));
            f($"{k}.shuffle" , "순서를 무작위로 섞습니다. 원래 배열/리스트를 바꾸고 그대로 반환합니다." , S("array|list" , c));
            f($"{k}.contains" , "값과 같은(equal) 원소가 있으면 true를 반환합니다." , S("bool" , c , P("value" , "any")));
            f($"{k}.indexof" , "값과 같은(equal) 첫 원소의 인덱스를 반환합니다. 없으면 -1입니다." , S("int" , c , P("value" , "any")));
            f($"{k}.lastindexof" , "값과 같은(equal) 마지막 원소의 인덱스를 반환합니다. 없으면 -1입니다." , S("int" , c , P("value" , "any")));
            f($"{k}.count" , "값과 같은(equal) 원소의 개수를 반환합니다." , S("int" , c , P("value" , "any")));
            f($"{k}.slice" , "시작 인덱스부터 끝 인덱스 바로 앞까지(끝을 생략하면 마지막까지)의 원소로 새 배열/리스트를 만듭니다." , S("array|list" , c , P("start" , "int")) , S("array|list" , c , P("start" , "int") , P("end" , "int")));
            f($"{k}.join" , "원소를 문자열로 바꿔서 구분자(생략하면 빈 문자열)로 이어붙입니다." , S("string" , c) , S("string" , c , P("separator" , "string")));
            f($"{k}.new" , $"모든 원소가 값(생략하면 null)이고 길이가 length인 {made}을/를 만듭니다." , S(kind , P("length" , "int")) , S(kind , P("length" , "int") , P("value" , "any")));
            f($"{k}.copy" , "같은 종류, 같은 원소 타입의 새 배열/리스트로 복사합니다. (얕은 복사)" , S("array|list" , c));
            f($"{k}.distinct" , "중복된(equal) 원소를 제거한 새 배열/리스트를 반환합니다. 처음 나온 원소를 남깁니다." , S("array|list" , c));
            f($"{k}.binary_search" , "오름차순으로 정렬된 배열/리스트에서 값을 이진 탐색합니다. 찾으면 그 인덱스(같은 값이 여러개면 가장 앞), 없으면 들어갈 위치를 not으로 반전한 음수를 반환합니다." , S("int" , c , P("value" , "any")));
            f($"{k}.fill" , "모든 원소를 값으로 바꿉니다. 원래 배열/리스트를 바꾸고 그대로 반환합니다." , S("array|list" , c , P("value" , "any")));
            f($"{k}.min" , "가장 작은 원소를 반환합니다. (sort와 같은 기준, 빈 배열/리스트는 오류)" , S("any" , c));
            f($"{k}.max" , "가장 큰 원소를 반환합니다. (sort와 같은 기준, 빈 배열/리스트는 오류)" , S("any" , c));
            f($"{k}.sum" , "모든 원소(숫자)를 add와 같이 더합니다. 빈 배열/리스트는 0입니다." , S("int|double" , c));
            f($"{k}.range" , $"시작(생략하면 0)부터 끝 바로 앞까지 간격(생략하면 1)만큼 바뀌는 정수 {made}을/를 만듭니다." , S(kind , P("end" , "int")) , S(kind , P("start" , "int") , P("end" , "int")) , S(kind , P("start" , "int") , P("end" , "int") , P("step" , "int")));
        }
        BuiltinParameter l = P("list" , "list");
        f("list.push" , "리스트 끝에 값들을 추가하고 리스트를 반환합니다. (배열은 길이를 바꿀수 없어 오류)" , V("list" , l , P("value" , "any")));
        f("list.pop" , "마지막 원소를 제거하고, 그 원소를 반환합니다." , S("any" , l));
        f("list.insert" , "인덱스 위치에 값을 끼워넣고 리스트를 반환합니다." , S("list" , l , P("index" , "int") , P("value" , "any")));
        f("list.removeat" , "인덱스 위치의 원소를 제거하고, 그 원소를 반환합니다." , S("any" , l , P("index" , "int")));
        f("list.remove" , "값과 같은(equal) 첫 원소를 제거합니다. 제거했다면 true를 반환합니다." , S("bool" , l , P("value" , "any")));
        f("list.clear" , "모든 원소를 제거하고 리스트를 반환합니다." , S("list" , l));

        BuiltinParameter t = P("text" , "string");
        f("string.concat" , "값들을 문자열로 바꿔서 이어붙입니다. (C#의 string.Concat, null은 빈 문자열)" , V("string" , P("value" , "any")));
        f("string.join" , "값들을 구분자로 이어붙입니다. 값이 배열/리스트 하나라면 그 원소들을 이어붙입니다. (C#의 string.Join)" , V("string" , P("separator" , "string") , P("value" , "any")));
        f("string.format" , "C#의 string.Format처럼 {0}, {1:F2}, {0,5} 자리에 값을 넣습니다. {0}이 첫번째 값입니다." , V("string" , P("format" , "string") , P("arg" , "any")));
        f("string.compare" , "유니코드 순서로 비교해서 a가 앞이면 -1, 같으면 0, 뒤면 1을 반환합니다. (null은 가장 앞)" , S("int" , P("a" , "string") , P("b" , "string")));
        f("string.equals" , "두 문자열이 같은지 비교합니다. 세번째 인자가 참이면 대소문자를 무시합니다." , S("bool" , P("a" , "string") , P("b" , "string")) , S("bool" , P("a" , "string") , P("b" , "string") , P("ignoreCase" , "bool")));
        f("string.isnullorempty" , "값이 null 또는 빈 문자열이면 true를 반환합니다." , S("bool" , P("value" , "string")));
        f("string.isnullorwhitespace" , "값이 null이거나 공백 문자로만 이루어졌다면 true를 반환합니다." , S("bool" , P("value" , "string")));
        f("string.indexof" , "찾을 문자열이 처음 나오는 인덱스를 반환합니다. 없으면 -1입니다." , S("int" , t , P("value" , "string")) , S("int" , t , P("value" , "string") , P("start" , "int")));
        f("string.lastindexof" , "찾을 문자열이 마지막으로 나오는 인덱스를 반환합니다. 없으면 -1입니다." , S("int" , t , P("value" , "string")));
        f("string.contains" , "찾을 문자열이 있으면 true를 반환합니다. (대소문자 구별)" , S("bool" , t , P("value" , "string")));
        f("string.startswith" , "값으로 시작하면 true를 반환합니다." , S("bool" , t , P("value" , "string")));
        f("string.endswith" , "값으로 끝나면 true를 반환합니다." , S("bool" , t , P("value" , "string")));
        f("string.replace" , "바꿀 문자열을 모두 새 문자열로 바꿉니다. 새 문자열이 null이면 제거합니다." , S("string" , t , P("old" , "string") , P("new" , "string")));
        f("string.substring" , "시작 인덱스부터 길이만큼(생략하면 끝까지)의 문자열을 반환합니다." , S("string" , t , P("start" , "int")) , S("string" , t , P("start" , "int") , P("length" , "int")));
        f("string.insert" , "인덱스 위치에 값을 끼워넣은 문자열을 반환합니다." , S("string" , t , P("index" , "int") , P("value" , "string")));
        f("string.remove" , "시작 인덱스부터 개수만큼(생략하면 끝까지)의 글자를 제거한 문자열을 반환합니다." , S("string" , t , P("start" , "int")) , S("string" , t , P("start" , "int") , P("count" , "int")));
        f("string.toupper" , "대문자로 바꾼 문자열을 반환합니다. (문화권과 무관)" , S("string" , t));
        f("string.tolower" , "소문자로 바꾼 문자열을 반환합니다. (문화권과 무관)" , S("string" , t));
        f("string.trim" , "양쪽의 공백 문자(또는 두번째 인자에 있는 글자들)를 제거합니다." , S("string" , t) , S("string" , t , P("chars" , "string")));
        f("string.trimstart" , "앞쪽의 공백 문자(또는 두번째 인자에 있는 글자들)를 제거합니다." , S("string" , t) , S("string" , t , P("chars" , "string")));
        f("string.trimend" , "뒤쪽의 공백 문자(또는 두번째 인자에 있는 글자들)를 제거합니다." , S("string" , t) , S("string" , t , P("chars" , "string")));
        f("string.padleft" , "길이가 될때까지 앞쪽에 글자(생략하면 공백)를 채웁니다." , S("string" , t , P("width" , "int")) , S("string" , t , P("width" , "int") , P("char" , "string")));
        f("string.padright" , "길이가 될때까지 뒤쪽에 글자(생략하면 공백)를 채웁니다." , S("string" , t , P("width" , "int")) , S("string" , t , P("width" , "int") , P("char" , "string")));
        f("string.split" , "구분자로 나눈 문자열 배열을 반환합니다. 구분자를 생략하면 공백 문자마다 나눕니다." , S("array" , t) , S("array" , t , P("separator" , "string")));
        f("string.tochararray" , "한 글자씩 나눈 문자열 배열을 반환합니다." , S("array" , t));
        f("string.charcode" , "인덱스(생략하면 0) 위치 글자의 UTF-16 코드를 반환합니다." , S("int" , t) , S("int" , t , P("index" , "int")));
        f("string.fromcharcode" , "UTF-16 코드(0 ~ 65535)들로 문자열을 만듭니다." , V("string" , P("code" , "int")));
        return d;
    }

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
