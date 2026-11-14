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
        var result = Jyuno.Compiler.JyunoTypeChecker.Check(lines , runtime , level switch { "strict" => Jyuno.Compiler.TypeCheckLevel.Strict , "lenient" => Jyuno.Compiler.TypeCheckLevel.Lenient , _ => Jyuno.Compiler.TypeCheckLevel.Pragmatic });
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

    /// <summary>
    /// 검사를 통과한 프로그램을 제한 모드로 빌드해서, 검사기가 증명한 타입으로 컴파일 (IlTyped)
    /// 제한 모드로 빌드할수 없는 프로그램(구조체, 삭제 등)이면 null (인터프리터로 실행)
    /// </summary>
    public static Func<object?>? TryCompile(IReadOnlyList<string> lines , Runtime runtime , out string engine)
    {
#if JYUNO_STATIC_TYPING
        try
        {
            byte[] image = Jyuno.Compiler.JyunoProgramImage.Build(lines , runtime);
            JyunoProgram program = Jyuno.Compiler.JyunoProgramImage.Load(image , runtime , Jyuno.Compiler.JyunoEngine.IlTyped);
            //검사기의 타입을 쓰지 못한 경우(함수 안의 함수, 레이블 등)는 기본 추론의 IL 또는 클로저 엔진 (결과는 같음)
            engine = program.Engine;
            return program.Run;
        }
        catch (Jyuno.Compiler.JyunoBuildException e)
        {
            engine = "Interpreter (제한 모드로 빌드할수 없음: " + (e.Errors.Count > 0 ? e.Errors[0].Message : e.Message) + ")";
            return null;
        }
#else
        engine = "Interpreter";
        return null;
#endif
    }
}
