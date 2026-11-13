namespace JyunoSdk.Tests;

//연산자(Jyuno 1.6.0)가 Native JS에서 인터프리터와 같은 결과를 내는지 비교 (node가 있을 때만)
//Jyuno 1.6.0 이상이 필요: dotnet test -p:JyunoSource=../Jyuno/Jyuno.csproj
[Collection("Console")]
public class NativeJsOperatorTests
{
    static object[] P(string name , string code) => new object[] { name , Script.Lines(code) };

    public static IEnumerable<object[]> Programs() => new[] {
        P("Arithmetic" , @"
a = 7
b = 2
console.writeline (a + b * 3 - -a)
console.writeline ((a + b) * 3 % 5)
console.writeline (a / b)
console.writeline (a / 2.0)
console.writeline (a - 1.5)
console.writeline (-a + +b)
console.writeline (- 2.5)
console.writeline (-(-3))
console.writeline ('ab' + 1 + 2)
console.writeline (1 + 2 + 'ab')
console.writeline ('ab' * 3)
console.writeline ('a,b,c' / ',')
console.writeline (string (list 1 2 + 3))
console.writeline (9223372036854775807 + 1)
console.writeline (-9223372036854775807 - 1 - 1)
console.writeline (4611686018427387904 * 4)
x = 10
x -= 3
x *= 2
x /= 3
x %= 3
console.writeline x
s = 'a'
s += 'b'
s += 1
console.writeline s"),
        P("Bitwise" , @"
console.writeline (6 & 3)
console.writeline (6 | 3)
console.writeline (6 ^ 3)
console.writeline (~5)
console.writeline (~-1)
console.writeline (1 << 62)
console.writeline (1 << 63)
console.writeline (1 << 64)
console.writeline (3 << -1)
console.writeline (-9 >> 1)
console.writeline (-1 >> 70)
console.writeline (9007199254740993 >> 1)
console.writeline (123456789 << 30)
console.writeline (true ^ false)
console.writeline ('a' ^ '')
console.writeline (true & 1)
console.writeline (0 | false)
x = 1
x <<= 10
x >>= 2
x |= 1
x &= 7
x ^= 2
console.writeline x"),
        P("Comparison" , @"
console.writeline (1 < 2)
console.writeline (2 <= 2)
console.writeline (3 > 2.5)
console.writeline (2 >= 2.0)
console.writeline (1 == 1.0)
console.writeline (1 != 1.0)
console.writeline ('a' < 'b')
console.writeline ('b' >= 'a')
console.writeline (false < true)
console.writeline (null == null)
console.writeline (null != 0)
console.writeline ((math.nan) < 1)
console.writeline ((math.nan) >= 1)
console.writeline ((math.nan) == (math.nan))
console.writeline ('1' == 1)
l = list 1 2
console.writeline (l == l)
console.writeline (l == (list 1 2))"),
        P("Logic_ShortCircuit" , @"
count = 0
func tick
  param v
  count += 1
  return v
endfunc
console.writeline (tick false && tick true)
console.writeline (tick true || tick false)
console.writeline (tick 0 || tick '')
console.writeline (tick null ?? tick 5)
console.writeline (tick 3 ?? tick 5)
console.writeline (tick 1 ? tick 'yes' : tick 'no')
console.writeline (tick 0 ? tick 'yes' : tick 'no')
console.writeline count
console.writeline (!0)
console.writeline (!'a')
console.writeline (!!null)
console.writeline (true ? 1 : false ? 2 : 3)
console.writeline (null ?? null ?? 'c')
x = null
x ??= 4
x ??= 5
console.writeline x"),
        P("Precedence_And_Calls" , @"
func sq
  param v
  return v * v
endfunc
console.writeline (sq 3 + sq 4)
console.writeline (add 1 2 * 10)
console.writeline (1 + 2 == 3 && 2 * 3 > 5 || false)
console.writeline (1 | 2 ^ 3 & 4)
console.writeline (2 + 3 << 1)
console.writeline (5 - 3 - 1)
console.writeline (100 / 10 / 5)
x = 5
console.writeline (x-1)
l = list 10 20 30
console.writeline (l 1 + l 2)
console.writeline (length l * 2)
console.writeline (x -1)"),
        P("Keyword_Arguments" , @"
func f
  param n
  if n % 2 == 0 && n > 0
    return n / 2
  end
  return n * 3 + 1
endfunc
n = 6
steps = 0
while n != 1
  n = f n
  steps += 1
end
console.writeline steps
total = 0
repeat 2 + 1
  total += 2
end
console.writeline total
for v (array 1 2 3)
  total += v * v
end
console.writeline total
func g
  return f 7
endfunc
console.writeline (g)"),
        P("Comments" , @"
// 주석 줄
x = 1 // 줄 끝 주석
console.writeline (x + 1) // 출력
console.writeline 'a // 문자열 안은 주석이 아님'"),
        P("Struct_PointExample" , @"
struct point
  param x
  param y
  func +
    param other
    return point (x + other.x) (y + other.y)
  endfunc
endstruct
p1 = point 10 20
p2 = point -5 4
if p1 + p2 == (add p1 p2)
  console.writeline ""success""
end
console.writeline (string (p1 + p2))
console.writeline (string (add p1 p2 p2))
console.writeline (string p1)"),
        P("Struct_ReceiverIsCopy" , @"
struct v
  param x
  func +
    param o
    x = x + o.x
    o.x = 999
    return v x
  endfunc
  func -
    param o
    if null == o
      x = -x
      return v x
    end
    return v (x - o.x)
  endfunc
  func ==
    param o
    if null == o
      return false
    end
    x = 12345
    return x == o.x
  endfunc
endstruct
a = v 1
b = v 2
c = a + b
console.writeline (string c)
console.writeline (string (a - b))
console.writeline (string (-a))
console.writeline (string a)
console.writeline (string b)
console.writeline (a == b)
console.writeline (a != b)
console.writeline (equal a b)
console.writeline (a == a)
d = a + a + a
console.writeline (string d)
a += b
console.writeline (string a)
a -= b
console.writeline (string a)
console.writeline (a == null)
console.writeline (a != null)
console.writeline (null == a)"),
        P("Struct_DerivedComparisons" , @"
struct ver
  param major
  param minor
  func <
    param o
    return major < o.major || (major == o.major && minor < o.minor)
  endfunc
  func ==
    param o
    return major == o.major && minor == o.minor
  endfunc
endstruct
a = ver 1 2
b = ver 1 10
c = ver 2 0
console.writeline (a < b)
console.writeline (b < a)
console.writeline (a > b)
console.writeline (a <= a)
console.writeline (a >= b)
console.writeline (b >= a)
console.writeline (less a b c)
console.writeline (less a c b)
console.writeline (greater c b a)
console.writeline (equal a (ver 1 2))
console.writeline (a != (ver 1 2))
console.writeline (a != b)
l = list c a b
array.sort l
console.writeline (string.join ' ' (list (string (l 0)) (string (l 1)) (string (l 2))))
console.writeline (string (array.max l))
console.writeline (array.binary_search l (ver 1 10))"),
        P("Struct_RawResults" , @"
struct odd
  param v
  func <
    param o
    return 'less?'
  endfunc
  func ==
    param o
    return 0
  endfunc
  func !
    return 'not!'
  endfunc
endstruct
a = odd 1
b = odd 2
console.writeline (a < b)
console.writeline (b > a)
console.writeline (less a b)
console.writeline (a == b)
console.writeline (a != b)
console.writeline (equal a b)
console.writeline (!a)
console.writeline (not a)
if a == b
  console.writeline 'yes'
else
  console.writeline 'no'
end"),
        P("Struct_Unary_And_Logic" , @"
struct flag
  param on
  func !
    return flag (!on)
  endfunc
  func ~
    return flag (on ? 0 : 1)
  endfunc
  func &
    param o
    return flag (on && o.on)
  endfunc
  func |
    param o
    return flag (on || o.on)
  endfunc
  func ^
    param o
    return flag (on ^ o.on)
  endfunc
endstruct
t = flag true
f = flag false
console.writeline (string (!t))
console.writeline (string (~t))
console.writeline (string (t & f))
console.writeline (string (t | f))
console.writeline (string (t ^ t))
console.writeline (string (not t))
console.writeline (string (and t f t))
console.writeline (string (or f f t))
console.writeline (t && f)
console.writeline (string (f ?? t))
s = flag 1
s &= t
s |= f
s ^= t
console.writeline (string s)"),
        P("Struct_OperandOrder" , @"
struct c
  param n
  func +
    param o
    return c (n + o.n)
  endfunc
endstruct
func bump
  a.n += 10
  return c 3
endfunc
a = c 1
console.writeline (string (a + (bump)))
console.writeline (string a)
console.writeline (string (add a (bump)))
console.writeline (string a)"),
        P("Struct_InStruct" , @"
struct vec
  param x
  param y
  func +
    param o
    return vec (x + o.x) (y + o.y)
  endfunc
  func -
    param o
    return o == null ? vec (-x) (-y) : vec (x - o.x) (y - o.y)
  endfunc
endstruct
struct seg
  param from
  param to
  func len
    d = to - from
    return d.x * d.x + d.y * d.y
  endfunc
  func +
    param o
    return seg (from + o) (to + o)
  endfunc
endstruct
s = seg (vec 0 0) (vec 3 4)
t = s + (vec 1 1)
console.writeline (string t)
console.writeline (s.len)
console.writeline (t.len)
console.writeline (string (-s.to))
console.writeline (string (s.from - s.to))
s.to += vec 1 1
console.writeline (string s)
console.writeline s.to.x
arr = array (vec 1 1) (vec 2 2)
arr 0 ((arr 0) + (arr 1))
console.writeline (string (arr 0))"),
        P("Struct_Collections" , @"
struct money
  param amount
  param unit
  func value
    return unit == 'k' ? amount * 1000 : amount
  endfunc
  func ==
    param o
    return (value) == (o.value)
  endfunc
  func <
    param o
    return (value) < (o.value)
  endfunc
endstruct
func show
  param l
  r = ''
  for m l
    r += string m.amount + m.unit + ' '
  end
  return r
endfunc
a = money 1 'k'
b = money 1000 ''
c = money 5 ''
d = money 2 'k'
l = list d a c b
console.writeline (a == b)
console.writeline (equal a b)
console.writeline (a > c)
console.writeline (c >= a)
console.writeline (less c a d)
array.sort l
console.writeline (show l)
array.sort l true
console.writeline (show l)
console.writeline (array.contains l (money 2000 ''))
console.writeline (array.indexof l (money 5000 ''))
console.writeline (array.count l (money 1 'k'))
console.writeline (show (array.distinct l))
console.writeline (show (sub l (money 1000 '')))
struct box
  param inner
endstruct
x = box (money 1 'k')
y = box (money 1000 '')
console.writeline (x == y)
console.writeline (equal x y)"),
        P("Struct_ConditionalDefinition" , @"
struct num
  param v
  param loud
  if loud
    func +
      param o
      console.writeline 'adding'
      return num (v + o.v) loud
    endfunc
  end
endstruct
a = num 1 true
b = num 2 false
console.writeline (string (a + b))
console.writeline (string (a + a))
console.writeline (b == (num 2 false))"),
    };

    //오류는 마지막 줄에서 나도록 (MatchesInterpreter가 마지막 줄의 오류 메시지를 비교)
    public static IEnumerable<object[]> Errors() => new[] {
        "x = 1 +" , "x = * 2" , "x = (1 ? 2)" , "x = 1 ? 2 :" , "x = 1|x +=" , "x = 1 << 1.5" , "x = 'a' >> 1" ,
        "x = ~1.5" , "x = -'a'" , "x = +true" , "x = 1 / 0" , "x = 5 % 0" , "x = (list 1) < 2" ,
        "return nothing" , "return nothing + 1" , "return 1 + -nothing" , "return (nothing)" , "x = nothing" , "y += 1" ,
        "struct p|param v|endstruct|x = (p 1) < (p 2)" ,
        "struct p|param v|func +|param o|return o.v|endfunc|endstruct|x = -(p 1)" ,
    }.Select((code , i) => new object[] { $"operator_error{i}" , code.Split('|') });

    [Theory]
    [MemberData(nameof(Programs))]
    [MemberData(nameof(Errors))]
    public void MatchesInterpreter(string name , string[] lines) => new NativeJsTests().MatchesInterpreter(name , lines);

    [Fact]
    public void OperatorFunctionErrors()
    {
        //구조체 밖이나 다시 정의할수 없는 연산자는 그 func 줄을 실행할때 오류
        var outside = JyunoSdk.NativeJs.NativeJsCompiler.Compile(new[] { "func +" , "endfunc" });
        Assert.Contains("$fail(\"연산자 '+'은/는 구조체(struct) 안에서만 다시 정의할수 있습니다.\")" , outside.Code);
        var logical = JyunoSdk.NativeJs.NativeJsCompiler.Compile(new[] { "struct p" , "  func &&" , "  endfunc" , "endstruct" });
        Assert.Contains("$fail(\"연산자 '&&'은/는 다시 정의할수 없습니다.\")" , logical.Code);
    }

    [Fact]
    public void OperatorsCompileToRuntimeFunctions()
    {
        //+, <는 내장 함수와 같은 빠른 경로, 구조체가 없는 프로그램의 ==는 equal과 같은 함수
        var result = JyunoSdk.NativeJs.NativeJsCompiler.Compile(new[] { "x = 1" , "while x < 10 && x != 5" , "  x += 1" , "end" , "console.writeline (x == 5)" });
        Assert.True(result.Success);
        string body = result.Code![result.Code!.IndexOf("return createProgram")..];
        Assert.Contains("less2(" , body);
        Assert.Contains("add2(" , body);
        Assert.Contains("equal2(" , body);
        Assert.Contains("opNe(" , body);
        Assert.DoesNotContain("JStruct" , body);
    }

    [Fact]
    public void ProgramsWithoutOperatorsAreUnchanged()
    {
        //연산자를 쓰지 않으면 연산자용 코드를 가져오지 않음
        var result = JyunoSdk.NativeJs.NativeJsCompiler.Compile(new[] { "x = add 1 2" , "console.writeline x" });
        Assert.DoesNotContain("opEq" , result.Code![result.Code!.IndexOf("return createProgram")..]);
    }
}
