namespace ModelContextProtocol.Server;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class McpServerToolTypeAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class McpServerToolAttribute : Attribute
{
    public McpServerToolAttribute(string? name = null, bool destructive = false)
    {
        Name = name;
        Destructive = destructive;
    }

    public string? Name { get; }

    public bool Destructive { get; }
}
