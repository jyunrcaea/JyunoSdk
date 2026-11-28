using System.Diagnostics;
using System.Text;
using Jyuno;
using JyunoSdk.NativeJs;

namespace JyunoSdk.Tests;

//파일, 디렉토리, 경로 명령어(Jyuno 1.7.1의 path.*, file.*, directory.*)가 Native JS에서 인터프리터와 같은 결과를 내는지 비교 (0.7.0, node가 있을 때만)
//같은 파일들을 만든 임시 디렉토리를 현재 디렉토리로 두고 인터프리터와 node로 한번씩 실행해서, 출력, 오류, 실행한 뒤의 파일들을 비교
//오류 메시지(.NET의 IOException 등)는 리눅스/macOS에서만 비교 (윈도우는 비슷하게 맞추지만 같다고 보장하지 않음)
[Collection("Console")]
public class NativeJsFileTests
{
    //각 항목: 식(결과를 출력), '!' 문장(오류만 출력), '#' 그대로 넣는 줄
    static string[] Cases(params string[] items)
    {
        List<string> lines = new() {
            "func s" , "  param v" , "  if (equal v null)" , "    return 'null'" , "  end" , "  return (add '<' v '>')" , "endfunc" ,
            "nul = string.fromcharcode 0"
        };
        int n = 0;
        foreach (string item in items)
        {
            if (item.StartsWith('#'))
            {
                lines.Add(item[1..]);
                continue;
            }
            n++;
            if (item.StartsWith('!'))
                lines.AddRange(new[] { "try" , "  " + item[1..] , "catch e" , $"  console.writeline (add '{n} E ' e)" , "tried" });
            else
                lines.AddRange(new[] { "try" , $"  console.writeline (add '{n} = ' (s ({item})))" , "catch e" , $"  console.writeline (add '{n} E ' e)" , "tried" });
        }
        return lines.ToArray();
    }

    static object[] P(string name , bool unix_only , params string[] items) => new object[] { name , unix_only , Cases(items) };

