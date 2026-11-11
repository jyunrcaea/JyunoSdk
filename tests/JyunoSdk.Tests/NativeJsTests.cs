using System.Diagnostics;
using Jyuno;
using JyunoSdk.NativeJs;

namespace JyunoSdk.Tests;

[Collection("Console")]
public class NativeJsTests
{
    static NativeJsResult Compile(params string[] lines) => NativeJsCompiler.Compile(lines);

    [Fact]
    public void CompilesToSelfContainedScript()
    {
        var result = Compile("x = add 1 2" , "console.writeline x");
        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Contains("var JyunoProgram" , result.Code);
        Assert.Contains("module.exports" , result.Code);
        //런타임이 포함되어 있어야 함
        Assert.Contains("createProgram" , result.Code);
        Assert.False(string.IsNullOrEmpty(NativeJsCompiler.RuntimeSource));
    }

    [Theory]
    [InlineData("x = 1|x =" , 1 , "삭제")]
    [InlineData("add = 1" , 0 , "'add'")]
    [InlineData("func mul|return 1|endfunc" , 0 , "'mul'")]
    [InlineData("true = 5" , 0 , "'true'")]
    [InlineData("i = 0|if 1|inner:|i = 1|end|goto inner" , 5 , "goto")]
    public void RejectsRestrictedCode(string code , int line , string fragment)
    {
        var result = Compile(code.Split('|'));
        Assert.False(result.Success);
        Assert.Null(result.Code);
        var error = Assert.Single(result.Errors);
        Assert.Equal(line , error.Line);
        Assert.Contains("Native JS 제약" , error.Message);
        Assert.Contains(fragment , error.Message);
    }

    [Fact]
    public void HostVariableAssignmentAllowed()
    {
        //프로그램이 제공하는 변수(console.title)에 대입하는건 재정의가 아님
        Assert.True(Compile("console.title = 'hi'").Success);
    }

