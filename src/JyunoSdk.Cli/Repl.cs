using System.Collections;
using System.Globalization;
using System.Text;
using Jyuno;
using Jyuno.Compiler;
using Jyuno.Language;
using JyunoSdk.Analysis;

namespace JyunoSdk.Cli;

/// <summary>
/// 대화형 Jyuno 프롬프트 (REPL)
/// 한 줄씩 실행하며, if/while/repeat/for/func 블록은 end/endfunc로 닫힐 때까지 입력받은 다음 한번에 실행합니다.
/// </summary>
public sealed class Repl
{
    readonly TextReader input;
    readonly TextWriter output;
    readonly bool color;
    readonly List<string> block = new();

    public Repl(TextReader? input = null , TextWriter? output = null , bool? color = null)
    {
        this.input = input ?? Console.In;
        this.output = output ?? Console.Out;
        this.color = color ?? (input is null && output is null && !Console.IsOutputRedirected);
        Runtime = new(Runtime.AddJyunoCommandType.All);
        Interpreter = Runtime.Create();

        Runtime.AddFunction("exit" , _ => {
            Running = false;
            return null;
        });
        Runtime.AddFunction("copyright" , _ => "All rights reserved jyunni.");
        Runtime.AddFunction("help" , args => args.Length > 0 && args[0] is string name ? Help(name) : HelpContent);
        Runtime.AddVariable(".input" , () => CommandLineText , v => CommandLineText = v as string ?? CommandLineText);
        Runtime.AddVariable(".return" , () => ReturnText , v => ReturnText = v as string ?? ReturnText);
        Runtime.AddVariable(".error" , () => ErrorText , v => ErrorText = v as string ?? ErrorText);
        Runtime.AddVariable(".remove" , () => Interpreter.EnableRemoveVariable , v => Interpreter.EnableRemoveVariable = Parser.IsTrue(v));
        Runtime.AddVariable(".substitute" , () => !Interpreter.BlockSubstitute , v => Interpreter.BlockSubstitute = !Parser.IsTrue(v));
        Runtime.AddFunction("execute" , args => {
            string path = args.Length is 0 ? "jyuno" : (string)(args[0] ?? "jyuno");
            if (!File.Exists(path))
                throw new JyunoException($"'{path}' 파일이 존재하지 않습니다.");
            using Interpreter script = Runtime.Create(File.ReadAllLines(path));
            return script.Run();
        });
        Runtime.AddFunction("history" , _ => string.Join('\n' , Interpreter.scripts.Select(s => s.script)));
        Runtime.AddFunction("eval" , args => Interpreter.ExecuteLine(args.Length > 0 ? args[0] as string ?? string.Empty : string.Empty));
    }

    public Runtime Runtime { get; }
    public Interpreter Interpreter { get; }
    public bool Running { get; private set; } = true;
    public string CommandLineText = ">>> ";
    public string ContinuationText = "... ";
    public string ReturnText = "{0}";
    public string ErrorText = "오류: {0}";
    public string ExceptionText = "C# 예외: {0}";

    public static string Banner => $"Jyuno {Program.JyunoVersion} 대화형 프롬프트 (JyunoSdk {Program.SdkVersion})\n도움말은 help, 종료는 exit 입니다.";

    public int Run()
    {
        if (color)
        {
            try { Console.Title = "Jyuno Prompt"; } catch { }
        }
        WriteLine(Banner , ConsoleColor.DarkGray);
        while (Running)
        {
            Write(block.Count is 0 ? CommandLineText : ContinuationText , ConsoleColor.Green);
            string? line = input.ReadLine();
            if (line is null)
            {
                //입력이 끝남 (Ctrl+D, Ctrl+Z 또는 파이프)
                if (block.Count > 0)
                    WriteLine("\n블록 입력을 취소했습니다." , ConsoleColor.DarkGray);
                output.WriteLine();
                break;
            }
            Submit(line);
        }
        return 0;
    }