    public static IEnumerable<object[]> Programs() => new[] {
        //경로 계산 (파일 시스템을 쓰지 않음)
        P("paths" , false ,
            "path.combine 'a' 'b'" , "path.combine 'a' '' 'b' 'c'" , "path.join 'a' 'b' 'c'" , "path.join 'a' '' 'b'" ,
            "path.filename (path.combine 'a' 'b.txt')" , "path.filenamewithoutext 'x.tar.gz'" , "path.extension 'x.tar.gz'" , "path.extension 'x.'" , "path.extension '.bashrc'" ,
            "path.directory (path.combine 'a' 'b' 'c')" , "path.directory 'a'" , "path.directory ''" , "path.root 'a'" , "path.root ''" ,
            "path.hasext 'a.b'" , "path.hasext 'a.'" , "path.isrooted 'a'" , "path.isfullyqualified 'a'" , "path.endsinsep (add 'a' path.sep)" , "path.trimsep (add 'a' path.sep)" ,
            "path.changeext 'a.txt' 'md'" , "path.changeext 'a.txt' '.md'" , "path.changeext 'a.txt' ''" , "path.changeext 'a.txt' null" , "path.changeext 'a' 'md'" ,
            "equal (path.full 'a') (path.combine directory.now 'a')" , "equal (path.full (path.combine 'a' '..' 'b')) (path.combine directory.now 'b')" ,
            "path.relative (path.combine directory.now 'x' 'y')" , "path.relative (path.full 'x') (path.full 'x')" , "path.relative (path.full 'x') (path.full 'y')" , "path.relative (path.full 'x') (path.full (path.combine 'x' 'y'))" ,
            "equal (path.full 'z' directory.now) (path.full 'z')" ,
            "length path.sep" , "length path.altsep" , "length path.pathsep" , "length path.volsep" ,
            "path.endsinsep path.temp" , "path.isrooted path.temp" , "length (path.randomname)" , "string.indexof (path.randomname) '.'" ,
            "path.combine" , "path.join 'a' 1" , "path.filename" , "path.filename 3" , "path.filename null" , "path.changeext 'a'" ,
            "#t = path.tempfile" , "string.startswith (path.filename t) 'tmp'" , "path.extension t" , "file.exist t" , "file.size t" , "file.remove t"),
        //파일 읽고 쓰기 (성공하는 경우)
        P("files" , false ,
            "file.exist 'f.txt'" , "file.exist 'd'" , "file.exist ''" , "file.exist 'missing'" , "directory.exist 'd'" , "directory.exist 'f.txt'" ,
            "file.read 'f.txt'" , "file.read 'missing'" , "file.read 'd'" , "file.read 'bom8.txt'" , "file.read 'bom16.txt'" , "file.read 'bom16be.txt'" , "file.read 'bom32.txt'" , "file.read 'bad.txt'" , "file.read 'empty.txt'" ,
            "file.readlines 'f.txt'" , "file.readlines 'cr.txt'" , "file.readlines 'empty.txt'" , "length (file.readlines 'nl.txt')" , "file.readbytes 'bom16be.txt'" , "file.readbytes 'missing'" ,
            "file.size 'f.txt'" , "file.modifiedtime 'f.txt'" , "less 0 (file.createdtime 'f.txt')" , "file.isreadonly 'f.txt'" , "directory.modifiedtime (path.combine 'd' 'e')" ,
            "!file.write 'w1.txt' 'abc'" , "file.read 'w1.txt'" , "!file.write 'w1.txt' (list 1 2 'x')" , "file.read 'w1.txt'" , "!file.write 'w2.txt' (div 1.0 3)" ,
            "!file.append 'w3.txt' 'a'" , "!file.append 'w3.txt' 'b'" , "file.read 'w3.txt'" ,
            "!file.writelines 'l1.txt' 'a' 'b' 3" , "!file.appendlines 'l1.txt' (list 'c' null 'd')" , "file.readlines 'l1.txt'" , "!file.writelines 'l2.txt' (list 1 2) (list 3)" ,
            "!file.writebytes 'b1.bin' 0 255 65" , "!file.appendbytes 'b1.bin' (list 66 67)" , "file.readbytes 'b1.bin'" , "!file.writebytes 'b2.bin' (list)" ,
            "!file.write 'u.txt' 'héllo 😀'" , "file.readbytes 'u.txt'" , "file.read 'u.txt'" ,
            "file.remove 'w1.txt'" , "file.remove 'w1.txt'" , "file.remove 'd'" , "file.remove ''" , "!file.touch 't1.txt'" , "!file.touch 'f.txt'" ,
            "!file.setreadonly 'g.txt' true" , "file.isreadonly 'g.txt'" , "!file.setreadonly 'g.txt' false" , "file.isreadonly 'g.txt'" ,
            "file.copy 'cr.txt' 'c1.txt'" , "file.read 'c1.txt'" , "file.modifiedtime 'c1.txt'" , "file.copy 'bom8.txt' 'c1.txt' true" , "file.read 'c1.txt'" ,
            "file.move 'c1.txt' 'm1.txt'" , "file.exist 'c1.txt'" , "file.move 'm1.txt' 'm1.txt'" , "file.move 'm1.txt' 'w2.txt' true" , "file.read 'w2.txt'" ,
            "file.copy 'cr.txt' 'r1.txt'" , "file.copy 'bom16.txt' 'r2.txt'" , "file.replace 'r1.txt' 'r2.txt'" , "file.read 'r2.txt'" , "file.exist 'r1.txt'" ,
            "file.copy 'bad.txt' 'r3.txt'" , "file.replace 'r3.txt' 'r2.txt' 'bak.txt'" , "file.read 'r2.txt'" , "file.read 'bak.txt'" ,
            "file.write" , "file.read 3" , "file.read null" , "file.readlines" , "file.append 'w3.txt'" , "file.copy 'f.txt'" , "file.copy 'f.txt' 3" ,
            "!file.writebytes 'b3.bin' 256" , "!file.writebytes 'b3.bin' 1.5" , "!file.writebytes 'b3.bin' null" , "file.remove 3" , "file.exist null"),
        //디렉토리 (성공하는 경우)
        P("directories" , false ,
            "equal (path.trimsep directory.now) (path.full '.')" , "path.filename (directory.create 'n1')" , "directory.exist (path.combine 'n2' 'a')" , "path.filename (directory.create (path.combine 'n2' 'a' 'b'))" ,
            "directory.remove 'n1'" , "directory.remove 'n1'" , "directory.remove 'n2' true" , "directory.exist 'n2'" , "directory.remove 'f.txt'" ,
            "directory.files 'd'" , "directory.files 'd' '*.txt'" , "directory.files 'd' '*' true" , "directory.list 'd' '*' true" , "directory.entries 'd' '*' true" ,
            "directory.files 'd' '*.TXT'" , "directory.files '.' '?.txt'" , "directory.files '.' 'bom*'" , "directory.files '.' '*16*'" , "directory.files '.' 'b?m8.*'" , "directory.files '.' '*.t?t'" , "directory.files '.' '*.'" , "directory.files 'names' '*.*.*'" , "directory.files 'names' '???.txt'" ,
            "directory.files 'missing'" , "directory.files 'f.txt'" , "directory.files 'd' 3" , "length directory.files" ,
            "directory.isempty 'empty'" , "directory.isempty 'd'" , "path.filename (directory.parent (path.combine 'd' 'e'))" , "equal (directory.root 'x') (path.root directory.now)" ,
            "#directory.create (path.combine 'mv1' 'sub')" , "directory.move 'mv1' 'mv2'" , "directory.exist (path.combine 'mv2' 'sub')" , "directory.move 'x'" , "directory.move 1 'x'" ,
            "directory.copy 'd' 'cp1'" , "directory.entries 'cp1' '*' true" , "file.modifiedtime (path.combine 'cp1' 'g.txt')" , "directory.copy 'd' 'cp1' true" , "directory.copy 'd' (path.combine 'd' 'x')" , "directory.copy 'd'" ,
            "#here = directory.now" , "path.filename (directory.cd 'd')" , "directory.files" , "file.read 'g.txt'" , "equal (directory.cd '..') here" , "directory.cd 3"),
        //오류 메시지 (.NET의 Unix 구현과 같음)
        P("errors" , true ,
            "path.full ''" , "path.full (add 'a' nul)" , "path.full 'a' 'b'" , "path.full 'a' ''" , "path.full (add 'a' nul) '/b'" , "path.relative '' 'a'" , "path.relative 'a' ''" ,
            "path.full '/x/../y'" , "path.full '' '/b/c/../d'" , "path.relative '/a/b' '/c/d'" , "path.relative '/a/bc' '/a/b'" , "path.relative '/a' '/a/b/'" , "path.directory '//a//b//'" , "path.root '//a'" ,
            "file.exist 'f.txt/'" , "file.exist 'flink'" , "file.exist 'broken'" , "file.exist 'dlink'" , "file.exist (add 'f' nul)" , "directory.exist 'dlink'" ,
            "file.read 'flink'" , "file.read 'broken'" , "file.size 'flink'" , "file.size 'd'" , "file.size 'missing'" , "file.size ''" , "file.size 'broken'" ,
            "file.isreadonly 'missing'" , "file.modifiedtime 'missing'" , "directory.modifiedtime 'f.txt'" , "file.isreadonly 'm464'" , "file.isreadonly 'm066'" , "file.isreadonly 'm444'" , "!file.setreadonly 'm444' false" , "!file.setreadonly 'm4755' true" ,
            "!file.write 'nodir/x' 'a'" , "!file.write 'd' 'a'" , "!file.write '' 'a'" , "!file.write (add 'a' nul) 'x'" , "!file.write 'f.txt/x' 'a'" , "!file.append 'd' 'b'" , "!file.appendbytes 'nodir/x' 1" , "!file.touch 'nodir/t'" , "!file.touch 'd'" ,
            "file.copy 'f.txt' 'c1.txt'" , "file.copy 'f.txt' 'c1.txt'" , "file.copy 'missing' 'c2.txt'" , "file.copy 'nodir/x' 'c2.txt'" , "file.copy 'f.txt' 'd'" , "file.copy 'f.txt' 'd' true" , "file.copy 'd' 'c3'" , "file.copy 'f.txt' 'nodir/c'" , "file.copy '' 'x'" , "file.copy 'f.txt' ''" , "file.copy 'f.txt' 'f.txt'" , "file.copy 'f.txt' 'f.txt' true" , "file.copy 'broken' 'c4'" , "file.copy 'flink' 'c5.txt'" ,
            "file.move 'missing' 'x'" , "file.move 'd' 'x'" , "file.move 'c1.txt' 'f.txt'" , "file.move 'c1.txt' 'd'" , "file.move 'c1.txt' 'd' true" , "file.move 'c1.txt' 'nodir/x'" , "file.move 'c1.txt' 'nodir/x' true" , "file.move '' 'x'" ,
            "file.replace 'missing' 'f.txt'" , "file.replace 'f.txt' 'missing'" , "file.replace 'd' 'f.txt'" , "file.replace 'f.txt' 'd'" , "file.replace 'f.txt' 'f.txt'" , "file.replace '' 'f.txt'" , "file.replace 'c1.txt' 'f.txt' 'nodir/b'" ,
            "file.remove 'flink'" , "file.remove 'broken'" ,
            "directory.create 'f.txt'" , "directory.create 'f.txt/x'" , "directory.create ''" , "directory.create 'broken/x'" , "#directory.create 'n2/a'" , "directory.remove 'n2'" , "directory.remove 'dlink'" , "directory.exist 'd'" ,
            "directory.files 'd' '/x'" , "directory.files 'd' 'e/*'" , "directory.files 'd' 'missing/*'" , "directory.files 'd' '../*.txt'" , "directory.files 'd' 'e/'" , "directory.files 'd' './*'" , "directory.files 'names' '*\\\\*'" , "directory.files 'names' 'q\\\"*'" , "directory.files 'names' '<*'" , "directory.files 'names' '*.'" ,
            "length (directory.entries 'wl' '*' true)" , "directory.copy 'wl' 'wl2'" , "directory.entries 'wl2'" , "directory.copy 'wl' 'wl2' true" ,
            "directory.isempty 'missing'" , "directory.parent '/'" , "directory.parent ''" , "directory.root ''" ,
            "directory.move 'n9' 'x'" , "#directory.create 'mv1/sub'" , "directory.move 'mv1' 'd'" , "directory.move 'mv1' 'mv1'" , "directory.move 'mv1' 'mv1/sub/z'" , "directory.move 'mv1' 'nodir/x'" , "directory.move '' 'x'" , "directory.move 'f.txt/' 'zz'" , "directory.move 'missing' 'd'" ,
            "directory.copy 'missing' 'x'" , "directory.copy 'd' ''" , "directory.copy 'd' 'f.txt'" , "directory.cd 'missing'" , "directory.cd 'f.txt'" , "directory.cd ''"),
    };

