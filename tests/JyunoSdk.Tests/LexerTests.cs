using Jyuno.Compiler;
using JyunoSdk.Analysis;

namespace JyunoSdk.Tests;

public class LexerTests
{
    //SDK의 토큰화기는 Jyuno의 토큰화기와 결과(종류, 값)가 항상 같아야 함
    static void AssertSameAsJyuno(string line)
    {
        var expected = Parser.Tokenizer(line).Select(t => (t.type, t.value)).ToArray();
        var actual = Lexer.Tokenize(line).Select(t => (t.Type, t.Value)).ToArray();
        Assert.True(expected.SequenceEqual(actual) , $"줄: {line}\n기대: {string.Join(" | " , expected)}\n실제: {string.Join(" | " , actual)}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a = add 1 2")]
    [InlineData("\tconsole.write \"hello {1}\" 'world!'")]
    [InlineData("x = -614")]
    [InlineData("x = - 614")]
    [InlineData("x = 6.14")]
    [InlineData("x = 1.")]
    [InlineData("x = 99999999999999999999")]
    [InlineData("s = 'I am \\'Jyunni\\'.'")]
    [InlineData("s = \"line\\nnext\\r\\\\\"")]
    [InlineData("s = 'never closed")]
    [InlineData("s = 'ends with backslash\\")]
    [InlineData("loop:")]
    [InlineData("614 = jyunni")]
    [InlineData("if (and (greater x 0) (less x 10))")]
    [InlineData("local i = 0")]
    [InlineData("for x (array 1 2 3)")]
    [InlineData("a\tb = 1")]
    [InlineData("x=add(1)(2)")]
    [InlineData("endfunc")]
    [InlineData("a.b.c [1] ~@<>*/+")]
    [InlineData("'a'\"b\"'c'")]
    [InlineData("x = -1-2-a")]
    //연산자 (Jyuno 1.6.0)
    [InlineData("x = a+b*-3 - -4")]
    [InlineData("x=(a)-1")]
    [InlineData("x -= 1")]
    [InlineData("a <<= 2 >>= 3 ??= 4")]
    [InlineData("if a<=b&&c!=d||!e")]
    [InlineData("x = a ? b : c")]
    [InlineData("x = 1 // 주석 'not closed")]
    [InlineData("// 주석만")]
    [InlineData("x = a / b")]
    [InlineData("x = 'a//b' // c")]
    [InlineData("func +")]
    [InlineData("y = ~x ^ 3 % 2 & 1 | 0")]
    public void SameAsJyuno(string line) => AssertSameAsJyuno(line);

    [Fact]
    public void RandomLinesSameAsJyuno()
    {
        const string alphabet = "abxz09-.'\"\\ ()=:+\t　가<>!&|?/*%^~";
        Random random = new(614);
        for (int i = 0 ; i < 5000 ; i++)
        {
            int length = random.Next(0 , 24);
            char[] chars = new char[length];
            for (int c = 0 ; c < length ; c++)
                chars[c] = alphabet[random.Next(alphabet.Length)];
            AssertSameAsJyuno(new string(chars));
        }
    }

    [Fact]
    public void PositionsCoverSourceText()
    {
        string line = "\tx = add 'a b' -3 (f y)";
        var tokens = Lexer.Tokenize(line);
        Assert.Equal(new[] { "x" , "=" , "add" , "'a b'" , "-3" , "(" , "f" , "y" , ")" } , tokens.Select(t => line[t.Start..t.End]));
    }

    [Fact]
    public void ErrorTokenSpansRestOfLine()
    {
        string line = "x = 'abc  ";
        var error = Lexer.Tokenize(line).Last();
        Assert.Equal(TokenType.Error , error.Type);
        Assert.Equal(4 , error.Start);
        Assert.Equal(line.TrimEnd().Length , error.End);
    }

    [Theory]
    [InlineData("a\nb\r\nc\rd" , new[] { "a" , "b" , "c" , "d" })]
    [InlineData("a\n" , new[] { "a" , "" })]
    [InlineData("" , new[] { "" })]
    public void SplitLines(string text , string[] lines) => Assert.Equal(lines , Lexer.SplitLines(text));

    [Theory]
    [InlineData("count" , true)]
    [InlineData("my.var" , true)]
    [InlineData("한글" , true)]
    [InlineData("1abc" , false)]
    [InlineData("a b" , false)]
    [InlineData("if" , false)]
    [InlineData("a=b" , false)]
    [InlineData("" , false)]
    public void ValidNames(string name , bool valid) => Assert.Equal(valid , Lexer.IsValidName(name));
}