    /// <summary>한 줄을 입력합니다. (블록 안이라면 블록이 닫힐 때 실행)</summary>
    public void Submit(string line)
    {
        if (block.Count > 0)
        {
            if (line.Trim() is ".cancel")
            {
                block.Clear();
                WriteLine("블록 입력을 취소했습니다." , ConsoleColor.DarkGray);
                return;
            }
            block.Add(line);
            if (IsComplete(block))
                ExecuteBlock();
            return;
        }
        string trimmed = line.Trim();
        if (trimmed.Length is 0)
            return;
        var tokens = Lexer.Tokenize(line);
        //help add 처럼 따옴표 없이 이름을 넘긴 경우
        if (tokens.Count is 2 && tokens[0].Name is "help" && tokens[1].Type is TokenType.Name or TokenType.Keyword)
        {
            WriteLine(Help(line.Trim()[4..].Trim()) , null);
            return;
        }
        if (tokens.Count > 0 && tokens[0].Type is TokenType.Keyword)
        {
            switch ((KeywordType)tokens[0].Value)
            {
                case KeywordType.If:
                case KeywordType.While:
                case KeywordType.Repeat:
                case KeywordType.For:
                case KeywordType.Func:
                case KeywordType.Struct:
                    block.Add(line);
                    if (IsComplete(block))
                        ExecuteBlock();
                    else if (!hinted)
                    {
                        hinted = true;
                        WriteLine("(블록을 end/endfunc/endstruct로 닫으면 실행됩니다. 취소하려면 .cancel)" , ConsoleColor.DarkGray);
                    }
                    return;
                case KeywordType.Goto:
                    WriteError("goto는 프롬프트에서 쓸수 없습니다. 파일로 저장해서 execute \"파일\" 또는 jyuno run 으로 실행하세요.");
                    return;
            }
        }
        last_start = -1;
        Execute(() => Interpreter.ExecuteLine(line));
    }
    bool hinted = false;
    //마지막으로 실행한 블록의 첫 줄 (한 줄만 실행했다면 -1)
    int last_start = -1;

    /// <summary>입력이 여러 줄 블록 중간인지 (블록 입력 중)</summary>
    public bool InBlock => block.Count > 0;

    //모든 블록이 닫혔는지 (endfunc가 안쪽의 닫히지 않은 블록을 같이 닫는 경우도 Jyuno와 같게 처리)
    static bool IsComplete(List<string> lines)
    {
        var document = JyunoDocument.Parse(lines);
        int last = lines.Count - 1;
        return !document.Blocks.Any(b => !b.IsClosed && b.EndLine == last);
    }

    void ExecuteBlock()
    {
        int start = Interpreter.scripts.Count;
        foreach (string line in block)
            Interpreter.scripts.Add(new CommandLine(line));
        block.Clear();
        last_start = start;
        Execute(() => Interpreter.Run(start));
    }

    void Execute(Func<object?> action)
    {
        try
        {
            object? result = action();
            switch (result)
            {
                case GrammarError error:
                    WriteError(last_start >= 0 && error.Line >= last_start ? $"{error.Message} (블록의 {error.Line - last_start + 1}번째 줄)" : error.Message);
                    break;
                case ReturnInfo info:
                    if (info.value is not null)
                        WriteLine(string.Format(ReturnText , Show(info.value)) , ConsoleColor.Gray);
                    break;
                case null:
                    break;
                default:
                    WriteLine(string.Format(ReturnText , Show(result)) , ConsoleColor.Gray);
                    break;
            }
        }
        catch (JyunoException e)
        {
            WriteError(e.Message);
        }
        catch (Exception e)
        {
            WriteLine(string.Format(ExceptionText , e.Message) , ConsoleColor.Red);
        }
    }

    /// <summary>값을 Jyuno 코드처럼 보여줌</summary>
    public static string Show(object? value , bool nested = false) => value switch {
        null => "null",
        bool b => b ? "true" : "false",
        string s => nested ? "'" + s.Replace("\\" , "\\\\").Replace("'" , "\\'").Replace("\n" , "\\n") + "'" : s,
        double d => ShowReal(d),
        IFormattable f => f.ToString(null , CultureInfo.InvariantCulture),
        IList list => "[" + string.Join(", " , list.Cast<object?>().Select(o => Show(o , true))) + "]",
        _ => value.ToString() ?? "null"
    };