    static void SetUp(string root)
    {
        void write(string path , string text) => File.WriteAllText(Path.Combine(root , path) , text);
        void bytes(string path , params int[] data) => File.WriteAllBytes(Path.Combine(root , path) , data.Select(b => (byte)b).ToArray());
        Directory.CreateDirectory(Path.Combine(root , "d" , "e"));
        Directory.CreateDirectory(Path.Combine(root , "d" , "E2"));
        Directory.CreateDirectory(Path.Combine(root , "empty"));
        Directory.CreateDirectory(Path.Combine(root , "names"));
        write("f.txt" , "hello\nworld\n");
        write("g.txt" , "g");
        write(Path.Combine("d" , "g.txt") , "g");
        write(Path.Combine("d" , "e" , "h.md") , "h");
        write(Path.Combine("d" , ".hidden") , "");
        write("empty.txt" , "");
        write("cr.txt" , "a\rb\r\nc\n\nd\r");
        bytes("nl.txt" , 0x0A);
        bytes("bom8.txt" , 0xEF , 0xBB , 0xBF , 0x41 , 0x0D , 0x0A , 0x42);
        bytes("bom16.txt" , 0xFF , 0xFE , 0x41 , 0x00 , 0x0D , 0x00 , 0x42 , 0x00);
        bytes("bom16be.txt" , 0xFE , 0xFF , 0x00 , 0x41 , 0x00 , 0x0A , 0xD8 , 0x3D , 0xDE , 0x00);
        bytes("bom32.txt" , 0xFF , 0xFE , 0 , 0 , 0x41 , 0 , 0 , 0 , 0x00 , 0xF6 , 0x01 , 0x00 , 0x0A , 0 , 0 , 0);
        bytes("bad.txt" , 0x41 , 0xC3 , 0x28 , 0xE2 , 0x82 , 0x0A , 0xF0 , 0x9F , 0x98);
        foreach (string name in new[] { "sp ace.txt" , "한글.txt" , "[br].txt" , "x.TXT" , "noext" , "two.dots.tar.gz" , ".dotfile" })
            write(Path.Combine("names" , name) , name);
        if (!OperatingSystem.IsWindows())
        {
            foreach (string name in new[] { "a\\b.txt" , "q\"x.txt" , "<y>.md" , "trail." })
                write(Path.Combine("names" , name) , name);
            File.CreateSymbolicLink(Path.Combine(root , "flink") , "f.txt");
            File.CreateSymbolicLink(Path.Combine(root , "broken") , "nothing");
            Directory.CreateSymbolicLink(Path.Combine(root , "dlink") , "d");
            Directory.CreateDirectory(Path.Combine(root , "wl" , "inner"));
            write(Path.Combine("wl" , "inner" , "i.txt") , "i");
            Directory.CreateSymbolicLink(Path.Combine(root , "wl" , "up") , "../d");
            Directory.CreateSymbolicLink(Path.Combine(root , "wl" , "self") , ".");
            foreach (string mode in new[] { "464" , "066" , "444" , "4755" })
            {
                write("m" + mode , "");
                File.SetUnixFileMode(Path.Combine(root , "m" + mode) , (UnixFileMode)Convert.ToInt32(mode , 8));
            }
        }
        //수정 시간은 정해진 값으로 (만든 시간이 달라도 같은 결과)
        DateTime time = new(2021 , 3 , 4 , 5 , 6 , 7 , 891 , DateTimeKind.Utc);
        foreach (string entry in Directory.EnumerateFileSystemEntries(root , "*" , SearchOption.AllDirectories).Where(e => new FileInfo(e).LinkTarget is null).Order(StringComparer.Ordinal))
        {
            //윈도우에서는 디렉토리에 File.SetLastWriteTimeUtc를 쓸수 없음 (접근 거부)
            if (Directory.Exists(entry))
                Directory.SetLastWriteTimeUtc(entry , time);
            else
                File.SetLastWriteTimeUtc(entry , time);
            time = time.AddMinutes(1);
        }
    }

