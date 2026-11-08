using System.Text.Json.Nodes;
using Jyuno.Compiler;
using Jyuno.Language;
using JyunoSdk.Analysis;

namespace JyunoSdk.LanguageServer;

/// <summary>
/// Jyuno 언어 서버 (Language Server Protocol 3.17)
/// 표준 입출력으로 실행하려면 <c>jyuno lsp</c>를 사용하세요.
/// </summary>
public sealed partial class JyunoLanguageServer
{
    public static string Version { get; } = typeof(JyunoLanguageServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    readonly JsonRpcConnection connection;
    readonly TextWriter? log;
    readonly Dictionary<string , TextDocument> documents = new();
    AnalyzerOptions options = AnalyzerOptions.Default;
    bool initialized;
    bool shutdown;
    bool snippet_support;
    bool hierarchical_symbols;
    bool markdown = true;

    public JyunoLanguageServer(Stream input , Stream output , TextWriter? log = null)
    {
        connection = new JsonRpcConnection(input , output);
        this.log = log;
    }

    /// <summary>현재 분석 설정</summary>
    public AnalyzerOptions Options => options;

    /// <summary>클라이언트가 연결을 끊거나 exit를 보낼 때까지 메시지를 처리합니다. 종료 코드를 반환합니다.</summary>
    public int Run()
    {
        while (true)
        {
            JsonObject? message;
            try
            {
                message = connection.Read();
            }
            catch (Exception e)
            {
                log?.WriteLine($"[jyuno-lsp] 메시지를 읽을수 없습니다: {e.Message}");
                connection.RespondError(null , ErrorCodes.ParseError , e.Message);
                continue;
            }
            if (message is null)
                return shutdown ? 0 : 1;
            string? method = message["method"]?.GetValue<string>();
            JsonNode? id = message["id"];
            JsonNode? parameters = message["params"];
            if (method is null)
                continue; //클라이언트의 응답 (서버가 보낸 요청이 없으므로 무시)
            if (method is "exit")
                return shutdown ? 0 : 1;
            try
            {
                if (id is null)
                    HandleNotification(method , parameters);
                else
                    HandleRequest(id , method , parameters);
            }
            catch (Exception e)
            {
                log?.WriteLine($"[jyuno-lsp] {method}: {e}");
                if (id is not null)
                    connection.RespondError(id , ErrorCodes.InternalError , e.Message);
            }
        }
    }

    void HandleRequest(JsonNode id , string method , JsonNode? p)
    {
        if (!initialized && method is not "initialize")
        {
            connection.RespondError(id , ErrorCodes.ServerNotInitialized , "initialize 요청을 먼저 보내야 합니다.");
            return;
        }
        if (shutdown && method is not "shutdown")
        {
            connection.RespondError(id , ErrorCodes.InvalidRequest , "서버가 종료되는 중입니다.");
            return;
        }
        JsonNode? result;
        switch (method)
        {
            case "initialize":
                result = Initialize(p);
                break;
            case "shutdown":
                shutdown = true;
                result = null;
                break;
            case "textDocument/completion":
                result = WithDocument(p , Completion);
                break;
            case "textDocument/hover":
                result = WithDocument(p , Hover);
                break;
            case "textDocument/signatureHelp":
                result = WithDocument(p , SignatureHelp);
                break;
            case "textDocument/definition":
                result = WithDocument(p , Definition);
                break;
            case "textDocument/references":
                result = WithDocument(p , References);
                break;
            case "textDocument/documentHighlight":
                result = WithDocument(p , DocumentHighlight);
                break;
            case "textDocument/documentSymbol":
                result = WithDocument(p , DocumentSymbols);
                break;
            case "textDocument/prepareRename":
                result = WithDocument(p , PrepareRename);
                break;
            case "textDocument/rename":
                if (Rename(p , out string? error) is JsonNode edit)
                    result = edit;
                else if (error is not null)
                {
                    connection.RespondError(id , ErrorCodes.RequestFailed , error);
                    return;
                }
                else
                    result = null;
                break;
            case "textDocument/foldingRange":
                result = WithDocument(p , FoldingRanges);
                break;
            case "textDocument/formatting":
                result = WithDocument(p , Formatting);
                break;
            case "textDocument/semanticTokens/full":
                result = WithDocument(p , SemanticTokens);
                break;
            case "textDocument/codeAction":
                result = WithDocument(p , CodeActions);
                break;
            default:
                connection.RespondError(id , ErrorCodes.MethodNotFound , $"지원하지 않는 요청입니다: {method}");
                return;
        }
        connection.Respond(id , result);
    }

    void HandleNotification(string method , JsonNode? p)
    {
        switch (method)
        {
            case "initialized":
                break;
            case "textDocument/didOpen":
            {
                var item = p!["textDocument"]!;
                string uri = item["uri"]!.GetValue<string>();
                documents[uri] = new TextDocument(uri , item["text"]!.GetValue<string>() , item["version"]?.GetValue<int>() ?? 0);
                Publish(uri);
                break;
            }
            case "textDocument/didChange":
            {
                string uri = p!["textDocument"]!["uri"]!.GetValue<string>();
                int version = p["textDocument"]!["version"]?.GetValue<int>() ?? 0;
                if (!documents.TryGetValue(uri , out var document))
                    break;
                foreach (var change in p["contentChanges"]!.AsArray())
                {
                    string text = change!["text"]!.GetValue<string>();
                    if (change["range"] is JsonNode range)
                    {
                        document.Apply(
                            range["start"]!["line"]!.GetValue<int>() , range["start"]!["character"]!.GetValue<int>() ,
                            range["end"]!["line"]!.GetValue<int>() , range["end"]!["character"]!.GetValue<int>() ,
                            text , version);
                    }
                    else
                        document.Replace(text , version);
                }
                Publish(uri);
                break;
            }
            case "textDocument/didClose":
            {
                string uri = p!["textDocument"]!["uri"]!.GetValue<string>();
                documents.Remove(uri);
                connection.Notify("textDocument/publishDiagnostics" , new JsonObject { ["uri"] = uri , ["diagnostics"] = new JsonArray() });
                break;
            }
            case "workspace/didChangeConfiguration":
            {
                if (p?["settings"] is JsonObject settings)
                {
                    options = ReadOptions(settings["jyuno"] as JsonObject ?? settings , options);
                    foreach (string uri in documents.Keys)
                        Publish(uri);
                }
                break;
            }
            default:
                //$/cancelRequest, $/setTrace, textDocument/didSave 등은 무시
                break;
        }
    }

    JsonNode? WithDocument(JsonNode? p , Func<JyunoDocument , JsonNode , JsonNode?> handler)
    {
        string uri = p!["textDocument"]!["uri"]!.GetValue<string>();
        if (!documents.TryGetValue(uri , out var document))
            return null;
        return handler(document.Analyze(options) , p);
    }

    JsonNode Initialize(JsonNode? p)
    {
        initialized = true;
        var capabilities = p?["capabilities"]?["textDocument"];
        snippet_support = capabilities?["completion"]?["completionItem"]?["snippetSupport"]?.GetValue<bool>() ?? false;
        hierarchical_symbols = capabilities?["documentSymbol"]?["hierarchicalDocumentSymbolSupport"]?.GetValue<bool>() ?? false;
        if (capabilities?["hover"]?["contentFormat"] is JsonArray formats)
            markdown = formats.Any(f => f?.GetValue<string>() is "markdown");
        if (p?["initializationOptions"] is JsonObject init)
            options = ReadOptions(init , options);
        return new JsonObject {
            ["capabilities"] = new JsonObject {
                ["positionEncoding"] = "utf-16" ,
                ["textDocumentSync"] = new JsonObject {
                    ["openClose"] = true ,
                    ["change"] = 2 , //Incremental
                } ,
                ["completionProvider"] = new JsonObject {
                    ["triggerCharacters"] = new JsonArray("." , "(") ,
                } ,
                ["hoverProvider"] = true ,
                ["signatureHelpProvider"] = new JsonObject {
                    ["triggerCharacters"] = new JsonArray(" " , "(") ,
                    ["retriggerCharacters"] = new JsonArray(" ") ,
                } ,
                ["definitionProvider"] = true ,
                ["referencesProvider"] = true ,
                ["documentHighlightProvider"] = true ,
                ["documentSymbolProvider"] = true ,
                ["renameProvider"] = new JsonObject { ["prepareProvider"] = true } ,
                ["foldingRangeProvider"] = true ,
                ["documentFormattingProvider"] = true ,
                ["codeActionProvider"] = new JsonObject { ["codeActionKinds"] = new JsonArray("quickfix") } ,
                ["semanticTokensProvider"] = new JsonObject {
                    ["legend"] = new JsonObject {
                        ["tokenTypes"] = new JsonArray(SemanticTokenTypes.Select(t => (JsonNode)t).ToArray()) ,
                        ["tokenModifiers"] = new JsonArray(SemanticTokenModifiers.Select(t => (JsonNode)t).ToArray()) ,
                    } ,
                    ["full"] = true ,
                } ,
            } ,
            ["serverInfo"] = new JsonObject {
                ["name"] = "jyuno-language-server" ,
                ["version"] = Version ,
            } ,
        };
    }

    /// <summary>
    /// 설정 읽기 (initializationOptions 또는 workspace/didChangeConfiguration의 jyuno 항목)
    /// <code>{ "commands": ["console", "math", "file"] | "default" | "all", "hostNames": ["myFunc"], "undefinedNames": true }</code>
    /// </summary>
    public static AnalyzerOptions ReadOptions(JsonObject settings , AnalyzerOptions current)
    {
        AnalyzerOptions result = current;
        switch (settings["commands"])
        {
            case JsonValue value when value.TryGetValue(out string? preset):
                result = result with { Commands = preset?.ToLowerInvariant() switch {
                    "all" => CommandGroups.All,
                    "essential" or "none" => CommandGroups.Essential,
                    _ => CommandGroups.Default
                } };
                break;
            case JsonArray groups:
                CommandGroups commands = CommandGroups.Essential;
                foreach (var group in groups)
                {
                    commands |= group?.GetValue<string>().ToLowerInvariant() switch {
                        "console" => CommandGroups.Console,
                        "math" => CommandGroups.Math,
                        "file" => CommandGroups.File,
                        "all" => CommandGroups.All,
                        "default" => CommandGroups.Default,
                        _ => CommandGroups.Essential
                    };
                }
                result = result with { Commands = commands };
                break;
        }
        if (settings["hostNames"] is JsonArray names)
            result = result with { HostNames = names.Select(n => n?.GetValue<string>()).OfType<string>().ToArray() };
        if (settings["undefinedNames"] is JsonValue undefined && undefined.TryGetValue(out bool report))
            result = result with { ReportUndefinedNames = report };
        return result;
    }

    void Publish(string uri)
    {
        if (!documents.TryGetValue(uri , out var document))
            return;
        var analysis = document.Analyze(options);
        JsonArray diagnostics = new();
        foreach (var diagnostic in analysis.Diagnostics)
        {
            JsonObject item = new() {
                ["range"] = Range(diagnostic.Line , diagnostic.Start , diagnostic.Line , diagnostic.End) ,
                ["severity"] = (int)diagnostic.Severity ,
                ["code"] = diagnostic.Code ,
                ["source"] = "jyuno" ,
                ["message"] = diagnostic.Message ,
            };
            if (diagnostic.Unnecessary)
                item["tags"] = new JsonArray(1);
            diagnostics.Add(item);
        }
        connection.Notify("textDocument/publishDiagnostics" , new JsonObject {
            ["uri"] = uri ,
            ["version"] = document.Version ,
            ["diagnostics"] = diagnostics ,
        });
    }

    static JsonObject Position(int line , int character) => new() { ["line"] = line , ["character"] = character };
    static JsonObject Range(int line , int start , int end_line , int end) => new() { ["start"] = Position(line , start) , ["end"] = Position(end_line , end) };
    static JsonObject Range(NameOccurrence name) => Range(name.Line , name.Start , name.Line , name.End);
    static JsonObject Range(int line , SourceToken token) => Range(line , token.Start , line , token.End);
    static (int Line, int Character) ReadPosition(JsonNode p) => (p["position"]!["line"]!.GetValue<int>() , p["position"]!["character"]!.GetValue<int>());

    JsonObject Location(string uri , NameOccurrence name) => new() { ["uri"] = uri , ["range"] = Range(name) };

    static string UriOf(JsonNode p) => p["textDocument"]!["uri"]!.GetValue<string>();

    JsonObject MarkupContent(string value) => new() {
        ["kind"] = markdown ? "markdown" : "plaintext" ,
        ["value"] = value
    };
}
