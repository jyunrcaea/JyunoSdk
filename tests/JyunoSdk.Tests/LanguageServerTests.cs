using System.Text;
using System.Text.Json.Nodes;
using JyunoSdk.LanguageServer;

namespace JyunoSdk.Tests;

public class LanguageServerTests
{
    const string Uri = "file:///test.jyuno";

    //요청들을 차례대로 보내고, 서버가 보낸 메시지들을 돌려줌
    static List<JsonObject> Exchange(params JsonObject[] messages)
    {
        MemoryStream input = new();
        foreach (var message in messages)
        {
            message["jsonrpc"] = "2.0";
            byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
            input.Write(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"));
            input.Write(body);
        }
        input.Position = 0;
        MemoryStream output = new();
        int code = new JyunoLanguageServer(input , output).Run();
        Assert.Equal(0 , code);
        output.Position = 0;
        var reader = new JsonRpcConnection(output , Stream.Null);
        List<JsonObject> result = new();
        while (reader.Read() is JsonObject message)
            result.Add(message);
        return result;
    }

    static int next_id = 100;
    static JsonObject Request(string method , JsonNode? parameters = null) => new() { ["id"] = next_id++ , ["method"] = method , ["params"] = parameters };
    static JsonObject Notification(string method , JsonNode? parameters = null) => new() { ["method"] = method , ["params"] = parameters };

    static JsonObject Initialize(JsonObject? options = null) => Request("initialize" , new JsonObject {
        ["processId"] = null ,
        ["capabilities"] = new JsonObject {
            ["textDocument"] = new JsonObject {
                ["completion"] = new JsonObject { ["completionItem"] = new JsonObject { ["snippetSupport"] = true } } ,
                ["documentSymbol"] = new JsonObject { ["hierarchicalDocumentSymbolSupport"] = true } ,
            }
        } ,
        ["initializationOptions"] = options ,
    });
    static JsonObject Open(string text) => Notification("textDocument/didOpen" , new JsonObject {
        ["textDocument"] = new JsonObject { ["uri"] = Uri , ["languageId"] = "jyuno" , ["version"] = 1 , ["text"] = text }
    });
    static JsonObject At(string method , int line , int character , JsonObject? extra = null)
    {
        JsonObject p = new() {
            ["textDocument"] = new JsonObject { ["uri"] = Uri } ,
            ["position"] = new JsonObject { ["line"] = line , ["character"] = character } ,
        };
        foreach (var (key , value) in extra ?? new JsonObject())
            p[key] = value?.DeepClone();
        return Request(method , p);
    }
    static JsonObject Doc(string method , JsonObject? extra = null)
    {
        JsonObject p = new() { ["textDocument"] = new JsonObject { ["uri"] = Uri } };
        foreach (var (key , value) in extra ?? new JsonObject())
            p[key] = value?.DeepClone();
        return Request(method , p);
    }
    static JsonObject[] Shutdown() => new[] { Request("shutdown") , Notification("exit") };

    //요청 하나의 결과
    static JsonNode? Ask(string text , JsonObject request , JsonObject? options = null)
    {
        var replies = Exchange(new[] { Initialize(options) , Notification("initialized") , Open(text) , request }.Concat(Shutdown()).ToArray());
        var reply = replies.Single(m => m["id"]?.GetValue<int>() == request["id"]!.GetValue<int>());
        Assert.True(reply["error"] is null , reply["error"]?.ToJsonString());
        return reply["result"];
    }

    const string Sample = "total = 0\nfunc add_score\n\tparam score\n\ttotal = add total score\nendfunc\nadd_score 10\nconsole.writeline total\n";

    [Fact]
    public void InitializeAdvertisesCapabilities()
    {
        var replies = Exchange(new[] { Initialize() }.Concat(Shutdown()).ToArray());
        var capabilities = replies[0]["result"]!["capabilities"]!;
        foreach (string capability in new[] { "completionProvider" , "hoverProvider" , "definitionProvider" , "referencesProvider" , "renameProvider" , "documentSymbolProvider" , "semanticTokensProvider" , "documentFormattingProvider" , "foldingRangeProvider" , "signatureHelpProvider" , "codeActionProvider" })
            Assert.NotNull(capabilities[capability]);
        Assert.Equal("jyuno-language-server" , replies[0]["result"]!["serverInfo"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void RequestsBeforeInitializeFail()
    {
        var request = Doc("textDocument/documentSymbol");
        var replies = Exchange(new[] { request , Initialize() }.Concat(Shutdown()).ToArray());
        Assert.Equal(ErrorCodes.ServerNotInitialized , replies[0]["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public void ExitWithoutShutdownReturnsOne()
    {
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"exit\"}";
        MemoryStream input = new(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n{body}"));
        Assert.Equal(1 , new JyunoLanguageServer(input , new MemoryStream()).Run());
    }

    [Fact]
    public void PublishesDiagnosticsOnOpenAndChange()
    {
        var replies = Exchange(new[] {
            Initialize() ,
            Open("if 1\nx = add 1 2") ,
            Notification("textDocument/didChange" , new JsonObject {
                ["textDocument"] = new JsonObject { ["uri"] = Uri , ["version"] = 2 } ,
                ["contentChanges"] = new JsonArray(new JsonObject {
                    ["range"] = new JsonObject {
                        ["start"] = new JsonObject { ["line"] = 1 , ["character"] = 12 } ,
                        ["end"] = new JsonObject { ["line"] = 1 , ["character"] = 12 } ,
                    } ,
                    ["text"] = "\nend"
                })
            }) ,
        }.Concat(Shutdown()).ToArray());
        var published = replies.Where(m => m["method"]?.GetValue<string>() == "textDocument/publishDiagnostics").ToList();
        Assert.Equal(2 , published.Count);
        var first = published[0]["params"]!["diagnostics"]!.AsArray();
        Assert.Contains(first , d => d!["code"]!.GetValue<string>() == "JY1002");
        Assert.Empty(published[1]["params"]!["diagnostics"]!.AsArray());
        Assert.Equal(2 , published[1]["params"]!["version"]!.GetValue<int>());
    }

    [Fact]
    public void Completion()
    {
        var result = Ask(Sample , At("textDocument/completion" , 6 , 0));
        var labels = result!["items"]!.AsArray().Select(i => i!["label"]!.GetValue<string>()).ToList();
        Assert.Contains("if" , labels);
        Assert.Contains("func … endfunc" , labels);
        Assert.Contains("add_score" , labels);
        Assert.Contains("total" , labels);
        Assert.Contains("console.writeline" , labels);
        Assert.DoesNotContain("file.read" , labels);
        Assert.DoesNotContain("param" , labels); //함수 밖
    }

    [Fact]
    public void CompletionReplacesDottedName()
    {
        var result = Ask("console.wr" , At("textDocument/completion" , 0 , 10));
        var item = result!["items"]!.AsArray().First(i => i!["label"]!.GetValue<string>() == "console.writeline")!;
        Assert.Equal(0 , item["textEdit"]!["range"]!["start"]!["character"]!.GetValue<int>());
    }

    [Fact]
    public void NoCompletionInsideStrings()
    {
        var result = Ask("x = 'hello wor'" , At("textDocument/completion" , 0 , 12));
        Assert.Empty(result!.AsArray());
    }

    [Fact]
    public void CompletionUsesHostNames()
    {
        var result = Ask("x = " , At("textDocument/completion" , 0 , 4) , new JsonObject { ["hostNames"] = new JsonArray("text.at") , ["commands"] = "all" });
        var labels = result!["items"]!.AsArray().Select(i => i!["label"]!.GetValue<string>()).ToList();
        Assert.Contains("text.at" , labels);
        Assert.Contains("file.read" , labels);
    }

    [Fact]
    public void Hover()
    {
        var builtin = Ask(Sample , At("textDocument/hover" , 3 , 11));
        Assert.Contains("add" , builtin!["contents"]!["value"]!.GetValue<string>());
        var function = Ask(Sample , At("textDocument/hover" , 5 , 3));
        Assert.Contains("func add_score (score)" , function!["contents"]!["value"]!.GetValue<string>());
        var keyword = Ask(Sample , At("textDocument/hover" , 2 , 2));
        Assert.Contains("param" , keyword!["contents"]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void SignatureHelp()
    {
        var result = Ask("x = math.pow 2 " , At("textDocument/signatureHelp" , 0 , 15));
        Assert.Equal(1 , result!["activeParameter"]!.GetValue<int>());
        Assert.StartsWith("math.pow" , result["signatures"]![0]!["label"]!.GetValue<string>());
        var user = Ask(Sample + "add_score " , At("textDocument/signatureHelp" , 7 , 10));
        Assert.Equal("func add_score (score)" , user!["signatures"]![0]!["label"]!.GetValue<string>());
    }

    [Fact]
    public void DefinitionAndReferences()
    {
        var definition = Ask(Sample , At("textDocument/definition" , 6 , 20))!.AsArray();
        Assert.Equal(new[] { 0 , 3 } , definition.Select(l => l!["range"]!["start"]!["line"]!.GetValue<int>()));
        var references = Ask(Sample , At("textDocument/references" , 0 , 1 , new JsonObject { ["context"] = new JsonObject { ["includeDeclaration"] = false } }))!.AsArray();
        Assert.Equal(new[] { 3 , 6 } , references.Select(l => l!["range"]!["start"]!["line"]!.GetValue<int>()));
    }

    [Fact]
    public void Rename()
    {
        var edit = Ask(Sample , At("textDocument/rename" , 2 , 8 , new JsonObject { ["newName"] = "points" }));
        var edits = edit!["changes"]![Uri]!.AsArray();
        Assert.Equal(2 , edits.Count);
        Assert.All(edits , e => Assert.Equal("points" , e!["newText"]!.GetValue<string>()));
    }

    [Fact]
    public void RenameRejectsBuiltinsAndBadNames()
    {
        var request = At("textDocument/rename" , 3 , 11 , new JsonObject { ["newName"] = "plus" });
        var replies = Exchange(new[] { Initialize() , Open(Sample) , request , At("textDocument/rename" , 0 , 1 , new JsonObject { ["newName"] = "two words" }) }.Concat(Shutdown()).ToArray());
        Assert.Equal(2 , replies.Count(r => r["error"] is not null));
    }

    [Fact]
    public void DocumentSymbols()
    {
        var symbols = Ask(Sample , Doc("textDocument/documentSymbol"))!.AsArray();
        Assert.Equal(new[] { "total" , "add_score" } , symbols.Select(s => s!["name"]!.GetValue<string>()));
        var function = symbols[1]!;
        Assert.Equal(4 , function["range"]!["end"]!["line"]!.GetValue<int>());
        Assert.Equal("score" , function["children"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void FoldingAndFormatting()
    {
        var folds = Ask("if 1\nx = 1\nelse\nx = 2\nend" , Doc("textDocument/foldingRange"))!.AsArray();
        Assert.Equal(2 , folds.Count);
        var edits = Ask("if 1\nx = 1\nend" , Doc("textDocument/formatting" , new JsonObject { ["options"] = new JsonObject { ["tabSize"] = 2 , ["insertSpaces"] = true } }))!.AsArray();
        Assert.Single(edits);
        Assert.Equal("  x = 1" , edits[0]!["newText"]!.GetValue<string>());
    }

    [Fact]
    public void SemanticTokens()
    {
        var data = Ask("func f\nendfunc\nf 'a' 1" , Doc("textDocument/semanticTokens/full"))!["data"]!.AsArray().Select(n => n!.GetValue<int>()).ToArray();
        Assert.Equal(0 , data.Length % 5);
        //func(keyword) f(function, declaration) endfunc(keyword) f(function) 'a'(string) 1(number)
        int[] types = data.Where((_ , i) => i % 5 == 3).ToArray();
        Assert.Equal(new[] { 0 , 1 , 0 , 1 , 4 , 5 } , types);
    }

    [Fact]
    public void QuickFixWrapsFunctionInParentheses()
    {
        string text = "func f\nreturn 1\nendfunc\nx = add f 1";
        var request = Doc("textDocument/codeAction" , new JsonObject {
            ["range"] = new JsonObject { ["start"] = new JsonObject { ["line"] = 3 , ["character"] = 8 } , ["end"] = new JsonObject { ["line"] = 3 , ["character"] = 9 } } ,
            ["context"] = new JsonObject {
                ["diagnostics"] = new JsonArray(new JsonObject {
                    ["range"] = new JsonObject { ["start"] = new JsonObject { ["line"] = 3 , ["character"] = 8 } , ["end"] = new JsonObject { ["line"] = 3 , ["character"] = 9 } } ,
                    ["code"] = "JY2002" ,
                    ["message"] = "..." ,
                })
            }
        });
        var actions = Ask(text , request)!.AsArray();
        var change = actions.Single()!["edit"]!["changes"]![Uri]![0]!;
        Assert.Equal("(f)" , change["newText"]!.GetValue<string>());
    }

    [Fact]
    public void UnknownMethod()
    {
        var request = Request("textDocument/unknownThing" , new JsonObject());
        var replies = Exchange(new[] { Initialize() , request }.Concat(Shutdown()).ToArray());
        Assert.Equal(ErrorCodes.MethodNotFound , replies.Single(r => r["id"]?.GetValue<int>() == request["id"]!.GetValue<int>())["error"]!["code"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("ab\ncd" , 1 , 1 , 4)]
    [InlineData("ab\r\ncd" , 1 , 0 , 4)]
    [InlineData("ab\rcd" , 1 , 5 , 5)]
    [InlineData("ab" , 3 , 0 , 2)]
    public void Offsets(string text , int line , int character , int offset) => Assert.Equal(offset , TextDocument.OffsetAt(text , line , character));
}
