using System.Diagnostics;
using System.Text.Json;
using JyunoSdk.NativeJs;

namespace JyunoSdk.Tests;

//JyunoSdk 0.6.0: Native JS의 메모리 한도 (run({ memoryLimit }), MemoryGuard) (node가 있을 때만)
[Collection("Console")]
public class NativeJsMemoryTests
{
    const long MB = 1024 * 1024;

    //변환한 코드를 run({ memoryLimit })으로 실행하고 결과 객체(JSON)를 돌려줌
    static JsonElement? Run(string[] lines , long? limit , bool guard = true)
    {
        string? node = NativeJsTests.FindNode();
        if (node is null)
            return null;
        var result = NativeJsCompiler.Compile(lines , new NativeJsOptions { MemoryGuard = guard });
        Assert.True(result.Success , string.Join("\n" , result.Errors.Select(e => e.Message)));
        string file = Path.Combine(Path.GetTempPath() , $"jyuno-nativejs-memory-{Guid.NewGuid():N}.js");
        File.WriteAllText(file , result.Code);
        try
        {
            string options = limit is null ? "{}" : $"{{ memoryLimit: {limit} }}";
            string js = "const p = require(process.argv[1]);" +
                $"const r = p.run({options});" +
                "process.stdout.write(JSON.stringify({ status: r.status, value: r.value, message: r.message }));";
            ProcessStartInfo info = new(node) { RedirectStandardOutput = true , RedirectStandardError = true };
            info.StandardOutputEncoding = info.StandardErrorEncoding = System.Text.Encoding.UTF8;
            //힙이 한도보다 훨씬 크므로, 감시가 없다면 node가 오래 돌거나 죽음
            info.ArgumentList.Add("--max-old-space-size=1024");
            info.ArgumentList.Add("-e");
            info.ArgumentList.Add(js);
            info.ArgumentList.Add(file);
            using Process process = Process.Start(info)!;
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                process.Kill(true);
                Assert.Fail("시간 안에 끝나지 않습니다.");
            }
            Assert.True(process.ExitCode == 0 , stderr.Result);
            return JsonDocument.Parse(stdout.Result).RootElement.Clone();
        }
        finally
        {
            File.Delete(file);
        }
    }

    static void AssertLimitError(JsonElement? result)
    {
        if (result is not JsonElement r)
            return;
        Assert.Equal("error" , r.GetProperty("status").GetString());
        Assert.Contains("메모리 한도" , r.GetProperty("message").GetString());
    }

    [Fact]
    public void GrowingList_StopsAtLimit()
    {
        AssertLimitError(Run(new[] { "keep = list" , "while true" , "list.push keep (mul 'x' 10000)" , "end" } , 32 * MB));
    }

    [Fact]
    public void GrowingListInFunction_StopsAtLimit()
    {
        AssertLimitError(Run(new[] { "func grow" , "param l" , "while true" , "list.push l (list 1 2 3 4 5 6 7 8)" , "end" , "endfunc" , "grow (list)" } , 32 * MB));
    }

    [Fact]
    public void ManyStructs_StopAtLimit()
    {
        AssertLimitError(Run(new[] { "struct box" , "param items" , "endstruct" , "l = list" , "while true" , "list.push l (box (list 1 2 3 4))" , "end" } , 16 * MB));
    }

    [Theory]
    [InlineData("x = array.new 100000000 0")]
    [InlineData("x = array.range 100000000")]
    [InlineData("x = mul 'ab' 100000000")]
    [InlineData("x = string.padleft '' 100000000")]
    public void LargeAllocation_IsRejectedBeforeAllocating(string line)
    {
        AssertLimitError(Run(new[] { line } , 16 * MB));
    }

    [Fact]
    public void SmallScript_RunsUnderLimit()
    {
        if (Run(new[] { "l = list" , "repeat 10000" , "list.push l 'abc'" , "end" , "s = mul 'a' 100000" , "return (add (length l) (length s))" } , 16 * MB) is not JsonElement r)
            return;
        Assert.Equal("ok" , r.GetProperty("status").GetString());
        Assert.Equal(110000 , r.GetProperty("value").GetInt64());
    }

    [Fact]
    public void ZeroLimit_DoesNotLimit()
    {
        if (Run(new[] { "x = array.new 10000000 0" , "return (length x)" } , 0) is not JsonElement r)
            return;
        Assert.Equal("ok" , r.GetProperty("status").GetString());
        Assert.Equal(10000000 , r.GetProperty("value").GetInt64());
    }

    [Fact]
    public void DefaultLimit_RejectsHugeArray()
    {
        //기본 한도는 256MB
        AssertLimitError(Run(new[] { "x = array.new 100000000 0" } , null));
    }

    [Fact]
    public void WithoutGuard_StillCompilesAndRuns()
    {
        var result = NativeJsCompiler.Compile(new[] { "x = 0" , "repeat 10" , "x = add x 1" , "end" } , new NativeJsOptions { MemoryGuard = false });
        Assert.True(result.Success);
        Assert.Contains("memoryGuard: false" , result.Code);
        Assert.DoesNotContain("$roots[" , result.Code);
        if (Run(new[] { "l = list" , "repeat 1000" , "list.push l 'abc'" , "end" , "return (length l)" } , 16 * MB , guard: false) is not JsonElement r)
            return;
        Assert.Equal("ok" , r.GetProperty("status").GetString());
        Assert.Equal(1000 , r.GetProperty("value").GetInt64());
        //감시가 없어도 미리 계산하는 큰 할당은 막음
        AssertLimitError(Run(new[] { "x = array.new 100000000 0" } , 16 * MB , guard: false));
    }

    [Fact]
    public void NegativeLimit_IsRejected()
    {
        string? node = NativeJsTests.FindNode();
        if (node is null)
            return;
        var result = NativeJsCompiler.Compile(new[] { "x = 1" });
        string file = Path.Combine(Path.GetTempPath() , $"jyuno-nativejs-memory-{Guid.NewGuid():N}.js");
        File.WriteAllText(file , result.Code);
        try
        {
            ProcessStartInfo info = new(node) { RedirectStandardOutput = true , RedirectStandardError = true };
            info.ArgumentList.Add("-e");
            info.ArgumentList.Add("const p = require(process.argv[1]); try { p.run({ memoryLimit: -1 }); process.stdout.write('no'); } catch (e) { process.stdout.write(e.name); }");
            info.ArgumentList.Add(file);
            using Process process = Process.Start(info)!;
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.Equal("RangeError" , stdout);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
