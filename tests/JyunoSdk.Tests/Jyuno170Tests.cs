using JyunoSdk.Analysis;
using JyunoSdk.NativeJs;

namespace JyunoSdk.Tests;

//Jyuno 1.7.0의 시간 제한 블록(ttl ~ endttl)과 예외 처리(try ~ catch ~ tried, throw) (0.6.0)
[Collection("Console")]
public class Jyuno170Tests
{
    static JyunoDocument Parse(params string[] lines) => JyunoDocument.Parse(lines);

    [Fact]
    public void TtlAndTryBlocks_HaveNoDiagnostics()
    {
        var document = Parse(
            "done = false" ,
            "ttl 0.5" ,
            "try" ,
            "x = div 1 0" ,
            "catch e" ,
            "console.writeline e" ,
            "throw" ,
            "tried" ,
            "done = true" ,
            "endttl" ,
            "console.writeline e done");
        Assert.Empty(document.Diagnostics);
        Assert.Equal(2 , document.Blocks.Count);
        Block limit = document.Blocks[0], attempt = document.Blocks[1];
        Assert.Equal((BlockKind.Ttl , 1 , 9) , (limit.Kind , limit.OpenLine , limit.CloseLine));
        Assert.Equal((BlockKind.Try , 2 , 4 , 7) , (attempt.Kind , attempt.OpenLine , attempt.CatchLine , attempt.CloseLine));
        //ttl과 try는 새 영역을 만들지 않음
        Assert.Null(limit.Scope);
        Assert.Null(attempt.Scope);
        Assert.Equal(new[] { 0 , 0 , 1 , 2 , 1 , 2 , 2 , 1 , 1 , 0 , 0 } , document.Lines.Select(l => l.Depth));
    }

    [Theory]
    [InlineData(new[] { "ttl 1" } , "ttl 키워드는 endttl 키워드로 종료 표시가 있어야 합니다.")]
    [InlineData(new[] { "endttl" } , "endttl과 짝이 되는 ttl 키워드가 없습니다.")]
    [InlineData(new[] { "ttl 1" , "end" , "endttl" } , "end와 짝이 되는 if, while, repeat, for 키워드가 없습니다. (ttl은 endttl로 닫아야 합니다)")]
    [InlineData(new[] { "ttl 1" , "break" , "endttl" } , "break는 반복문(while, repeat, for) 안에서만 쓸수 있습니다.")]
    [InlineData(new[] { "try" } , "try 키워드는 tried 키워드로 종료 표시가 있어야 합니다.")]
    [InlineData(new[] { "try" , "catch e" } , "try 키워드는 tried 키워드로 종료 표시가 있어야 합니다.")]
    [InlineData(new[] { "tried" } , "tried와 짝이 되는 try 키워드가 없습니다.")]
    [InlineData(new[] { "try" , "tried" } , "try와 tried 사이에는 catch가 있어야 합니다.")]
    [InlineData(new[] { "catch e" } , "catch는 try와 tried 사이에 한번만 쓸수 있습니다.")]
    [InlineData(new[] { "try" , "catch e" , "catch f" , "tried" } , "catch는 try와 tried 사이에 한번만 쓸수 있습니다.")]
    [InlineData(new[] { "try" , "catch e f" , "tried" } , "catch 다음에는 오류를 받을 변수 이름 하나만 올수 있습니다.")]
    [InlineData(new[] { "try" , "catch 1" , "tried" } , "catch 다음에는 오류를 받을 변수 이름 하나만 올수 있습니다.")]
    [InlineData(new[] { "try x" , "catch" , "tried" } , "try 다음에는 아무것도 올수 없습니다.")]
    [InlineData(new[] { "try" , "catch" , "tried x" } , "tried 다음에는 아무것도 올수 없습니다.")]
    [InlineData(new[] { "try" , "end" , "catch" , "tried" } , "end와 짝이 되는 if, while, repeat, for 키워드가 없습니다. (try는 tried로 닫아야 합니다)")]
    [InlineData(new[] { "throw" } , "값 없는 throw는 catch와 tried 사이에서만 쓸수 있습니다. (처리중인 오류를 다시 던짐)")]
    [InlineData(new[] { "try" , "throw" , "catch" , "tried" } , "값 없는 throw는 catch와 tried 사이에서만 쓸수 있습니다. (처리중인 오류를 다시 던짐)")]
    [InlineData(new[] { "try" , "catch" , "func f" , "throw" , "endfunc" , "tried" } , "값 없는 throw는 catch와 tried 사이에서만 쓸수 있습니다. (처리중인 오류를 다시 던짐)")]
    public void BlockErrorsMatchJyuno(string[] lines , string message)
    {
        var document = Parse(lines);
        Assert.Contains(document.Diagnostics , d => d.Code == DiagnosticCodes.BlockError && d.Message == message);
        //Jyuno도 그 줄에 도달하면 같은 오류를 냄 (실행되지 않는 catch 본문/함수 안의 줄은 도달하지 않음)
        var result = new Jyuno.Runtime().Create(lines).Run();
        if (result is Jyuno.Language.GrammarError error)
            Assert.Equal(message , error.Message);
        else
            Assert.True(lines.Contains("catch f") || lines.Contains("func f") , "Jyuno가 문법 오류를 내지 않음");
    }

