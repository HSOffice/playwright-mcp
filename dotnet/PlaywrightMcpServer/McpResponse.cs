using System.Text.Json;

namespace PlaywrightMcpServer;

internal sealed class McpResponse
{
    private McpResponse(bool isError, object[] content)
    {
        IsError = isError;
        Content = content;
    }

    public bool IsError { get; }

    public object[] Content { get; }

    public object ToResult()
    {
        return new
        {
            content = Content,
            isError = IsError
        };
    }

    public static McpResponse FromJson(string json)
    {
        var element = JsonDocument.Parse(json).RootElement.Clone();
        return new McpResponse(false, new object[] { new { type = "json", json = element } });
    }

    public static McpResponse FromValue(object value)
    {
        var element = JsonSerializer.SerializeToElement(value);
        return new McpResponse(false, new object[] { new { type = "json", json = element } });
    }

    public static McpResponse Error(string message)
    {
        return new McpResponse(true, new object[] { new { type = "text", text = message } });
    }
}
