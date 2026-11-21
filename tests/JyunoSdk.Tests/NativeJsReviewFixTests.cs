namespace JyunoSdk.Tests;

//Jyuno 1.6.2에서 바뀐 동작(배열/리스트의 문자열, 사칙연산 오류, 구조체 비교, 식 뒤에 남은 값 등)을
//Native JS에서도 인터프리터와 같게 내는지 비교 (node가 있을 때만)
[Collection("Console")]
public class NativeJsReviewFixTests
{
    static object[] P(string name , string code) => new object[] { name , Script.Lines(code) };

    public static IEnumerable<object[]> Programs() => new[] {
        P("list_text" , @"
console.writeline (list 1 'a' null 1.5 true (array 2))
console.writeline (string (list))
console.writeline (add 'x' (list 1 2) 3)
l = list 1 'a'
list.push l l
console.writeline l
console.writeline '{1}|{2}' (list 1 2) 3
console.writeline (string.format '{0}' (array 'a' 'b'))
console.writeline (array.join (list (list 1) 2) ';')
console.write 1.5
console.writeline ''
"),
        P("arithmetic" , @"
console.writeline (mod -9223372036854775808 -1)
console.writeline (sub 'abc' '')
console.writeline (sub 'abcb' 'b')
console.writeline (add null 'x')
console.writeline (math.abs -5)
console.writeline (math.abs -2.5)
console.writeline (mul 'ab' 3)
console.writeline (mul '' 900000000)
"),
        P("struct_equality" , @"
flag = true
struct s
if flag
x = 1
y = 2
else
y = 2
x = 1
end
endstruct
p = s
flag = false
q = s
console.writeline (equal p q)
console.writeline p
console.writeline q
struct box
param l
endstruct
a = list 1 'a'
list.push a a
console.writeline (equal (box a) (box a))
g = box (0.0 / 0.0)
console.writeline (equal g g)
"),
    };

    //마지막 줄에서 나는 오류의 메시지가 같아야 함
    public static IEnumerable<object[]> Errors() => new[] {
        "add true 1", "add null 1", "add null null", "mul 2 true", "div true 2", "mod 'a' 2", "y = x + true",
        "sub 'abc' 1", "sub 'abc' null", "math.abs 'a'", "math.abs -9223372036854775808", "math.abs true",
        "mul 'ab' 300000000", "div -9223372036854775808 -1", "string.padleft '7' 600000000",
        "console.writeline '{5}' 1", "console.write 1 2", "console.writeline (list 1) 2", "console.writeline null 1",
        "y = (list 1) < 2", "less (array 1) 2", "string.toupper (list 1 2)",
        "y = 5 -3", "w = 1 2 3", "y = (1 2)", "y = " + new string('(' , 700) + "1" + new string(')' , 700),
    }.Select((code , i) => new object[] { $"fix_error{i}" , new[] { "x = 1" , code } });

    [Theory]
    [MemberData(nameof(Programs))]
    [MemberData(nameof(Errors))]
    public void MatchesInterpreter(string name , string[] lines) => new NativeJsTests().MatchesInterpreter(name , lines);
}