    [Fact]
    public void BreakAndRethrow_InsideNestedBlocks_AreValid()
    {
        //break는 ttl/try를 건너뛰고 바깥 반복문을 빠져나감, 값 없는 throw는 catch 안의 if 안에서도 쓸수 있음
        var document = Parse(
            "repeat 3" , "ttl 1" , "try" , "break" , "catch e" , "if e" , "throw" , "end" , "tried" , "endttl" , "end");
        Assert.DoesNotContain(document.Diagnostics , d => d.Code == DiagnosticCodes.BlockError);
    }

    [Theory]
    [InlineData("ttl" , DiagnosticSeverity.Error , "ttl 다음에는 제한 시간(초)이 와야 합니다.")]
    [InlineData("ttl 1 2" , DiagnosticSeverity.Error , "ttl 다음에는 제한 시간(초) 하나만 와야 합니다.")]
    [InlineData("throw 1 2" , DiagnosticSeverity.Error , "throw 다음에는 던질 값 하나만 와야 합니다.")]
    public void KeywordArguments(string first , DiagnosticSeverity severity , string message)
    {
        string[] lines = first.StartsWith("ttl") ? new[] { first , "endttl" } : new[] { first };
        var document = Parse(lines);
        var diagnostic = Assert.Single(document.Diagnostics , d => d.Code == DiagnosticCodes.KeywordArguments);
        Assert.Equal((severity , message) , (diagnostic.Severity , diagnostic.Message));
    }

    [Fact]
    public void TtlAndThrowArguments_AreExpressions()
    {
        //ttl과 throw 다음은 연산자 식, 이름은 읽기로 셈
        var document = Parse("t = 2" , "ttl t * 2" , "throw (add t 1)" , "endttl" , "try" , "throw 'x'" , "catch err" , "tried" , "console.writeline err");
        Assert.Empty(document.Diagnostics);
        Assert.Contains(document.Names , n => n.Name == "t" && n.Line == 1 && !n.IsDefinition);
        //catch의 이름은 일반 대입처럼 정의
        Assert.Contains(document.Names , n => n.Name == "err" && n.Line == 6 && n.Role == OccurrenceRole.Assignment);
        //정의하지 않은 이름은 경고
        Assert.Contains(Parse("ttl missing" , "endttl").Diagnostics , d => d.Code == DiagnosticCodes.UndefinedName);
    }

    [Fact]
    public void FormatterIndentsTtlAndTry()
    {
        string text = "ttl 1\ntry\nx = 1\ncatch e\nx = 2\ntried\nendttl";
        string expected = "ttl 1\n\ttry\n\t\tx = 1\n\tcatch e\n\t\tx = 2\n\ttried\nendttl";
        Assert.Equal(expected , Formatter.FormatText(text));
    }

    [Fact]
    public void KeywordsHaveDocumentation()
    {
        foreach (var type in new[] { Jyuno.Language.KeywordType.Ttl , Jyuno.Language.KeywordType.EndTtl , Jyuno.Language.KeywordType.Try , Jyuno.Language.KeywordType.Catch , Jyuno.Language.KeywordType.Tried , Jyuno.Language.KeywordType.Throw })
            Assert.NotNull(Keywords.Get(type));
    }

    [Theory]
    [InlineData("ttl 1|x = 1|endttl" , 0 , "ttl")]
    [InlineData("try|x = 1|catch e|tried" , 0 , "try")]
    [InlineData("throw 'x'" , 0 , "throw")]
    public void NativeJs_RejectsTtlAndTry(string code , int line , string fragment)
    {
        var result = NativeJsCompiler.Compile(code.Split('|'));
        Assert.False(result.Success);
        Assert.Contains(result.Errors , e => e.Line == line && e.Message.Contains("Native JS 제약") && e.Message.Contains(fragment));
    }

    static string RunRepl(string input)
    {
        StringWriter output = new();
        TextWriter original = Console.Out;
        Console.SetOut(output);
        try
        {
            new JyunoSdk.Cli.Repl(new StringReader(input) , output , false).Run();
        }
        finally
        {
            Console.SetOut(original);
        }
        return output.ToString().Replace("\r\n" , "\n");
    }

    [Fact]
    public void Repl_RunsTtlAndTryBlocks()
    {
        string output = RunRepl("try\nx = div 1 0\ncatch e\nconsole.writeline (add 'caught ' e)\ntried\nttl 0.05\nwhile true\nend\nendttl\nconsole.writeline 'after'\n");
        Assert.Contains("caught " , output);
        Assert.Contains("after\n" , output);
    }
}
