namespace JyunoSdk.Tests;

//구조체(Jyuno 1.5.0)가 Native JS에서 인터프리터와 같은 결과를 내는지 비교 (node가 있을 때만)
//Jyuno 1.5.0 이상이 필요: dotnet test -p:JyunoSource=../Jyuno/Jyuno.csproj
[Collection("Console")]
public class NativeJsStructTests
{
    static object[] P(string name , string code) => new object[] { name , Script.Lines(code) };

    public static IEnumerable<object[]> Programs() => new[] {
        P("Assigning_ExistingStruct_ReplacesMembers" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
a = person 'A' 1
b = person 'B' 2
a = b
a.show
b.grow 10
a.show
b.show"),
        P("Collection_HoldsCopies" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
me = person 'Jyunni' 1
people = list me (person 'Jyuno' 2)
me.grow 10
for p people
  p.show
end
first = people 0
first.show"),
        P("ConsoleWrite" , @"
struct person
  param name
  param rating
  func show
    console.write ""{1} rating is {2}"" name rating
  endfunc

endstruct
me = person ""Jyunni"" 2424
me.show"),
        P("Constructor_CanHaveCode" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct counter
  param start
  count = mul start 2
  func next
    count = add count 1
    return count
  endfunc
endstruct
c = counter 5
console.writeline (c.next)
console.writeline (c.next)
console.writeline c.count"),
        P("Constructor_Return_EndsConstructor" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct box
  param v
  if (equal v null)
    return 1
  end
  w = v
endstruct
a = box
console.writeline (string a)
b = box 3
console.writeline (string b)"),
        P("Copy_CopiesCollectionFields" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct bag
  items = list 1 2
endstruct
a = bag
b = a
list.push b.items 3
console.writeline (length a.items)
console.writeline (length b.items)"),
        P("Copy_IsDeep" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
a = person 'Jyunni' 2424
b = a
b.name = 'Jyuno'
b.grow 1
a.show
b.show"),
        P("Creating_OverwritesExistingMemberName" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
func me.show
  console.writeline 'old'
endfunc
me.show
me = person 'Jyunni' 2424
me.show"),
        P("Fields_AreDeclaredAsMemberNames" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
me = person 'Jyunni' 2424
console.writeline me.name
console.writeline me.rating"),
        P("Function_CanReturnStruct" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
func make
  param n
  p = person n 0
  p.grow 5
  return p
endfunc
me = make 'Jyunni'
me.show"),
        P("Function_Parameter_IsCopy" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
func bump
  param p
  p.grow 1
  p.show
endfunc
me = person 'Jyunni' 1
bump me
me.show"),
        P("MemberAssignment_ChangesField" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
me = person 'Jyunni' 2424
me.name = 'Jyuno'
me.show"),
        P("Method_CallsOtherMethod" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct acc
  total = 0
  func put
    param n
    total = add total n
  endfunc
  func twice
    param n
    put n
    put n
    return total
  endfunc
endstruct
a = acc
console.writeline (a.twice 5)
console.writeline a.total"),
        P("Method_ChangesField" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
me = person 'Jyunni' 2424
me.grow 100
console.writeline me.rating
me.show"),
        P("Method_ReadsFields" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
me = person 'Jyunni' 2424
me.show"),
        P("Method_SeesOuterVariables" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
bonus = 10
struct item
  param price
  func total
    return (add price bonus)
  endfunc
endstruct
i = item 5
bonus = 20
console.writeline (i.total)"),
        P("NestedStruct_DeclaresNestedMembers" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct point
  param x
  param y
endstruct
struct line
  param from
  param to
  func width
    return (sub to.x from.x)
  endfunc
endstruct
p = point 1 2
l = line p (point 4 6)
p.x = 100
console.writeline l.from.x
console.writeline (l.width)
l.to.x = 10
console.writeline (l.width)
l.from = (point 3 0)
console.writeline l.from.x
console.writeline (l.width)
copy = l
copy.to.x = 0
console.writeline l.to.x"),
        P("Reassigning_InLoop_UpdatesMembers" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
i = 0
while (less i 3)
  me = person 'P' i
  me.show
  i = add i 1
end"),
        P("StructInsideFunction_IsLocal" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
func f
  local p = person 'L' 0
  p.show
endfunc
p = person 'G' 1
f
p.show"),
        P("StructValue_ToStringAndEquality" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format '{0} rating is {1}' name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
a = person 'Jyunni' 2424
b = a
console.writeline (string a)
console.writeline (equal a b)
b.grow 1
console.writeline (equal a b)
return a"),
        P("call_during_ctor" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
v = ""outer""
struct e
  func show
    console.writeline v
  endfunc
  show
  v = ""inner""
  show
endstruct
x = e
x.show
console.writeline (string x)"),
        P("ctor_goto" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct g
  param n
  i = 0
  top:
  i = add i 1
  if (less i n)
    goto top
  end
endstruct
console.writeline (string (g 3))"),
        P("deep_return" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct p2
  param x
endstruct
func f
  local a = p2 1
  local b = a
  b.x = 2
  return (list a b)
endfunc
l = f
console.writeline (string (l 0))
console.writeline (string (l 1))"),
        P("dynamic_order" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct d
  param c
  if c
    b = 1
  end
  a = 2
  b = 3
endstruct
console.writeline (string (d true))
console.writeline (string (d false))
console.writeline (equal (d true) (d false))"),
        P("equal_misc" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct other
  param name
  param rating
endstruct
a = person ""x"" 1
b = other ""x"" 1
console.writeline (equal a b)
c = person ""x"" 1
console.writeline (equal a c)
console.writeline (equal a (person ""x"" 1.0))
console.writeline (list.contains (list a b) c)
console.writeline (string b)
if a
  console.writeline ""truthy""
end
console.writeline (string.format ""{0}"" a)"),
        P("explicit_cleared" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
me.name = ""explicit""
console.writeline me.name
me = person ""b"" 2
console.writeline me.name
me = 5
console.writeline me.name"),
        P("extra_member" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
me = person ""a"" 1
me.extra = 5
me = person ""b"" 2
console.writeline me.extra
me.show"),
        P("for_copy" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
people = list (person ""a"" 1) (person ""b"" 2)
for p people
  p.grow 10
  p.show
end
for p people
  p.show
end"),
        P("for_struct_fields" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct s
  for i (array 1 2)
    t = i
  end
  x = 1
endstruct
console.writeline (string s)"),
        P("hazard_list" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
me = person ""a"" 1
l = list me (me.grow 5)
(l 0).show"),
        P("hazard_list2" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
me = person ""a"" 1
l = list me (me.grow 5)
x = l 0
x.show
me.show"),
        P("local_shadow" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
me = person ""a"" 1
func f
  local me.name = ""local""
  console.writeline me.name
endfunc
f
console.writeline me.name"),
        P("lset" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
me = person ""a"" 1
lst = list null
lst 0 me
me.grow 5
x = lst 0
x.show
arr = array.new 2 me
me.grow 5
y = arr 1
y.show"),
        P("method_in_if" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct m
  param on
  if on
    func hi
      return ""hi""
    endfunc
  end
endstruct
a = m true
console.writeline (a.hi)
b = m false
console.writeline (string b)
console.writeline (b.hi)"),
        P("nested_method" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct point
  param x
  param y
  func move
    param d
    x = add x d
  endfunc
endstruct
struct seg
  param from
  param to
  func setx
    param v
    from.x = v
    to.move v
  endfunc
endstruct
s = seg (point 1 2) (point 3 4)
s.setx 7
console.writeline (string s)
t = s
t.from.move 100
s.to.move 1
console.writeline (string s)
console.writeline (string t)
return s"),
        P("param_entry" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
g = person ""g"" 1
func f
  g.grow 10
  param p
  p.show
endfunc
f g
g.show"),
        P("recursion" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct node
  param v
  param next
  func sum
    if (equal next null)
      return v
    end
    return (add v (next.sum))
  endfunc
endstruct
n = node 1 (node 2 (node 3 null))
console.writeline (n.sum)
console.writeline (string n)"),
        P("return_global" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
g = person ""g"" 1
func getg
  return g
endfunc
l = list (getg) (g.grow 1)
x = l 0
x.show
y = getg
g.grow 1
y.show"),
        P("shared_list" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
struct bag2
  param x
  param y
endstruct
a = list 1 2
s = bag2 a a
t = s
list.push t.x 9
console.writeline (length t.y)
console.writeline (length s.x)
list.push a 5
console.writeline (length s.y)"),
        P("struct_in_func" , @"
struct person
  param name
  param rating
  func show
    console.writeline (string.format ""{0} rating is {1}"" name rating)
  endfunc
  func grow
    param amount
    rating = add rating amount
  endfunc
endstruct
func make
  param n
  struct box
    param v
    func get
      return (add v n)
    endfunc
  endstruct
  return (box 1)
endfunc
a = make 10
b = make 20
console.writeline (a.get)
console.writeline (b.get)
console.writeline (equal (make 1) (make 2))
console.writeline (equal a b)"),
        P("collection_equality" , @"
struct pt
  param x
endstruct
a = pt 1
b = pt 1
console.writeline (length (list.distinct (list a b (pt 2) a)))
console.writeline (length (sub (list a b (pt 2)) (pt 1)))
console.writeline (list.remove (list a b) (pt 1))
console.writeline (list.join (list a b) "","")")
    };

    //오류는 마지막 줄에서 나도록 (MatchesInterpreter가 마지막 줄의 오류 메시지를 비교)
    public static IEnumerable<object[]> Errors() => new[] {
        "console.writeline me.nothing" , "x = me.show" , "me.show = 3" , "me = 5|me.show" ,
    }.Select((code , i) => new object[] { $"struct_error{i}" , new[] { "struct person" , "  param name" , "  func show" , "    return name" , "  endfunc" , "endstruct" , "me = person 'a'" }.Concat(code.Split('|')).ToArray() });

    [Theory]
    [MemberData(nameof(Programs))]
    [MemberData(nameof(Errors))]
    public void MatchesInterpreter(string name , string[] lines) => new NativeJsTests().MatchesInterpreter(name , lines);

    [Fact]
    public void RedefiningMemberNameIsRestricted()
    {
        //구조체 값의 멤버 이름을 func 등으로 다시 정의하면 실행할때 Native JS 제약 오류
        var result = JyunoSdk.NativeJs.NativeJsCompiler.Compile(new[] { "struct p" , "  func show" , "  endfunc" , "endstruct" , "me = p" , "func me.show" , "endfunc" });
        Assert.True(result.Success);
        Assert.Contains("$aliasDefine(\"me.show\")" , result.Code);
    }

    [Fact]
    public void ProgramsWithoutStructsAreUnchanged()
    {
        //구조체가 없으면 구조체용 코드를 만들지 않음
        var result = JyunoSdk.NativeJs.NativeJsCompiler.Compile(new[] { "a.b = 1" , "console.writeline a.b" });
        Assert.DoesNotContain("JStruct" , result.Code![result.Code!.IndexOf("return createProgram")..]);
    }
}
