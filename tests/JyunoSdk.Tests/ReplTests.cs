using JyunoSdk.Cli;

namespace JyunoSdk.Tests;

//Jyuno의 console 명령어는 System.Console을 직접 사용하므로 병렬로 실행하지 않음
[CollectionDefinition("Console" , DisableParallelization = true)]
public class ConsoleCollection { }

[Collection("Console")]
public class ReplTests
{
    static string RunRepl(string input)
    {
        StringWriter output = new();
        TextWriter original = Console.Out;
        Console.SetOut(output);
        try
        {
            new Repl(new StringReader(input) , output , false).Run();
        }
        finally
        {
            Console.SetOut(original);
        }
        return output.ToString().Replace("\r\n" , "\n");
    }

    [Fact]
    public void EvaluatesLines()
    {
        string output = RunRepl("x = add 1 2\nx\narray 1 'a' 2.0\ntrue\n");
        Assert.Contains(">>> 3\n" , output);
        Assert.Contains("[1, 'a', 2.0]" , output);
        Assert.Contains(">>> true\n" , output);
    }

    [Fact]
    public void MultiLineBlocks()
    {
        string output = RunRepl("func double\nparam n\nreturn (mul n 2)\nendfunc\ndouble 21\nfor c 'ab'\nconsole.writeline c\nend\n");
        Assert.Contains("42" , output);
        Assert.Contains("a\nb\n" , output);
        Assert.Contains("... " , output);
    }

    [Fact]
    public void NestedBlocksWaitForOutermostEnd()
    {
        string output = RunRepl("repeat 2\nif 1\nconsole.writeline 'hi'\nend\nend\n");
        Assert.Equal(2 , output.Split("hi").Length - 1);
    }

    [Fact]
    public void CancelBlock()
    {
        string output = RunRepl("if 1\nconsole.writeline 'never'\n.cancel\nconsole.writeline 'after'\n");
        Assert.DoesNotContain("never\n" , output.Replace("console.writeline 'never'" , ""));
        Assert.Contains("after" , output);
        Assert.Contains("취소" , output);
    }

    [Fact]
    public void ErrorsAreReported()
    {
        string output = RunRepl("nope\nx = 'abc\n");
        Assert.Contains("오류: 'nope'은/는 존재하지 않는 함수/변수/명령어 입니다." , output);
        Assert.Contains("문자열은 항상 따옴표로 끝나야 합니다." , output);
    }

    [Fact]
    public void ExitStopsPrompt()
    {
        string output = RunRepl("exit\nconsole.writeline 'not printed'\n");
        Assert.DoesNotContain("not printed\n" , output.Replace("console.writeline 'not printed'" , ""));
    }

    [Fact]
    public void Help()
    {
        Assert.Contains("math.pow" , RunRepl("help\n"));
        Assert.Contains("거듭제곱" , RunRepl("help math.pow\n"));
        Assert.Contains("반복" , RunRepl("help for\n"));
        Assert.Contains("거듭제곱" , RunRepl("help 'math.pow'\n"));
    }

    [Fact]
    public void PromptVariables()
    {
        string output = RunRepl(".input = '$ '\n1\n");
        Assert.Contains("$ 1" , output);
    }

    [Theory]
    [InlineData(null , "null")]
    [InlineData(3L , "3")]
    [InlineData(2.0 , "2.0")]
    [InlineData(0.5 , "0.5")]
    [InlineData(false , "false")]
    [InlineData("a'b" , "a'b")]
    public void Show(object? value , string expected) => Assert.Equal(expected , Repl.Show(value));

    [Fact]
    public void ShowCollections() => Assert.Equal("['a\\'b', [1, 2], null]" , Repl.Show(new object?[] { "a'b" , new long[] { 1 , 2 } , null }));
}

[Collection("Console")]
public class CliTests
{
    static (int Code, string Output, string Error) Run(params string[] args)
    {
        StringWriter output = new(), error = new();
        TextWriter original_out = Console.Out, original_error = Console.Error;
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            int code = Program.Main(args);
            return (code , output.ToString() , error.ToString());
        }
        finally
        {
            Console.SetOut(original_out);
            Console.SetError(original_error);
        }
    }

    static string Temp(string text)
    {
        string path = Path.Combine(Path.GetTempPath() , $"jyuno-test-{Guid.NewGuid():N}.jyuno");
        File.WriteAllText(path , text);
        return path;
    }

    [Fact]
    public void Version() => Assert.Contains("JyunoSdk" , Run("--version").Output);

    [Fact]
    public void RunsFile()
    {
        string path = Temp("console.writeline (add 1 2)\n");
        var (code, output, _) = Run(path);
        Assert.Equal(0 , code);
        Assert.Equal("3" , output.Trim());
        Assert.Equal("3" , Run("run" , path).Output.Trim());
    }

    [Fact]
    public void RunReportsGrammarErrors()
    {
        var (code, _, error) = Run("run" , Temp("if 1\n"));
        Assert.Equal(1 , code);
        Assert.Contains(":1: 문법 오류" , error);
    }

    [Fact]
    public void FileCommandsNeedFlag()
    {
        string path = Temp("console.writeline (file.exist 'nothing.txt')\n");
        Assert.Equal(1 , Run("run" , path).Code);
        var (code, output, _) = Run("run" , path , "--file");
        Assert.Equal(0 , code);
        Assert.Equal("False" , output.Trim());
        Assert.Equal(0 , Run(path , "-file").Code);
    }

    [Fact]
    public void Check()
    {
        var (code, output, _) = Run("check" , Temp("if 1\nx = text.at 'a' 0\n"));
        Assert.Equal(1 , code);
        Assert.Contains("JY1002" , output);
        Assert.Contains("JY2001" , output);
        Assert.Equal(0 , Run("check" , Temp("x = text.at 'a' 0\n") , "--host" , "text.at").Code);
    }

    [Fact]
    public void FormatWrite()
    {
        string path = Temp("if 1\r\nx = 1\r\nend\r\n");
        Assert.Equal(0 , Run("format" , path , "--write").Code);
        Assert.Equal("if 1\r\n\tx = 1\r\nend\r\n" , File.ReadAllText(path));
    }

    [Fact]
    public void UnknownOption()
    {
        var (code, _, error) = Run("run" , "--nope");
        Assert.Equal(2 , code);
        Assert.Contains("--nope" , error);
    }
}