    [Fact]
    public void StructureErrorsReported()
    {
        var result = Compile("if 1" , "x = 1");
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void UsesProvidedGlobals()
    {
        //프로그램이 추가한 함수도 내장 함수처럼 재정의할수 없음
        Runtime runtime = new();
        runtime.AddFunction("hostfn" , args => 1L);
        var result = NativeJsCompiler.Compile(new[] { "hostfn = 3" } , new NativeJsOptions { Globals = runtime.GetGlobals() });
        Assert.False(result.Success);
        Assert.Contains("'hostfn'" , result.Errors[0].Message);
        Assert.True(NativeJsCompiler.Compile(new[] { "hostfn = 3" }).Success);
    }

    //명령줄: jyuno <파일> --native-js [출력 파일]
    [Fact]
    public void CliExportsNextToScript()
    {
        string dir = Directory.CreateTempSubdirectory("jyuno-nativejs-").FullName;
        try
        {
            string script = Path.Combine(dir , "hello.jyuno");
            File.WriteAllText(script , "console.writeline 'hi'\n");
            Assert.Equal(0 , Quiet(() => Cli.Program.Main(new[] { script , "--native-js" })));
            Assert.True(File.Exists(Path.Combine(dir , "hello.js")));

            string custom = Path.Combine(dir , "out" , "custom.js");
            Directory.CreateDirectory(Path.GetDirectoryName(custom)!);
            Assert.Equal(0 , Quiet(() => Cli.Program.Main(new[] { script , "--native-js" , custom })));
            Assert.True(File.Exists(custom));

            //옵션이 앞에 와도 .js 파일이면 출력 파일
            string first = Path.Combine(dir , "first.js");
            Assert.Equal(0 , Quiet(() => Cli.Program.Main(new[] { "--native-js" , first , script })));
            Assert.True(File.Exists(first));

            File.WriteAllText(script , "x = 1\nx =\n");
            Assert.Equal(1 , Quiet(() => Cli.Program.Main(new[] { script , "--native-js" , Path.Combine(dir , "bad.js") })));
            Assert.False(File.Exists(Path.Combine(dir , "bad.js")));
        }
        finally
        {
            Directory.Delete(dir , true);
        }
    }

    //--optimize(-O)로 실험적 최적화(탈출 분석, 인라이닝)를 켜도 결과가 같아야 함 (Jyuno 1.5.2부터)
    [Fact]
    public void CliOptimizeKeepsOutput()
    {
        string dir = Path.Combine(Path.GetTempPath() , "jyuno-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string script = Path.Combine(dir , "vec.jyuno");
            File.WriteAllLines(script , Script.Lines(@"
struct vec
    param x
    param y
    func plus
        param o
        return (vec (add x o.x) (add y o.y))
    endfunc
endstruct
func twice
    param v
    return (v.plus v)
endfunc
v = vec 0 0
w = vec 1 2
repeat 10
    v = v.plus w
end
u = twice v
console.writeline (add u.x u.y)
console.writeline v.x
"));
            string run(params string[] options)
            {
                TextWriter output = Console.Out;
                StringWriter writer = new();
                Console.SetOut(writer);
                try
                {
                    Assert.Equal(0 , Cli.Program.Main(new[] { script }.Concat(options).ToArray()));
                }
                finally
                {
                    Console.SetOut(output);
                }
                return writer.ToString();
            }
            string expected = run();
            Assert.Contains("60" , expected);
            Assert.Equal(expected , run("-O"));
            Assert.Equal(expected , run("--optimize"));
        }
        finally
        {
            Directory.Delete(dir , true);
        }
    }

    static int Quiet(Func<int> action)
    {
        TextWriter output = Console.Out, error = Console.Error;
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        try
        {
            return action();
        }
        finally
        {
            Console.SetOut(output);
            Console.SetError(error);
        }
    }

    //node가 있다면 변환한 코드를 실행해서 인터프리터와 출력을 비교
    public static IEnumerable<object[]> Programs() => new[] {
        new object[] { "fact" , Script.Lines(@"
func fact
	param n
	if (less n 2)
		return 1
	end
	return (mul n (fact (sub n 1)))
endfunc
console.writeline (fact 20)
console.writeline (fact 25)") },
        new object[] { "goto" , Script.Lines(@"
count = 3
loop:
console.writeline count
count = sub count 1
if count
	goto loop
end
console.writeline 'done'") },
        new object[] { "values" , Script.Lines(@"
a = array 1 'b' 2.0 true
console.writeline a
console.writeline (add 0.1 0.2)
console.writeline (div 7 2)
console.writeline (add 'x' 3)
console.writeline (mul 'ab' 3)
console.writeline (add 9223372036854775807 1)
for c 'hey'
	console.write c
	console.write '-'
end
console.writeline ''
s = 0
i = 0
while (less i 1000)
	s = add s i
	i = add i 1
end
console.writeline s") },
        new object[] { "error" , Script.Lines(@"
x = 1
console.writeline x
console.writeline (div x 0)") },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public void MatchesInterpreter(string name , string[] lines)
    {
        string? node = FindNode();
        if (node is null)
            return;
        //인터프리터
        StringWriter expected = new();
        string? expected_error = null;
        TextWriter original = Console.Out;
        Console.SetOut(expected);
        try
        {
            using Interpreter interpreter = new Runtime().Create(lines);
            interpreter.Run();
        }
        catch (JyunoException e)
        {
            expected_error = e.Message;
        }
        finally
        {
            Console.SetOut(original);
        }
        //Native JS
        var result = NativeJsCompiler.Compile(lines , new NativeJsOptions { Name = name + ".jyuno" });
        Assert.True(result.Success , string.Join("\n" , result.Errors.Select(e => e.Message)));
        string file = Path.Combine(Path.GetTempPath() , $"jyuno-nativejs-{name}-{Guid.NewGuid():N}.js");
        File.WriteAllText(file , result.Code);
        try
        {
            ProcessStartInfo info = new(node , $"\"{file}\"") { RedirectStandardOutput = true , RedirectStandardError = true };
            info.StandardOutputEncoding = info.StandardErrorEncoding = System.Text.Encoding.UTF8;
            using Process process = Process.Start(info)!;
            var stderr = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(expected.ToString().Replace("\r\n" , "\n") , stdout.Replace("\r\n" , "\n"));
            if (expected_error is null)
            {
                Assert.Equal(0 , process.ExitCode);
            }
            else
            {
                Assert.Equal(1 , process.ExitCode);
                Assert.Contains($"{name}.jyuno:{lines.Length}: 오류: {expected_error}" , stderr.Result);
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void RunApiWithHostFunctions()
    {
        string? node = FindNode();
        if (node is null)
            return;
        Runtime runtime = new();
        runtime.AddFunction("text.upper" , args => args[0]?.ToString()?.ToUpperInvariant());
        var result = NativeJsCompiler.Compile(new[] { "name = console.readline", "console.writeline (text.upper name)", "return (add 40 2)" } , new NativeJsOptions { Globals = runtime.GetGlobals() });
        Assert.True(result.Success);
        string file = Path.Combine(Path.GetTempPath() , $"jyuno-nativejs-host-{Guid.NewGuid():N}.js");
        File.WriteAllText(file , result.Code);
        try
        {
            string js = "const p = require(process.argv[1]);" +
                "const r = p.run({ input: 'jyuno\\n', host: { 'text.upper': (a) => String(a[0]).toUpperCase() } });" +
                "process.stdout.write(JSON.stringify([r.status, r.output, r.value]));";
            ProcessStartInfo info = new(node) { RedirectStandardOutput = true , RedirectStandardError = true };
            info.ArgumentList.Add("-e");
            info.ArgumentList.Add(js);
            info.ArgumentList.Add(file);
            using Process process = Process.Start(info)!;
            var stderr = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0 , stderr.Result);
            Assert.Equal("[\"ok\",\"JYUNO\\n\",42]" , stdout);
        }
        finally
        {
            File.Delete(file);
        }
    }

    internal static string? FindNode()
    {
        string exe = OperatingSystem.IsWindows() ? "node.exe" : "node";
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator , StringSplitOptions.RemoveEmptyEntries))
        {
            string path = Path.Combine(dir , exe);
            if (File.Exists(path))
                return path;
        }
        return null;
    }
}