    //실행한 뒤의 파일들: 경로, 크기, 내용 (수정 시간은 처음 만든 값 그대로인 것만)
    static string Snapshot(string root)
    {
        StringBuilder sb = new();
        foreach (string entry in Directory.EnumerateFileSystemEntries(root , "*" , new EnumerationOptions { RecurseSubdirectories = true , AttributesToSkip = 0 }).Order(StringComparer.Ordinal))
        {
            FileInfo info = new(entry);
            sb.Append(entry[root.Length..]);
            if (info.LinkTarget is string link)
                sb.Append(" -> ").Append(link);
            else if (Directory.Exists(entry))
                sb.Append('/');
            else
            {
                DateTime modified = File.GetLastWriteTimeUtc(entry);
                sb.Append(' ').Append(info.Length).Append(' ').Append(modified.Year < 2025 ? new DateTimeOffset(modified).ToUnixTimeMilliseconds().ToString() : "new");
                if (!OperatingSystem.IsWindows())
                    sb.Append(' ').Append(Convert.ToString((int)File.GetUnixFileMode(entry) , 8));
                else
                    sb.Append(' ').Append(info.IsReadOnly ? "ro" : "rw");
                try { sb.Append(' ').Append(Convert.ToHexString(File.ReadAllBytes(entry))); } catch (UnauthorizedAccessException) { sb.Append(" (읽을수 없음)"); }
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    static void Remove(string root)
    {
        if (!Directory.Exists(root))
            return;
        foreach (string file in Directory.EnumerateFiles(root , "*" , new EnumerationOptions { RecurseSubdirectories = true , AttributesToSkip = 0 }))
            if (new FileInfo(file).LinkTarget is null)
                File.SetAttributes(file , FileAttributes.Normal);
        Directory.Delete(root , true);
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void MatchesInterpreter(string name , bool unix_only , string[] lines)
    {
        string? node = NativeJsTests.FindNode();
        if (node is null || (unix_only && OperatingSystem.IsWindows()))
            return;
        string root = Path.Combine(Path.GetTempPath() , $"jyuno-nativejs-files-{name}-{Guid.NewGuid():N}");
        string script = root + ".js";
        string original_directory = Directory.GetCurrentDirectory();
        try
        {
            //인터프리터
            Directory.CreateDirectory(root);
            SetUp(root);
            StringWriter expected = new();
            string? expected_error = null;
            TextWriter original = Console.Out;
            Console.SetOut(expected);
            Directory.SetCurrentDirectory(root);
            try
            {
                using Interpreter interpreter = new Runtime(Runtime.AddJyunoCommandType.All).Create(lines);
                interpreter.Run();
            }
            catch (JyunoException e)
            {
                expected_error = $"{name}.jyuno:{e.Line + 1}: 오류: {e.Message}";
            }
            finally
            {
                Console.SetOut(original);
                Directory.SetCurrentDirectory(original_directory);
            }
            string expected_files = Snapshot(root);
            Remove(root);

            //Native JS (같은 파일들을 다시 만들어서)
            Directory.CreateDirectory(root);
            SetUp(root);
            var result = NativeJsCompiler.Compile(lines , new NativeJsOptions { Globals = new Runtime(Runtime.AddJyunoCommandType.All).GetGlobals() , Name = name + ".jyuno" });
            Assert.True(result.Success , string.Join("\n" , result.Errors.Select(e => e.Message)));
            File.WriteAllText(script , result.Code);
            ProcessStartInfo info = new(node) { RedirectStandardOutput = true , RedirectStandardError = true , WorkingDirectory = root };
            info.ArgumentList.Add(script);
            info.StandardOutputEncoding = info.StandardErrorEncoding = Encoding.UTF8;
            using Process process = Process.Start(info)!;
            var stderr = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            Assert.Equal(expected.ToString().Replace("\r\n" , "\n") , stdout.Replace("\r\n" , "\n"));
            if (expected_error is null)
                Assert.True(process.ExitCode == 0 , stderr.Result);
            else
                Assert.Contains(expected_error , stderr.Result);
            Assert.Equal(expected_files , Snapshot(root));
        }
        finally
        {
            Directory.SetCurrentDirectory(original_directory);
            Remove(root);
            File.Delete(script);
        }
    }

    static string Body(NativeJsResult result)
    {
        Assert.True(result.Success , string.Join("\n" , result.Errors.Select(e => e.Message)));
        return result.Code!;
    }

    [Fact]
    public void FileRuntimeIsIncludedOnlyWhenUsed()
    {
        var all = new Runtime(Runtime.AddJyunoCommandType.All).GetGlobals();
        //파일 명령어를 쓰지 않으면 파일 런타임이 없음 (전과 같은 코드)
        string plain = Body(NativeJsCompiler.Compile(new[] { "console.writeline (add 1 2)" } , new NativeJsOptions { Globals = all }));
        Assert.DoesNotContain("function addFiles" , plain);
        Assert.DoesNotContain("files: true" , plain);
        string uses = Body(NativeJsCompiler.Compile(new[] { "console.writeline (file.exist 'a')" } , new NativeJsOptions { Globals = all }));
        Assert.Contains("function addFiles" , uses);
        Assert.Contains("files: true" , uses);
        //기본 전역 이름(파일 명령어 없음)으로 변환하면 없는 명령어
        string missing = Body(NativeJsCompiler.Compile(new[] { "console.writeline (file.exist 'a')" }));
        Assert.DoesNotContain("function addFiles" , missing);
    }

    [Fact]
    public void PathSeparatorsAreReadAtRunTime()
    {
        //path.sep 등은 변환한 머신의 값을 넣지 않고 실행하는 운영체제의 값을 읽음
        string code = Body(NativeJsCompiler.Compile(new[] { "console.writeline path.sep" , "x = path.combine 'a' path.sep" } , new NativeJsOptions { Globals = new Runtime(Runtime.AddJyunoCommandType.All).GetGlobals() }));
        Assert.Contains("$H.holder(\"path.sep\")" , code);
    }

    [Fact]
    public void RunOptionsChooseFileSystem()
    {
        string? node = NativeJsTests.FindNode();
        if (node is null)
            return;
        string[] lines = {
            "console.writeline (path.full 'a')" , "console.writeline (path.combine 'x' 'y')" , "console.writeline path.sep" , "console.writeline (path.relative 'C:\\\\work\\\\b' 'c:\\\\WORK\\\\c')" ,
            "console.writeline (file.exist 'a')" , "file.write 'a' 'b'"
        };
        var result = NativeJsCompiler.Compile(lines , new NativeJsOptions { Globals = new Runtime(Runtime.AddJyunoCommandType.All).GetGlobals() });
        Assert.True(result.Success);
        string file = Path.Combine(Path.GetTempPath() , $"jyuno-nativejs-files-options-{Guid.NewGuid():N}.js");
        File.WriteAllText(file , result.Code);
        try
        {
            //파일 시스템이 없는 환경 (경로 계산은 되고, 파일 작업은 오류), 윈도우 경로, 파일 명령어를 끈 경우
            string js = "const p = require(process.argv[1]);" +
                "const a = p.run({ files: { fs: null, platform: 'win32', cwd: () => 'C:\\\\work' } });" +
                "const b = p.run({ files: false });" +
                "process.stdout.write(JSON.stringify([a.output, a.message, a.line, b.message]));";
            ProcessStartInfo info = new(node) { RedirectStandardOutput = true , RedirectStandardError = true };
            info.StandardOutputEncoding = info.StandardErrorEncoding = Encoding.UTF8;
            info.ArgumentList.Add("-e");
            info.ArgumentList.Add(js);
            info.ArgumentList.Add(file);
            using Process process = Process.Start(info)!;
            var stderr = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0 , stderr.Result);
            Assert.Equal("[\"C:\\\\work\\\\a\\nx\\\\y\\n\\\\\\n..\\\\c\\nFalse\\n\",\"파일 작업을 하지 못했습니다: 이 환경에서는 파일 시스템을 쓸수 없습니다. (run의 files.fs)\",5,\"'path.full'은/는 존재하지 않는 함수/변수/명령어 입니다.\"]" , stdout);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
