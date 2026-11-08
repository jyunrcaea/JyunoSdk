using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JyunoSdk.LanguageServer;

/// <summary>
/// LSP 기본 프로토콜: "Content-Length: n\r\n\r\n" 헤더 다음에 JSON-RPC 메시지가 오는 스트림
/// </summary>
public sealed class JsonRpcConnection
{
    readonly Stream input;
    readonly Stream output;
    readonly object write_lock = new();

    public JsonRpcConnection(Stream input , Stream output)
    {
        this.input = input;
        this.output = output;
    }

    /// <summary>메시지 하나를 읽습니다. 스트림이 끝났다면 null</summary>
    public JsonObject? Read()
    {
        int length = -1;
        while (true)
        {
            string? header = ReadHeaderLine();
            if (header is null)
                return null;
            if (header.Length is 0)
                break;
            int colon = header.IndexOf(':');
            if (colon > 0 && header[..colon].Trim().Equals("Content-Length" , StringComparison.OrdinalIgnoreCase))
                length = int.Parse(header[(colon + 1)..].Trim());
        }
        if (length < 0)
            throw new InvalidDataException("Content-Length 헤더가 없습니다.");
        byte[] body = new byte[length];
        int read = 0;
        while (read < length)
        {
            int n = input.Read(body , read , length - read);
            if (n is 0)
                return null;
            read += n;
        }
        return JsonNode.Parse(body)?.AsObject();
    }

    string? ReadHeaderLine()
    {
        List<byte> bytes = new();
        while (true)
        {
            int b = input.ReadByte();
            if (b < 0)
                return bytes.Count is 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            if (b == '\n')
            {
                if (bytes.Count > 0 && bytes[^1] == '\r')
                    bytes.RemoveAt(bytes.Count - 1);
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
            bytes.Add((byte)b);
        }
    }

    public void Write(JsonObject message)
    {
        message["jsonrpc"] = "2.0";
        byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString(options));
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        lock (write_lock)
        {
            output.Write(header);
            output.Write(body);
            output.Flush();
        }
    }

    static readonly JsonSerializerOptions options = new() {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping ,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() ,
    };

    public void Respond(JsonNode? id , JsonNode? result) => Write(new JsonObject { ["id"] = id?.DeepClone() , ["result"] = result });

    public void RespondError(JsonNode? id , int code , string message) => Write(new JsonObject {
        ["id"] = id?.DeepClone() ,
        ["error"] = new JsonObject { ["code"] = code , ["message"] = message }
    });

    public void Notify(string method , JsonNode? parameters) => Write(new JsonObject { ["method"] = method , ["params"] = parameters });
}

public static class ErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int ServerNotInitialized = -32002;
    public const int RequestFailed = -32803;
}
