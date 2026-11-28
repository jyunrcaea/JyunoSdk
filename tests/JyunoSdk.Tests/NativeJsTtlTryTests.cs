using System.Diagnostics;
using JyunoSdk.NativeJs;

namespace JyunoSdk.Tests;

//시간 제한 블록(ttl)과 try/catch, throw(Jyuno 1.7.0)가 Native JS에서 인터프리터와 같은 결과를 내는지 비교 (0.7.0, node가 있을 때만)
//Jyuno 1.7.1 이상이 필요: dotnet test -p:JyunoSource=../Jyuno/Jyuno.csproj
[Collection("Console")]
public class NativeJsTtlTryTests
{
    static object[] P(string name , string code) => new object[] { name , Script.Lines(code) };

    public static IEnumerable<object[]> Programs() => new[] {
        P("c01_rethrow_line" , @"
func f
  x = div 1 0
endfunc
try
  f
catch e
  console.writeline e
  throw
tried"),
        P("c02_throw_e_newline" , @"
try
  x = div 1 0
catch e
  console.writeline e
  throw e
tried"),
        P("c03_uncaught_list" , @"
l = list 1 2 'a'
throw l"),
        P("c04_uncaught_struct" , @"
struct point
  param x
  param y
endstruct
throw (point 1 2)"),
        P("c05_depth_uncatchable" , @"
func r
  param n
  return r (n + 1)
endfunc
try
  r 0
catch e
  console.writeline 'caught?'
tried
console.writeline 'after'"),
        P("c06_catch_func_name" , @"
func e
  return 1
endfunc
try
  throw 5
catch e
  console.writeline e
tried"),
        P("c07_goto_into_try" , @"
try
  console.writeline 'a'
  inside:
  console.writeline 'b'
catch e
  console.writeline e
tried
try
  goto inside
catch e
  console.writeline ('caught: ' + e)
tried"),
        P("c08_goto_out_and_break" , @"
n = 0
while true
  try
    n += 1
    if n > 3
      break
    end
    throw n
  catch e
    console.writeline e
    if e == 2
      goto skip
    end
    console.writeline 'not skipped'
    skip:
  tried
end
console.writeline n
try
  goto 21
catch e
  console.writeline 'x'
tried
console.writeline 'after goto'"),
        P("c09_return_from_try" , @"
func f
  param n
  try
    if n > 0
      return 'try'
    end
    throw 'boom'
  catch e
    return 'catch ' + e
  tried
  return 'end'
endfunc
console.writeline (f 1)
console.writeline (f 0)"),
        P("c10_nested_rethrow_inner" , @"
try
  try
    x = list.pop (list)
  catch a
    console.writeline ('inner ' + a)
    try
      throw
    catch b
      console.writeline ('inner2 ' + b)
      throw 'new'
    tried
  tried
catch c
  console.writeline ('outer ' + c)
tried"),
        P("c11_catch_in_struct" , @"
struct box
  param v
  try
    w = div v 0
  catch err
    console.writeline err
  tried
  func show
    return 'box ' + (string v) + ' ' + err
  endfunc
endstruct
b = box 3
console.writeline (b.show)
console.writeline b"),
        P("c12_throw_two_args" , @"
func g
  console.writeline 'g called'
  return 1
endfunc
try
  throw (g) (g)
catch e
  console.writeline e
tried
throw 1 2"),
        P("c13_catch_in_for_scope" , @"
e = 'top'
for i (array 1 2)
  try
    throw i * 10
  catch e
    console.writeline e
  tried
  console.writeline e
end
console.writeline e"),
        P("c14_unknown_var_in_func" , @"
func f
  return nope + 1
endfunc
try
  f
catch e
  console.writeline e
tried
try
  q = (list 1 2) 5
catch e
  console.writeline e
tried
try
  s = add 'a' (list 1)
  t = sub 'a' 1
catch e
  console.writeline e
tried"),
        P("c15_error_in_catch_line" , @"
func f
  throw 'x'
endfunc
try
  try
    f
  catch e
    y = div 1 0
  tried
catch e2
  console.writeline e2
tried
y = div 2 0"),
        P("c16_ttl_basic" , @"
sum = 0
t = 5
ttl t
  sum += 1
endttl
console.writeline sum
x = 0
ttl 0
  x = 1
endttl
ttl -1
  x = 2
endttl
console.writeline x
ttl
endttl"),
        P("c17_ttl_bad" , @"
try
  ttl 'abc'
  endttl
catch e
  console.writeline e
tried
try
  ttl 1 2
  endttl
catch e
  console.writeline e
tried
try
  ttl null
    console.writeline 'no'
  endttl
catch e
  console.writeline e
tried
ttl (double 'NaN')
endttl"),
        P("c18_ttl_try_inside" , @"
n = 0
ttl 0.05
  try
    while true
      n += 1
    end
  catch e
    console.writeline 'caught ttl?'
  tried
endttl
console.writeline (n > 0)"),
        P("c19_ttl_error_inside" , @"
ttl 10
  console.writeline 'a'
  x = div 1 0
endttl"),
        P("c20_ttl_return" , @"
func f
  ttl 10
    return 5
  endttl
  return 0
endfunc
console.writeline (f)
ttl 10
  return 'top'
endttl"),
        P("c21_ttl_funcs" , @"
func spin
  param tag
  local count = 0
  while true
    count += 1
  end
endfunc
func depth
  param n
  if n > 0
    return depth (n - 1)
  end
  return spin 'x'
endfunc
func sum
  param n
  return n > 0 ? (sum (n - 1)) + n : 0
endfunc
count = 'outer'
ttl 0.05
  depth 20
endttl
console.writeline count
console.writeline (sum 190)"),
        P("c22_ttl_nested" , @"
ttl 0.05
  ttl 100
    while true
    end
  endttl
  console.writeline 'inner done'
endttl
console.writeline 'outer done'
ttl 100
  ttl 0.03
    while true
    end
  endttl
  console.writeline 'inner done 2'
endttl
console.writeline 'outer done 2'
func work
  ttl 100
    while true
    end
  endttl
  console.writeline 'work done'
endfunc
ttl 0.05
  work
  console.writeline 'after work'
endttl
console.writeline 'done'"),
        P("c23_ttl_for_scope" , @"
x = 'outer'
ttl 0.05
  for x (array 1 2 3)
    inner = 1
    while true
    end
  end
endttl
console.writeline x
sum = 0
for y (array 1 2 3)
  sum += y
end
console.writeline sum
k = 0
repeat 2
  ttl 0.02
    repeat 5
      k += 1
      if k == 2
        while true
        end
      end
    end
  endttl
end
console.writeline k"),
        P("c25_ttl_struct" , @"
struct forever
  param n
  while true
    n += 1
  end
endstruct
v = null
ttl 0.05
  v = forever 0
endttl
console.writeline v
struct pt
  param x
  func spin
    while true
      x += 1
    end
  endfunc
  func get
    return x
  endfunc
endstruct
p = pt 1
ttl 0.03
  p.spin
endttl
console.writeline (p.get > 1)"),
        P("c27_ttl_recursion" , @"
func f
  param n
  return n < 1 ? 0 : (f (n - 1)) + (f (n - 1))
endfunc
x = 'before'
ttl 0.05
  x = f 60
endttl
console.writeline x"),
        P("c28_ttl_in_func_calls_outside" , @"
func g
  param n
  return n * 2
endfunc
func f
  t = 0
  ttl 10
    repeat 1000
      t += g 1
    end
  endttl
  return t
endfunc
console.writeline (f)
console.writeline (g 21)"),
        P("c29_try_depth_restore" , @"
func r
  param n
  if n == 150
    throw 'deep'
  end
  return r (n + 1)
endfunc
repeat 3
  try
    r 0
  catch e
    console.writeline e
  tried
end
func r2
  param n
  return r2 (n + 1)
endfunc
r 0"),
        P("c30_catch_const" , @"
k = 5
try
  throw 1
catch k
  console.writeline k
tried
c = 1"),
        P("c31_ttl_depth_limit" , @"
func r
  param n
  return r (n + 1)
endfunc
ttl 100
  try
    r 0
  catch e
    console.writeline 'caught'
  tried
endttl"),
        P("c32_ttl_depth_ok" , @"
func r
  param n
  if n == 199
    return n
  end
  return r (n + 1)
endfunc
ttl 100
  console.writeline (r 0)
endttl
console.writeline (r 0)
func r2
  param n
  if n == 200
    return n
  end
  return r2 (n + 1)
endfunc
ttl 100
  console.writeline (r2 0)
endttl"),
        P("c33_ttl_closure_in_for" , @"
fs = list
for i (array 1 2 3)
  func show
    return i * 10
  endfunc
  if i == 1
    first = 0
  end
  console.writeline (show)
end
func outer
  local k = 5
  func inner
    return k + 1
  endfunc
  ttl 10
    k = 7
    console.writeline (inner)
  endttl
  return inner
endfunc
console.writeline (outer)"),
        P("c34_ttl_methods_ops" , @"
struct v
  param x
  func +
    param o
    return v (x + o.x)
  endfunc
  func ==
    param o
    return x == o.x
  endfunc
  func get
    return x
  endfunc
endstruct
a = v 1
b = v 2
ttl 10
  c = a + b
  console.writeline c.x
  console.writeline (c.get)
  console.writeline (a == b)
  l = list (v 3) (v 1) (v 2)
  console.writeline (list.contains l (v 1))
  console.writeline (equal a (v 1))
endttl
console.writeline c"),
        P("c35_ttl_expire_in_catch" , @"
n = 0
ttl 0.03
  try
    throw 'x'
  catch e
    while true
      n += 1
    end
  tried
endttl
console.writeline (n > 0)
try
  ttl 0.02
    while true
    end
  endttl
  console.writeline 'after ttl in try'
  throw 'later'
catch e
  console.writeline e
tried"),
        P("c36_ttl_goto_dispatch_fn" , @"
func spin
  i = 0
  top:
  i += 1
  goto top
endfunc
ttl 0.03
  spin
endttl
console.writeline 'left'
func count
  param n
  i = 0
  again:
  i += 1
  if i < n
    goto again
  end
  return i
endfunc
ttl 10
  console.writeline (count 1000)
endttl"),
        P("c37_ttl_nested_struct_ctor" , @"
struct node
  param v
  local kids = list
  if v > 0
    list.push kids (node (v - 1))
  end
  func depth
    if (length kids) == 0
      return 0
    end
    return 1 + ((kids 0).depth)
  endfunc
endstruct
ttl 10
  t = node 50
  console.writeline (t.depth)
endttl
console.writeline ((node 3).depth)"),
        P("c38_try_in_func_in_ttl_errors" , @"
func safe
  param a
  param b
  try
    return div a b
  catch e
    return e
  tried
endfunc
ttl 10
  console.writeline (safe 6 3)
  console.writeline (safe 1 0)
endttl
console.writeline (safe 1 0)"),
        P("ttl_stops_loop_and_calls" , @"
func spin
  while true
  end
endfunc
ttl 0.05
  spin
endttl
console.writeline 'after'
n = 0
ttl 0.05
  while true
    n += 1
  end
endttl
console.writeline (n > 0)
func fib
  param k
  if k < 2
    return k
  end
  return (fib (k - 1)) + (fib (k - 2))
endfunc
console.writeline (fib 10)
ttl 10
  console.writeline (fib 12)
endttl
console.writeline (fib 11)"),
        P("ttl_stops_goto_loop" , @"
i = 0
ttl 0.05
top:
  i += 1
  goto top
endttl
console.writeline (i > 0)"),
        P("throw_values" , @"
func check
  param v
  if v < 0
    throw (list 'negative' v)
  end
  return v
endfunc
for v (list 1 -2 3)
  try
    console.writeline (check v)
  catch e
    console.writeline e
  tried
end
try
  x = div 1 0
catch e
  console.writeline e
tried
throw 'stop'"),
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public void MatchesInterpreter(string name , string[] lines) => new NativeJsTests().MatchesInterpreter(name , lines);

    static string Body(params string[] lines)
    {
        var result = NativeJsCompiler.Compile(lines);
        Assert.True(result.Success , string.Join("\n" , result.Errors.Select(e => e.Message)));
        return result.Code![result.Code!.IndexOf("return createProgram")..];
    }

    [Fact]
    public void ProgramsWithoutTtlOrTryAreUnchanged()
    {
        //ttl과 try를 쓰지 않으면 시간 확인, 감시용 사본, 예외 처리 코드가 없음
        string body = Body("func f" , "  param x" , "  return x + 1" , "endfunc" , "i = 0" , "while i < 10" , "  i = f i" , "end" , "console.writeline i");
        foreach (string fragment in new[] { "$tick" , "$withWatched" , "$TT" , "$catchable" , "$thrown" , ".$w" })
            Assert.DoesNotContain(fragment , body);
        Assert.Contains("if ($depth >= 200) $tooDeep(); $depth++;" , body);
    }

    [Fact]
    public void TryOnlyProgramsHaveNoTimeChecks()
    {
        string body = Body("func f" , "  throw 1" , "endfunc" , "try" , "  f" , "catch e" , "  console.writeline e" , "tried");
        Assert.Contains("$catchable" , body);
        foreach (string fragment in new[] { "$tick" , "$withWatched" , "$TT" , ".$w" })
            Assert.DoesNotContain(fragment , body);
    }

    [Fact]
    public void TtlProgramsCallWatchedCopiesInsideTtl()
    {
        //원래 함수는 호출 깊이 검사 하나만 하고, ttl 안에서는 감시용 사본($w)을 직접 부름
        string body = Body("func f" , "  return 1" , "endfunc" , "x = f" , "ttl 1" , "  y = f" , "endttl");
        Assert.Contains("$withWatched(function $f_f(" , body);
        Assert.Contains("if ($depth >= 200 && $enter()) return $f_f.$w(" , body);
        Assert.Contains(".$w(" , body[body.IndexOf("$ttlOpen([")..]);
    }

    [Fact]
    public void RunApiReturnsThrownValue()
    {
        string? node = NativeJsTests.FindNode();
        if (node is null)
            return;
        var result = NativeJsCompiler.Compile(new[] { "x = 1" , "try" , "  throw 'inner'" , "catch e" , "  x = e" , "tried" , "throw (mul 6 7)" });
        Assert.True(result.Success);
        string file = Path.Combine(Path.GetTempPath() , $"jyuno-nativejs-thrown-{Guid.NewGuid():N}.js");
        File.WriteAllText(file , result.Code);
        try
        {
            string js = "const p = require(process.argv[1]); const r = p.run();" +
                "process.stdout.write(JSON.stringify([r.status, r.message, r.line, r.thrown]));";
            ProcessStartInfo info = new(node) { RedirectStandardOutput = true , RedirectStandardError = true };
            info.ArgumentList.Add("-e");
            info.ArgumentList.Add(js);
            info.ArgumentList.Add(file);
            using Process process = Process.Start(info)!;
            var stderr = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0 , stderr.Result);
            Assert.Equal("[\"error\",\"42\",6,42]" , stdout);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
