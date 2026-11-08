using System.Globalization;
using System.Text;
using Jyuno;
using Jyuno.Analysis;
using Jyuno.Language;

namespace JyunoSdk.NativeJs;

/// <summary>Native JS 변환 옵션</summary>
public sealed class NativeJsOptions
{
    /// <summary>전역 이름 (기본: 콘솔과 수학 명령어를 포함한 Jyuno 런타임). 프로그램이 추가한 함수/변수가 있다면 그 런타임의 GetGlobals()</summary>
    public IReadOnlyDictionary<string , GlobalName>? Globals { get; init; }
    /// <summary>실행할때 변수 대입을 막음 (Interpreter.BlockSubstitute)</summary>
    public bool BlockSubstitute { get; init; }
    /// <summary>오류 메시지 등에 쓸 스크립트 이름</summary>
    public string Name { get; init; } = "script.jyuno";
}

/// <summary>Native JS 변환 결과</summary>
public sealed class NativeJsResult
{
    internal NativeJsResult(IReadOnlyList<GrammarError> errors , string? code)
    {
        Errors = errors;
        Code = code;
    }
    public bool Success => Code is not null;
    /// <summary>블록 구조 오류와 Native JS 제약 위반 (줄은 0부터)</summary>
    public IReadOnlyList<GrammarError> Errors { get; }
    /// <summary>
    /// 런타임을 포함한 JavaScript 코드. 전역 변수 JyunoProgram(CommonJS라면 module.exports)에
    /// run(options)과 main()이 있는 객체를 만들고, Node.js에서 직접 실행하면 표준 입출력으로 실행합니다.
    /// </summary>
    public string? Code { get; }
}

/// <summary>
/// Jyuno 코드를 JavaScript(Native JS)로 변환합니다.
/// 분석(구문 트리, 영역, goto 대상, 제약 검사)은 Jyuno 라이브러리(Jyuno.Analysis)가 하고, 여기서는 JS 코드만 만듭니다.
/// </summary>
public static class NativeJsCompiler
{
    static readonly Lazy<string> runtime_source = new(() => {
        using Stream stream = typeof(NativeJsCompiler).Assembly.GetManifestResourceStream("JyunoSdk.NativeJs.jyuno-runtime.js")!;
        using StreamReader reader = new(stream , Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n" , "\n");
    });
    /// <summary>생성 코드에 포함되는 JS 런타임</summary>
    public static string RuntimeSource => runtime_source.Value;

    static readonly Lazy<IReadOnlyDictionary<string , GlobalName>> default_globals = new(() => new Runtime(Runtime.AddJyunoCommandType.Default).GetGlobals());
    /// <summary>콘솔과 수학 명령어를 포함한 기본 전역 이름</summary>
    public static IReadOnlyDictionary<string , GlobalName> DefaultGlobals => default_globals.Value;

    /// <summary>코드를 변환합니다. (줄바꿈은 \n 또는 \r\n)</summary>
    public static NativeJsResult Compile(string source , NativeJsOptions? options = null)
        => Compile(source.Replace("\r\n" , "\n").Split('\n') , options);

    /// <summary>줄 단위 코드를 변환합니다.</summary>
    public static NativeJsResult Compile(IReadOnlyList<string> lines , NativeJsOptions? options = null)
    {
        options ??= new();
        var globals = options.Globals ?? DefaultGlobals;
        ScriptAnalysis analysis = ScriptAnalysis.Analyze(lines);
        var errors = analysis.CheckNativeJs(globals);
        if (errors.Count > 0)
            return new(errors , null);
        string code = new Emitter(analysis , globals , options).Emit();
        return new(errors , code);
    }
}
