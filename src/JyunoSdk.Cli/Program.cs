using Jyuno;
using JyunoSdk.Analysis;
using JyunoSdk.LanguageServer;
using JyunoSdk.NativeJs;

namespace JyunoSdk.Cli;

public static class Program
{
    public static string SdkVersion { get; } = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public static string JyunoVersion { get; } = typeof(Runtime).Assembly.GetName().Version?.ToString(3) ?? "?";

    public const string Usage = @"사용법:
  jyuno                          대화형 프롬프트(REPL)를 실행합니다.
  jyuno <파일> [옵션]            스크립트 파일을 실행합니다.
  jyuno run <파일> [옵션]        스크립트 파일을 실행합니다.
  jyuno <파일> --native-js [출력 파일]
                                 실행하지 않고 JavaScript(Native JS)로 변환합니다.
                                 (출력 파일을 생략하면 스크립트 이름의 확장자를 .js로 바꾼 파일)
  jyuno check <파일...> [옵션]   스크립트를 실행하지 않고 문법/이름 문제를 검사합니다.
  jyuno format <파일...> [--write] [--spaces <n>]
                                 블록 구조에 맞춰 들여쓰기를 정리합니다. (--write: 파일에 저장)
  jyuno lsp                      Jyuno 언어 서버(LSP)를 표준 입출력으로 실행합니다.
  jyuno --version                버전을 출력합니다.

옵션:
  --file, -file                  파일/디렉터리를 다루는 명령어(file.*, directory.*)를 허용합니다.
  --commands <묶음,...>          사용할 명령어 묶음 (console, math, file, default, all)
  --host <이름,...>              (check) 프로그램이 따로 제공하는 함수/변수 이름
  --native-js [출력 파일]        Native JS로 변환해서 저장합니다. (node 출력파일.js 로 실행)";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length is 0)
            return new Repl().Run();
        string command = args[0];
        string[] rest = args[1..];
        try
        {
            switch (command)
            {
                case "--version" or "-v" or "version":
                    Console.WriteLine($"JyunoSdk {SdkVersion} (Jyuno {JyunoVersion})");
                    return 0;
                case "--help" or "-h" or "help":
                    Console.WriteLine(Usage);
                    return 0;
                case "repl" or "prompt":
                    return new Repl().Run();
                case "run":
                    return RunFile(rest);
                case "check":
                    return Check(rest);
                case "format" or "fmt":
                    return Format(rest);
                case "lsp" or "language-server":
                    //VS Code의 languageclient는 --stdio 등의 인자를 붙이므로 나머지 인자는 무시
                    return new JyunoLanguageServer(Console.OpenStandardInput() , Console.OpenStandardOutput() , Console.Error).Run();
                default:
                    //이전 JyunoPrompt처럼 jyuno <파일> [-file] 형식도 지원
                    return RunFile(args);
            }
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 2;
        }
    }

    sealed class UsageException : Exception
    {
        public UsageException(string message) : base(message) { }
    }

    sealed record Arguments(List<string> Files , CommandGroups Commands , List<string> Hosts , bool Write , int? Spaces , bool NativeJs , string? NativeJsOutput);

    static Arguments Parse(string[] args , CommandGroups commands)
    {
        List<string> files = new();
        List<string> hosts = new();
        bool write = false;
        int? spaces = null;
        bool native_js = false;
        string? native_js_output = null;
        for (int i = 0 ; i < args.Length ; i++)
        {
            string arg = args[i];
            string value() => ++i < args.Length ? args[i] : throw new UsageException($"'{arg}' 다음에 값이 필요합니다.");
            switch (arg.ToLowerInvariant())
            {
                case "--file" or "-file" or "--allow-file":
                    commands |= CommandGroups.File;
                    break;
                case "-console" or "-default" or "-math":
                    //이전 JyunoPrompt의 옵션 (기본으로 포함됨)
                    break;
                case "--commands":
                    commands = CommandGroups.Essential;
                    foreach (string group in value().Split(',' , StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        commands |= group.ToLowerInvariant() switch {
                            "console" => CommandGroups.Console,
                            "math" => CommandGroups.Math,
                            "file" => CommandGroups.File,
                            "default" => CommandGroups.Default,
                            "all" => CommandGroups.All,
                            "essential" or "none" => CommandGroups.Essential,
                            _ => throw new UsageException($"'{group}'은/는 알수 없는 명령어 묶음입니다.")
                        };
                    }
                    break;
                case "--host":
                    hosts.AddRange(value().Split(',' , StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--native-js" or "-native-js":
                    native_js = true;
                    //다음 인자가 옵션이 아니고, .js 파일이거나 스크립트 파일을 이미 받았다면 출력 파일 이름
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-') && (is_js(args[i + 1]) || files.Count > 0))
                        native_js_output = args[++i];
                    break;
                case "--write" or "-w":
                    write = true;
                    break;
                case "--spaces":
                    spaces = int.TryParse(value() , out int n) && n > 0 ? n : throw new UsageException("--spaces 다음에는 양의 정수가 와야 합니다.");
                    break;
                default:
                    if (arg.StartsWith('-') && arg.Length > 1 && !File.Exists(arg))
                        throw new UsageException($"'{arg}'은/는 알수 없는 옵션입니다.");
                    files.Add(arg);
                    break;
            }
        }
        return new(files , commands , hosts , write , spaces , native_js , native_js_output);
    }

    static bool is_js(string path) => Path.GetExtension(path).ToLowerInvariant() is ".js" or ".mjs" or ".cjs";

    static Runtime.AddJyunoCommandType ToRuntime(CommandGroups groups) => (Runtime.AddJyunoCommandType)(int)groups;

    static int RunFile(string[] args)
    {
        var parsed = Parse(args , CommandGroups.Default);
        if (parsed.Files.Count is not 1)
            throw new UsageException("실행할 파일 하나를 지정해야 합니다.");
        string path = parsed.Files[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"'{path}' 파일이 존재하지 않습니다.");
            return 1;
        }
        Runtime runtime = new(ToRuntime(parsed.Commands));
        if (parsed.NativeJs)
            return ExportNativeJs(path , parsed.NativeJsOutput ?? Path.ChangeExtension(path , ".js") , runtime , parsed.Commands);
        using Interpreter interpreter = runtime.Create(File.ReadAllLines(path));
        try
        {
            object? result = interpreter.Run();
            if (result is Jyuno.Language.GrammarError error)
            {
                Console.Error.WriteLine($"{path}:{error.Line + 1}: 문법 오류: {error.Message}");
                return 1;
            }
            return 0;
        }
        catch (JyunoException e)
        {
            Console.Error.WriteLine($"{path}: 오류: {e.Message}");
            return 1;
        }
    }

    //Jyuno 코드를 JavaScript로 변환해서 저장 (실행하지 않음)
    static int ExportNativeJs(string path , string output , Runtime runtime , CommandGroups commands)
    {
        if (commands.HasFlag(CommandGroups.File))
            Console.Error.WriteLine("경고: Native JS는 파일/디렉터리 명령어(file.*, directory.*)를 지원하지 않습니다.");
        string source = File.ReadAllText(path);
        var result = NativeJsCompiler.Compile(source , new NativeJsOptions { Globals = runtime.GetGlobals() , Name = Path.GetFileName(path) });
        if (!result.Success)
        {
            foreach (var error in result.Errors)
                Console.Error.WriteLine($"{path}:{error.Line + 1}: 오류: {error.Message}");
            return 1;
        }
        File.WriteAllText(output , result.Code , new System.Text.UTF8Encoding(false));
        Console.WriteLine($"{path} → {output} (Native JS, 실행: node {output})");
        return 0;
    }

    static int Check(string[] args)
    {
        var parsed = Parse(args , CommandGroups.Default);
        if (parsed.Files.Count is 0)
            throw new UsageException("검사할 파일을 지정해야 합니다.");
        AnalyzerOptions options = new() { Commands = parsed.Commands , HostNames = parsed.Hosts };
        int errors = 0, warnings = 0;
        foreach (string path in parsed.Files)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"'{path}' 파일이 존재하지 않습니다.");
                errors++;
                continue;
            }
            var document = JyunoDocument.Parse(File.ReadAllText(path) , options);
            foreach (var diagnostic in document.Diagnostics)
            {
                Console.WriteLine($"{path}:{diagnostic}");
                if (diagnostic.Severity is DiagnosticSeverity.Error)
                    errors++;
                else if (diagnostic.Severity is DiagnosticSeverity.Warning)
                    warnings++;
            }
        }
        Console.WriteLine($"오류 {errors}개, 경고 {warnings}개");
        return errors > 0 ? 1 : 0;
    }

    static int Format(string[] args)
    {
        var parsed = Parse(args , CommandGroups.Default);
        if (parsed.Files.Count is 0)
            throw new UsageException("정리할 파일을 지정해야 합니다.");
        foreach (string path in parsed.Files)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"'{path}' 파일이 존재하지 않습니다.");
                return 1;
            }
            string text = File.ReadAllText(path);
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            string formatted = Formatter.FormatText(text , parsed.Spaces ?? 4 , parsed.Spaces is not null).Replace("\n" , newline);
            if (parsed.Write)
            {
                if (formatted != text)
                    File.WriteAllText(path , formatted);
            }
            else
                Console.Write(formatted);
        }
        return 0;
    }
}
