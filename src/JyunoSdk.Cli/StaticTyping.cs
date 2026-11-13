using Jyuno;

namespace JyunoSdk.Cli;

//[실험] --static-typing: 실행 전에 모든 타입을 추론 (Jyuno의 JyunoTypeChecker)
//정적 타입 검사는 아직 Jyuno 패키지에 없으므로, 그 기능이 있는 Jyuno 소스로 빌드할 때만 켜짐
//  dotnet build -p:JyunoSource=../Jyuno/Jyuno.csproj -p:JyunoStaticTyping=true
static class StaticTyping
{
    /// <summary>검사 결과를 출력하고 오류 수를 반환</summary>
    public static int Report(string path , IReadOnlyList<string> lines , Runtime runtime , string level , out int warnings , TextWriter? output = null)
    {
        output ??= Console.Error;
#if JYUNO_STATIC_TYPING
        var result = Jyuno.Compiler.JyunoTypeChecker.Check(lines , runtime , level is "strict" ? Jyuno.Compiler.TypeCheckLevel.Strict : Jyuno.Compiler.TypeCheckLevel.Pragmatic);
        foreach (var d in result.Errors)
            output.WriteLine($"{path}:{d.Line + 1}: error type/{d.Category}: {d.Message}");
        foreach (var d in result.Warnings)
            output.WriteLine($"{path}:{d.Line + 1}: warning type/{d.Category}: {d.Message}");
        if (Environment.GetEnvironmentVariable("JYUNO_SHOW_TYPES") is "1")
            foreach (string t in result.Types)
                output.WriteLine($"  {t}");
        warnings = result.Warnings.Count;
        return result.Errors.Count;
#else
        output.WriteLine($"{path}: error type: 이 jyuno는 정적 타입 검사 없이 빌드되었습니다. (실험 기능: -p:JyunoSource=<Jyuno.csproj> -p:JyunoStaticTyping=true 로 빌드)");
        warnings = 0;
        return 1;
#endif
    }
}
