namespace JyunoSdk.Tests;

//Jyuno 1.4.1~1.4.2에서 추가된 내장 함수(array.*, list.*, string.*, math.random, math.csprng)가
//Native JS에서도 인터프리터와 같은 결과/오류를 내는지 비교 (node가 있을 때만)
[Collection("Console")]
public class NativeJsBuiltinTests
{
    static object[] P(string name , string code) => new object[] { name , Script.Lines(code) };

    public static IEnumerable<object[]> Programs() => new[] {
        P("collections" , @"
a = array 5 3 9 1 3
array.sort a
console.writeline (array.join a ',')
array.sort a true
console.writeline (array.join a ',')
console.writeline (array.join (array.sort (array 2 1.0 1 1.5 math.nan)) ' ')
console.writeline (array.join (array.sort (array 2 1.0 1 math.nan) true) ' ')
console.writeline (list.join (list.sort (list 'b' 'B' 'a' '가')) '')
console.writeline (array.join (array.reverse (array 1 2 3)) '')
l = list 1 2 3 4 5 6 7 8
list.shuffle l
console.writeline (list.join (list.sort l) '')
console.writeline (array.contains (array 1 2) 2.0)
console.writeline (list.indexof (list 'a' 'b' 'b') 'b')
console.writeline (array.lastindexof (array 'a' 'b' 'b') 'b')
console.writeline (array.count (array 1 1.0 2) 1)
console.writeline (array.join (array.slice (array 1 2 3 4) 1 3) ',')
console.writeline (array.join (array.slice (array 1 2 3 4) 2) ',')
console.writeline (array.join (array 1 2.5 'x' true null 3.0) '|')
console.writeline (array.join (array.new 3 7) ',')
console.writeline (length (list.new 2))
c = array.copy a
c 0 100
console.writeline (array.join a ',')
console.writeline (array.join (array.distinct (array 3 1 3.0 '3' null null 1)) ',')
console.writeline (array.binary_search (array 1 3 3 5) 3)
console.writeline (array.binary_search (array 1 3 5) 4)
console.writeline (not (array.binary_search (array 1 3 5) 6))
console.writeline (array.join (array.fill (array 1 2) 0) ',')
console.writeline (array.join (list.fill (list 1.5 2.5) 1) ',')
console.writeline (array.min (array 3 1.0 1 2))
console.writeline (array.max (array 'a' 'c' 'b'))
console.writeline (array.sum (array 1 2 3))
console.writeline (array.sum (array 1 2.5))
console.writeline (array.sum (array 9223372036854775807 1))
console.writeline (array.sum (array))
console.writeline (array.join (array.range 5) ',')
console.writeline (array.join (array.range 2 12 3) ',')
console.writeline (array.join (list.range 5 0 -2) ',')
console.writeline (array.join (array.range 9223372036854775806 9223372036854775807) ',')
console.writeline (array.join (array.range 9223372036854775807 -1 -9223372036854775807) ',')
console.writeline (length (array.range 0))
l = list
list.push l 1 'a' null
console.writeline (list.join l ',')
console.writeline (list.pop l)
list.insert l 0 'first'
list.insert l 3 'last'
console.writeline (list.join l ',')
console.writeline (list.removeat l 1)
console.writeline (list.remove l 'a')
console.writeline (list.remove l 'zz')
console.writeline (list.join l ',')
console.writeline (length (list.clear l))
n = list 1.5
list.push n 2
console.writeline (list.join n ',')") ,
        P("strings" , @"
console.writeline (string.concat 'a' 1 null true 2.5 3.0)
console.writeline (string.join '-' 1 2 3)
console.writeline (string.join ', ' (list 'a' 'b'))
console.writeline (string.join null 'x' 'y')
console.writeline (string.format '{0} + {1} = {2}, {3}' 1 2.5 3.5 true)
console.writeline (string.format '[{0,4}] [{1,-4}] {2:F2} {3:D5} {{x}}' 7 'ab' 3.14159 42)
console.writeline (string.compare 'B' 'a')
console.writeline (string.compare 'b' 'a')
console.writeline (string.compare null 'a')
console.writeline (string.compare 'a' 'a')
console.writeline (string.equals 'Jyuno' 'JYUNO' true)
console.writeline (string.equals 'Jyuno' 'JYUNO')
console.writeline (string.equals 'straße' 'STRASSE' true)
console.writeline (string.isnullorempty null)
console.writeline (string.isnullorempty '')
console.writeline (string.isnullorwhitespace ' \n')
console.writeline (string.isnullorwhitespace 'a')
console.writeline (string.indexof 'abcabc' 'c')
console.writeline (string.indexof 'abcabc' 'c' 3)
console.writeline (string.indexof 'abc' '' 3)
console.writeline (string.lastindexof 'abcabc' 'bc')
console.writeline (string.lastindexof 'abc' '')
console.writeline (string.contains 'jyuno' 'un')
console.writeline (string.startswith 'jyuno' 'jy')
console.writeline (string.endswith 'jyuno' 'NO')
console.writeline (string.replace 'aaaa' 'aa' 'b')
console.writeline (string.replace 'hello' 'l' null)
console.writeline (string.substring 'jyuno' 1 3)
console.writeline (string.substring 'jyuno' 5)
console.writeline (string.insert 'jyuno' 3 '-')
console.writeline (string.remove 'jyuno' 1 2)
console.writeline (string.remove 'jyuno' 2)
console.writeline (string.toupper 'abc àßı ǆ ᾳ')
console.writeline (string.tolower 'ABC ÀİΣ ǅ')
console.writeline (string.concat '[' (string.trim '  a b \n') ']')
console.writeline (string.concat '[' (string.trimstart '  a  ') ']')
console.writeline (string.concat '[' (string.trimend '  a  ') ']')
console.writeline (string.trim 'xyabcyx' 'xy')
console.writeline (string.trimstart 'xyabcyx' 'yx')
console.writeline (string.trim ' a ' '')
console.writeline (string.padleft '7' 3 '0')
console.writeline (string.concat '[' (string.padright '7' 3) ']')
console.writeline (string.padleft '1234' 2)
console.writeline (array.join (string.split 'a,b,,c' ',') '|')
console.writeline (array.join (string.split 'a b\n\tc') '|')
console.writeline (array.join (string.split 'abc' '') '|')
console.writeline (array.join (string.tochararray 'jy가') '|')
console.writeline (string.charcode 'ABC')
console.writeline (string.charcode '가나' 1)
console.writeline (string.fromcharcode 72 105 44032)
console.writeline (length (string.fromcharcode))
console.writeline (string 12)") ,
        P("random" , @"
ok = true
repeat 200
	x = math.random 6
	if (or (less x 0) (greater x 5) (not (equal x (int x))))
		ok = false
	end
	y = math.csprng -3 3
	if (or (less y -3) (greater y 2))
		ok = false
	end
	z = math.random 1.0 1.5
	if (or (less z 1.0) (not (less z 1.5)))
		ok = false
	end
	w = math.csprng
	if (or (less w 0) (not (less w 1)))
		ok = false
	end
	v = math.csprng -9223372036854775808 9223372036854775807
	u = math.random 0 9223372036854775807
	if (less u 0)
		ok = false
	end
end
console.writeline ok
console.writeline (math.random 10 11)
console.writeline (math.csprng 1)") ,
    };

    //마지막 줄에서 나는 오류의 메시지가 같아야 함
    public static IEnumerable<object[]> Errors() => new[] {
        "array.sort (array 1 'a' 2)", "array.sort 5", "array.sort", "array.sort null", "list.indexof (list 1)",
        "array.slice (array 1 2) 3", "array.slice (array 1 2) 1 0", "array.slice (array 1 2) 'a'", "array.slice (array 1)",
        "array.new -1", "array.new 1.5", "array.new", "list.push (array 1) 2", "list.push (list 1 2) 3 'x'",
        "list.pop (list)", "list.insert (list 1) 2 3", "list.insert (list 1) 0", "list.removeat (list 1) 1", "list.removeat (list) 0",
        "list.removeat (list 1)", "list.clear (array 1)", "array.min (array)", "array.max (array 1 'a')", "array.sum (array 1 'a')",
        "array.range 0 10 0", "array.range 1.5", "array.range", "array.range -9223372036854775808 9223372036854775807",
        "array.fill (array 1) 'a'", "array.binary_search (array 1 2) 'a'", "array.count (array 1)", "array.max (list 2.5 null)",
        "string.format '{1}' 'a'", "string.format 1", "string.compare 'a' 1", "string.equals 1 1", "string.isnullorempty 0",
        "string.indexof 'abc' 'a' 4", "string.contains 'abc' 1", "string.contains 'abc'", "string.contains null 'a'",
        "string.replace 'a' '' 'b'", "string.substring 'jyuno' 6", "string.substring 'jyuno' 2 4", "string.remove 'abc' 1 5",
        "string.padleft '7' 3 '00'", "string.padleft '7' -1", "string.charcode ''", "string.charcode 'a' 1",
        "string.fromcharcode 65536", "string.fromcharcode 'a'", "string.insert 'a' 2 'b'", "string.toupper (array 1)",
        "math.random 0", "math.csprng 3 3", "math.random 1.5 1.0", "math.csprng 0 math.nan", "math.random 'a'", "math.csprng null",
        "math.random -5",
    }.Select((code , i) => new object[] { $"error{i}" , new[] { "x = 1" , code } });

    [Theory]
    [MemberData(nameof(Programs))]
    [MemberData(nameof(Errors))]
    public void MatchesInterpreter(string name , string[] lines) => new NativeJsTests().MatchesInterpreter(name , lines);
}
