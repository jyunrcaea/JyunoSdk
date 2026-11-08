using Jyuno;
using JyunoSdk.Analysis;

namespace JyunoSdk.Tests;

public class BuiltinTests
{
    //Jyuno에 새 내장 명령어가 추가되면 SDK에도 설명을 추가해야 함
    [Fact]
    public void EveryJyunoBuiltinIsDocumented()
    {
        var undocumented = Builtins.All.Values.Where(b => b.Undocumented).Select(b => b.Name).ToList();
        Assert.Empty(undocumented);
    }

    [Fact]
    public void EveryDocumentedNameExistsInJyuno()
    {
        Runtime runtime = new(Runtime.AddJyunoCommandType.All);
        using var interpreter = runtime.Create();
        foreach (string name in Builtins.DocumentedNames)
        {
            if (name is "console.title" && !OperatingSystem.IsWindows())
                continue;
            //이름 = : 삭제에 성공하면 그 이름이 있었다는 뜻
            Assert.True(interpreter.ExecuteLine(name + " =") is true , name);
        }
    }

    [Fact]
    public void GroupsMatchRuntime()
    {
        Assert.Equal(CommandGroups.File , Builtins.All["file.read"].Group);
        Assert.Equal(CommandGroups.Math , Builtins.All["math.pi"].Group);
        Assert.Equal(CommandGroups.Console , Builtins.All["console.writeline"].Group);
        Assert.Equal(CommandGroups.Essential , Builtins.All["add"].Group);
        Assert.DoesNotContain(Builtins.In(CommandGroups.Default) , b => b.Name is "file.read");
        Assert.Contains(Builtins.In(CommandGroups.All) , b => b.Name is "file.read");
        Assert.Equal(BuiltinKind.Constant , Builtins.All["true"].Kind);
        Assert.True(Builtins.All["add"].IsFunction);
    }
}