    static string ShowReal(double value)
    {
        string text = value.ToString("R" , CultureInfo.InvariantCulture);
        return double.IsFinite(value) && !text.Contains('.') && !text.Contains('E') ? text + ".0" : text;
    }

    void Write(string text , ConsoleColor? foreground)
    {
        if (color && foreground is ConsoleColor c)
        {
            Console.ForegroundColor = c;
            output.Write(text);
            Console.ResetColor();
        }
        else
            output.Write(text);
    }
    void WriteLine(string text , ConsoleColor? foreground) => Write(text + Environment.NewLine , foreground);
    void WriteError(string message) => WriteLine(string.Format(ErrorText , message) , ConsoleColor.Red);

    /// <summary>이름(내장 명령어, 키워드, 프롬프트 명령어)에 대한 설명</summary>
    public static string Help(string name)
    {
        if (Builtins.TryGet(name , out var builtin))
            return Plain(builtin.ToMarkdown());
        if (Keywords.All.FirstOrDefault(k => k.Name == name) is KeywordInfo keyword)
            return Plain(keyword.ToMarkdown());
        if (PromptCommands.FirstOrDefault(c => c.Name == name) is var (command, description) && command is not null)
            return $"{command}\n{description}\n\n(프롬프트에서만 제공)";
        return $"'{name}'에 대한 도움말이 없습니다. help 로 전체 목록을 보세요.";
    }

    //마크다운 코드 블록 표시 제거
    static string Plain(string markdown) => markdown.Replace("```jyuno\n" , "").Replace("```\n" , "").Replace("*" , "").TrimEnd();

    static readonly (string Name, string Description)[] PromptCommands = {
        ("exit" , "프롬프트를 나갑니다."),
        ("help" , "도움말을 봅니다. help (이름) 으로 함수/키워드 설명을 볼수 있습니다."),
        ("execute" , "execute (파일 경로): 파일을 실행합니다."),
        ("history" , "지금까지 입력한 줄을 반환합니다."),
        ("eval" , "eval (문자열): 문자열을 한 줄의 코드로 실행합니다."),
        ("copyright" , "저작권 정보를 반환합니다."),
        (".input" , "(변수) 입력 표시 ('>>> ')"),
        (".return" , "(변수) 결과 표시 서식 ('{0}')"),
        (".error" , "(변수) 오류 표시 서식 ('오류: {0}')"),
        (".remove" , "(변수) 변수/함수 삭제 연산 허용 여부"),
        (".substitute" , "(변수) 대입 연산 허용 여부"),
        (".cancel" , "블록(if, func 등)을 입력하는 중에 입력하면 그 블록을 취소합니다."),
    };

    public static string HelpContent
    {
        get
        {
            StringBuilder sb = new();
            sb.AppendLine("[Jyuno 도움말]");
            sb.AppendLine("자료형: int, double, string, bool, array, list, null");
            sb.AppendLine("help (이름) 으로 자세한 설명을 볼수 있습니다. (예: help add, help for)");
            sb.AppendLine();
            sb.AppendLine("키워드: " + string.Join(", " , Keywords.All.Select(k => k.Name)));
            foreach (var group in Builtins.All.Values.GroupBy(b => b.GroupName))
                sb.AppendLine($"{group.Key} 명령어: " + string.Join(", " , group.Select(b => b.Name)));
            sb.AppendLine();
            sb.AppendLine("프롬프트 명령어:");
            foreach (var (name , description) in PromptCommands)
                sb.AppendLine($"- {name} : {description}");
            sb.AppendLine();
            sb.AppendLine("if/while/repeat/for/func 블록은 여러 줄로 입력하면 end/endfunc로 닫을 때 실행됩니다.");
            sb.Append("언어 문서: https://github.com/jyunrcaea/Jyuno/blob/master/Language/README.md");
            return sb.ToString();
        }
    }
}
