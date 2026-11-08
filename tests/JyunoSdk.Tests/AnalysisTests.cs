using JyunoSdk.Analysis;

namespace JyunoSdk.Tests;

public class AnalysisTests
{
    static JyunoDocument Parse(params string[] lines) => JyunoDocument.Parse(lines);

    static string[] Codes(JyunoDocument document) => document.Diagnostics.Select(d => d.Code).ToArray();

    //언어 문서(Language/README.md)의 예제들: 문제가 하나도 없어야 함
    public static IEnumerable<object[]> ValidPrograms() => new[] {
        new object[] { Script.Lines(@"
func fact
	param n
	if (equal n 0)
		return 1
	end
	return (mul n (fact (sub n 1)))
endfunc
console.writeline (fact 5)") },
        new object[] { Script.Lines(@"
count = 3
loop:
console.writeline count
count = sub count 1
if count
	goto loop
end") },
        new object[] { Script.Lines(@"
total = 0
func add_score
	param score
	total = add total score
endfunc
add_score 10
add_score 20
console.writeline total") },
        new object[] { Script.Lines(@"
i = 'outside'
func f
	local i = 0
	repeat 3
		i = add i 1
	end
	console.writeline i
endfunc
f
console.writeline i") },
        new object[] { Script.Lines(@"
sum = 0
for x (array 1 2 3)
	sum = add sum x
end
console.writeline sum") },
        new object[] { Script.Lines(@"
l = array 10 20 30
l 0 (l 2)
console.writeline (l 0)
words = div 'hello jyuno world' ' '
console.writeline (words 1)
if (and (greater x 0) (less x 10))
	console.writeline 'x는 한 자리 자연수입니다.'
else
	x =
end
614 = jyunni
console.writeline jyunni math.pi") },
        new object[] { Script.Lines(@"
func prime
    param n
    if (less n 2)
        return false
    end
    local i = 2
    while (not (greater (mul i i) n))
        if (equal (mod n i) 0)
            return false
        end
        i = add i 1
    end
    return true
endfunc
m = int (console.readline)
c = 0
k = 1
repeat m
    if (prime k)
        c = add c 1
    end
    k = add k 1
end
console.writeline c") },
    };

    [Theory]
    [MemberData(nameof(ValidPrograms))]
    public void ValidProgramsHaveNoDiagnostics(string[] lines)
    {
        //x는 위 예제에서 일부러 정의하지 않음
        var document = JyunoDocument.Parse(lines , new AnalyzerOptions { HostNames = new[] { "x" } });
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void BlockStructure()
    {
        var document = Parse("if a" , "  b" , "else" , "  c" , "end" , "func f" , "  while 1" , "    break" , "  end" , "endfunc");
        Assert.Equal(3 , document.Blocks.Count);
        var branch = document.Blocks[0];
        Assert.Equal((0 , 2 , 4) , (branch.OpenLine , branch.ElseLine , branch.CloseLine));
        var func = document.Blocks[1];
        Assert.Equal(BlockKind.Func , func.Kind);
        Assert.Equal("f" , func.FunctionName);
        Assert.Equal(9 , func.CloseLine);
        Assert.Equal(new[] { 0 , 1 , 0 , 1 , 0 , 0 , 1 , 2 , 1 , 0 } , document.Lines.Select(l => l.Depth));
    }

    [Theory]
    [InlineData(new[] { "if 1" } , "모든 조건문(if, while 등) 키워드는 end 키워드로 종료 표시가 있어야 합니다.")]
    [InlineData(new[] { "end" } , "end와 짝이 되는 if, while, repeat, for 키워드가 없습니다.")]
    [InlineData(new[] { "func f" } , "func 키워드는 endfunc 키워드로 종료 표시가 있어야 합니다.")]
    [InlineData(new[] { "endfunc" } , "endfunc와 짝이 되는 func 키워드가 없습니다.")]
    [InlineData(new[] { "if 1" , "else" , "else" , "end" } , "else는 if와 end 사이에 한번만 쓸수 있습니다.")]
    [InlineData(new[] { "break" } , "break는 반복문(while, repeat, for) 안에서만 쓸수 있습니다.")]
    [InlineData(new[] { "param x" } , "param은 func와 endfunc 사이에서만 쓸수 있습니다.")]
    [InlineData(new[] { "repeat 1" , "func f" , "break" , "endfunc" , "end" } , "break는 반복문(while, repeat, for) 안에서만 쓸수 있습니다.")]
    public void BlockErrorsMatchJyuno(string[] lines , string message)
    {
        var document = Parse(lines);
        Assert.Contains(document.Diagnostics , d => d.Code == DiagnosticCodes.BlockError && d.Message == message);
        //Jyuno도 같은 오류를 냄
        var result = new Jyuno.Runtime().Create(lines).Run();
        if (result is Jyuno.Language.GrammarError error)
            Assert.Equal(message , error.Message);
    }

    [Fact]
    public void EndFuncClosesUnclosedInnerBlocks()
    {
        var document = Parse("func f" , "if 1" , "endfunc" , "x = 1");
        var inner = document.Blocks[1];
        Assert.False(inner.IsClosed);
        Assert.Equal(1 , inner.EndLine);
        Assert.True(document.Blocks[0].IsClosed);
        Assert.Single(document.Diagnostics , d => d.Code == DiagnosticCodes.BlockError);
    }

    [Fact]
    public void FunctionLocalsAndOuterVariables()
    {
        var document = Parse(
            "total = 0" ,
            "func f" ,
            "  param n" ,
            "  total = add total n" ,
            "  temp = 1" ,
            "  local total2 = temp" ,
            "endfunc" ,
            "f 1");
        var total = document.Script.Symbols["total"];
        Assert.Equal(2 , total.Definitions.Count); //함수 안의 대입도 바깥 변수를 바꿈
        var function = document.Blocks[0].Scope!;
        Assert.Equal(ScopeKind.Function , function.Kind);
        Assert.Contains("temp" , function.Symbols.Keys); //바깥에 없으므로 지역 변수
        Assert.Contains("total2" , function.Symbols.Keys);
        Assert.Equal(SymbolKind.Parameter , function.Symbols["n"].Kind);
        Assert.Equal(SymbolKind.Function , document.Script.Symbols["f"].Kind);
        Assert.Equal(new[] { "n" } , document.Script.Symbols["f"].FunctionBlock!.Parameters);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void LocalShadowsOuterVariable()
    {
        var document = Parse("i = 1" , "func f" , "local i = 0" , "i = add i 1" , "endfunc");
        var inner = document.Blocks[0].Scope!.Symbols["i"];
        Assert.Equal(SymbolKind.Local , inner.Kind);
        Assert.Equal(2 , inner.Definitions.Count);
        Assert.Single(document.Script.Symbols["i"].Definitions);
    }

    [Fact]
    public void ForVariableIsLocalToLoop()
    {
        var document = Parse("for x (array 1 2)" , "y = x" , "end" , "console.writeline x");
        var loop = document.Blocks[0].Scope!;
        Assert.Equal(ScopeKind.Loop , loop.Kind);
        Assert.Equal(SymbolKind.LoopVariable , loop.Symbols["x"].Kind);
        Assert.Contains("y" , loop.Symbols.Keys);
        //for 밖의 x는 정의되지 않음
        Assert.Single(document.Diagnostics , d => d.Code == DiagnosticCodes.UndefinedName && d.Line == 3);
    }

    [Fact]
    public void CallerLocalsAreNotVisible()
    {
        var document = Parse("func g" , "console.writeline secret" , "endfunc" , "func f" , "secret = 1" , "g" , "endfunc");
        Assert.Contains(document.Diagnostics , d => d.Code == DiagnosticCodes.UndefinedName && d.Line == 1);
    }

    [Fact]
    public void UndefinedNameAndHostNames()
    {
        Assert.Equal(new[] { DiagnosticCodes.UndefinedName } , Codes(Parse("text.at 'abc' 0")));
        Assert.Empty(JyunoDocument.Parse(new[] { "text.at 'abc' 0" } , new AnalyzerOptions { HostNames = new[] { "text.at" } }).Diagnostics);
        Assert.Empty(JyunoDocument.Parse(new[] { "text.at 'abc' 0" } , new AnalyzerOptions { ReportUndefinedNames = false }).Diagnostics);
    }

    [Fact]
    public void FileCommandsNeedFileGroup()
    {
        Assert.Equal(new[] { DiagnosticCodes.CommandGroup } , Codes(Parse("x = file.read 'a.txt'")));
        Assert.Empty(JyunoDocument.Parse(new[] { "x = file.read 'a.txt'" } , new AnalyzerOptions { Commands = CommandGroups.All }).Diagnostics);
    }

    [Fact]
    public void FunctionUsedAsValue()
    {
        var document = Parse("func f" , "return 1" , "endfunc" , "x = add f 1" , "return add 1 2" , "y = add (f) 1");
        var warnings = document.Diagnostics.Where(d => d.Code == DiagnosticCodes.FunctionAsValue).ToList();
        Assert.Equal(new[] { 3 , 4 } , warnings.Select(d => d.Line));
        //return add 1 2: 첫 값만 사용됨
        Assert.Contains(document.Diagnostics , d => d.Code == DiagnosticCodes.KeywordArguments && d.Line == 4);
    }

    [Fact]
    public void Parentheses()
    {
        Assert.Contains(DiagnosticCodes.Parenthesis , Codes(Parse("x = add (mul 1 2")));
        Assert.Contains(DiagnosticCodes.Parenthesis , Codes(Parse("(add 1 2")));
        Assert.Contains(DiagnosticCodes.Ignored , Codes(Parse("add 1 2) 3")));
        Assert.Contains(DiagnosticCodes.Ignored , Codes(Parse("(add 1 2) 3")));
        Assert.Empty(Parse("x = (add (mul 1 2) (sub 3 4))").Diagnostics);
    }

    [Fact]
    public void OperatorsAndKeywordsInWrongPlaces()
    {
        Assert.Contains(DiagnosticCodes.Ignored , Codes(Parse("x = add 1 + 2")));
        Assert.Contains(DiagnosticCodes.Ignored , Codes(Parse("+ 1 2")));
        Assert.Contains(DiagnosticCodes.MisplacedKeyword , Codes(Parse("x = (if 1)")));
        Assert.Contains(DiagnosticCodes.Ignored , Codes(Parse("while 1" , "end 3")));
    }

    [Fact]
    public void KeywordSyntax()
    {
        Assert.Contains(DiagnosticCodes.KeywordSyntax , Codes(Parse("func" , "endfunc")));
        Assert.Contains(DiagnosticCodes.KeywordSyntax , Codes(Parse("func f g" , "endfunc")));
        Assert.Contains(DiagnosticCodes.KeywordSyntax , Codes(Parse("for (array 1)" , "end")));
        Assert.Contains(DiagnosticCodes.KeywordSyntax , Codes(Parse("for x" , "end")));
        Assert.Contains(DiagnosticCodes.KeywordSyntax , Codes(Parse("local x 1")));
        Assert.Contains(DiagnosticCodes.KeywordSyntax , Codes(Parse("local x =")));
        Assert.Contains(DiagnosticCodes.KeywordArguments , Codes(Parse("goto")));
        Assert.Contains(DiagnosticCodes.KeywordArguments , Codes(Parse("if" , "end")));
    }

    [Fact]
    public void TokenErrorsSuppressOtherSyntaxErrors()
    {
        var document = Parse("x = (add 'abc");
        Assert.Equal(new[] { DiagnosticCodes.TokenError } , Codes(document));
    }

    [Fact]
    public void GotoAcrossFunction()
    {
        var document = Parse("top:" , "func f" , "goto top" , "endfunc");
        Assert.Contains(DiagnosticCodes.GotoAcrossFunction , Codes(document));
        Assert.DoesNotContain(DiagnosticCodes.GotoAcrossFunction , Codes(Parse("func f" , "inner:" , "goto inner" , "endfunc")));
    }

    [Fact]
    public void CallSites()
    {
        var document = Parse("x = add 1 (mul 2 3) y");
        var outer = document.CallAt(0 , 8)!;
        Assert.Equal("add" , outer.Head.Name);
        Assert.Equal(3 , outer.Arguments.Count);
        var inner = document.CallAt(0 , 15)!;
        Assert.Equal("mul" , inner.Head.Name);
        Assert.Equal(2 , inner.Arguments.Count);
    }

    [Fact]
    public void OccurrencesOfSymbol()
    {
        var document = Parse("a = 1" , "b = add a a" , "a =");
        var a = document.NameAt(1 , 9)!;
        Assert.Equal("a" , a.Name);
        Assert.Equal(4 , document.OccurrencesOf(a).Count());
    }

    [Fact]
    public void Formatter()
    {
        string text = "func f\nparam n\n  if n\nreturn 1\n   else\nreturn 2  \nend\nendfunc\n";
        string expected = "func f\n\tparam n\n\tif n\n\t\treturn 1\n\telse\n\t\treturn 2\n\tend\nendfunc\n";
        Assert.Equal(expected , Analysis.Formatter.FormatText(text));
        Assert.Equal("if 1\n  x = 1\nend" , Analysis.Formatter.FormatText("if 1\nx = 1\nend" , 2 , true));
    }
}

public class AnalysisFuzzTests
{
    static readonly string[] fragments = {
        "if" , "else" , "end" , "while" , "repeat" , "for" , "break" , "goto" , "func" , "param" , "endfunc" , "return" , "local" ,
        "x" , "y" , "f" , "add" , "console.writeline" , "math.pi" , "file.read" , "=" , ":" , "(" , ")" , "+" , "1" , "-2" , "3.5" ,
        "'s'" , "\"t" , "lbl:" , "array" , "\t" , " " , "99999999999999999999"
    };

    //어떤 입력이라도 분석과 언어 서버 기능이 예외를 내지 않아야 함
    [Fact]
    public void RandomDocumentsDoNotThrow()
    {
        Random random = new(5020);
        for (int doc = 0 ; doc < 400 ; doc++)
        {
            string[] lines = new string[random.Next(1 , 12)];
            for (int l = 0 ; l < lines.Length ; l++)
                lines[l] = string.Join(random.Next(3) is 0 ? "" : " " , Enumerable.Range(0 , random.Next(0 , 7)).Select(_ => fragments[random.Next(fragments.Length)]));
            var document = JyunoDocument.Parse(lines , new AnalyzerOptions { Commands = CommandGroups.Default });
            _ = JyunoSdk.LanguageServer.JyunoLanguageServer.ClassifyTokens(document).ToList();
            _ = Formatter.Format(document);
            for (int l = 0 ; l < lines.Length ; l++)
            {
                for (int c = 0 ; c <= lines[l].Length ; c++)
                {
                    if (document.NameAt(l , c) is NameOccurrence name)
                    {
                        _ = document.OccurrencesOf(name).ToList();
                        _ = JyunoSdk.LanguageServer.JyunoLanguageServer.Describe(document , name);
                    }
                    _ = document.CallAt(l , c);
                    _ = document.VisibleSymbols(l).ToList();
                }
            }
            //진단 위치는 줄 안에 있어야 함
            foreach (var diagnostic in document.Diagnostics)
            {
                Assert.InRange(diagnostic.Line , 0 , lines.Length - 1);
                Assert.InRange(diagnostic.Start , 0 , lines[diagnostic.Line].Length);
                Assert.InRange(diagnostic.End , diagnostic.Start , lines[diagnostic.Line].Length);
            }
        }
    }

    //블록 구조 오류는 Jyuno가 실행할 때 내는 문법 오류와 같아야 함 (실행이 그 줄에 도달하는 경우)
    [Fact]
    public void BlockErrorsAgreeWithJyuno()
    {
        string[] keywords = { "if 0" , "else" , "end" , "repeat 0" , "func f" , "endfunc" , "break" , "param p" , "x = 1" };
        Random random = new(614);
        for (int doc = 0 ; doc < 2000 ; doc++)
        {
            string[] lines = Enumerable.Range(0 , random.Next(1 , 7)).Select(_ => keywords[random.Next(keywords.Length)]).ToArray();
            var document = JyunoDocument.Parse(lines);
            object? result;
            try
            {
                result = new Jyuno.Runtime().Create(lines).Run();
            }
            catch (Jyuno.JyunoException)
            {
                continue;
            }
            if (result is Jyuno.Language.GrammarError error)
                Assert.Contains(document.Diagnostics , d => d.Code == DiagnosticCodes.BlockError && d.Line == error.Line && d.Message == error.Message);
        }
    }
}
