using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace PlaywrightMcpServer;

internal sealed class ToolRegistry
{
    private readonly List<ToolDefinition> _tools = new();
    private readonly Dictionary<string, ToolDefinition> _toolMap = new(StringComparer.Ordinal);
    private readonly NullabilityInfoContext _nullability = new();

    public ToolRegistry(CommandLineOptions options)
    {
        DiscoverTools();
    }

    public IReadOnlyList<object> ListTools()
    {
        return _tools.Select(tool => tool.ToJson()).ToArray();
    }

    public async Task<McpResponse> CallToolAsync(string name, JsonElement? arguments)
    {
        if (!_toolMap.TryGetValue(name, out var tool))
            return McpResponse.Error($"Tool \"{name}\" not found");

        try
        {
            return await tool.InvokeAsync(arguments, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return McpResponse.Error(ex.Message);
        }
    }

    private void DiscoverTools()
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
                continue;

            var isStatic = type.IsAbstract && type.IsSealed;
            var isSealed = type.IsSealed && !type.IsAbstract;
            if (!isStatic && !isSealed)
                throw new InvalidOperationException($"Tool type '{type.FullName}' must be a static or sealed class.");

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attribute = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attribute is null)
                    continue;

                if (!typeof(Task<string>).IsAssignableFrom(method.ReturnType))
                    throw new InvalidOperationException($"Tool method '{method.Name}' must return Task<string>.");

                var definition = CreateToolDefinition(type, method, attribute);
                if (_toolMap.ContainsKey(definition.Name))
                    throw new InvalidOperationException($"Duplicate tool name '{definition.Name}'.");

                _toolMap[definition.Name] = definition;
                _tools.Add(definition);
            }
        }
    }

    private ToolDefinition CreateToolDefinition(Type type, MethodInfo method, McpServerToolAttribute attribute)
    {
        var name = attribute.Name ?? BuildDefaultName(method.Name);
        var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name;
        var parameters = method.GetParameters();
        var parameterInfos = parameters.Select(p => _nullability.Create(p)).ToArray();
        var inputSchema = BuildInputSchema(parameters, parameterInfos);
        var title = description;

        return new ToolDefinition(
            name,
            description,
            title,
            attribute.Destructive,
            inputSchema,
            async (args, cancellationToken) => await InvokeAsync(method, parameters, parameterInfos, args, cancellationToken).ConfigureAwait(false));
    }

    private static string BuildDefaultName(string methodName)
    {
        if (methodName.EndsWith("Async", StringComparison.Ordinal))
            methodName = methodName[..^5];
        if (string.IsNullOrEmpty(methodName))
            throw new InvalidOperationException("Tool method name cannot be empty.");
        return char.ToLowerInvariant(methodName[0]) + methodName[1..];
    }

    private static object BuildInputSchema(ParameterInfo[] parameters, NullabilityInfo[] nullabilities)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter.ParameterType == typeof(CancellationToken))
                continue;

            var schema = new Dictionary<string, object>
            {
                ["type"] = MapSchemaType(parameter.ParameterType)
            };

            var description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;
            if (!string.IsNullOrWhiteSpace(description))
                schema["description"] = description;

            if (parameter.HasDefaultValue && parameter.DefaultValue is not null)
                schema["default"] = parameter.DefaultValue;

            var propertyName = parameter.Name ?? $"arg{i}";
            properties[propertyName] = schema;

            if (!IsOptional(parameter, nullabilities[i]))
                required.Add(propertyName);
        }

        return new
        {
            type = "object",
            properties,
            required = required.ToArray()
        };
    }

    private static string MapSchemaType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(bool))
            return "boolean";
        if (type == typeof(int) || type == typeof(long))
            return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return "number";
        return "string";
    }

    private static bool IsOptional(ParameterInfo parameter, NullabilityInfo nullability)
    {
        if (parameter.ParameterType == typeof(CancellationToken))
            return true;
        if (parameter.HasDefaultValue)
            return true;
        if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
            return true;
        return nullability.WriteState switch
        {
            NullabilityState.Nullable => true,
            NullabilityState.Unknown => false,
            _ => false
        };
    }

    private static async Task<McpResponse> InvokeAsync(MethodInfo method, ParameterInfo[] parameters, NullabilityInfo[] nullabilities, JsonElement? arguments, CancellationToken cancellationToken)
    {
        var values = new object?[parameters.Length];
        var argumentMap = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (arguments is { ValueKind: JsonValueKind.Object } obj)
        {
            foreach (var property in obj.EnumerateObject())
                argumentMap[property.Name] = property.Value;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter.ParameterType == typeof(CancellationToken))
            {
                values[i] = cancellationToken;
                continue;
            }

            var name = parameter.Name ?? $"arg{i}";
            if (!argumentMap.TryGetValue(name, out var element))
            {
                if (parameter.HasDefaultValue)
                {
                    values[i] = parameter.DefaultValue;
                    continue;
                }

                if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null || nullabilities[i].WriteState == NullabilityState.Nullable)
                {
                    values[i] = null;
                    continue;
                }

                return McpResponse.Error($"Missing required argument \"{name}\".");
            }

            values[i] = ConvertArgument(element, parameter.ParameterType, name);
        }

        if (method.Invoke(null, values) is not Task<string> task)
            throw new InvalidOperationException($"Tool method '{method.Name}' did not return Task<string>.");

        var resultJson = await task.ConfigureAwait(false);
        return McpResponse.FromJson(resultJson);
    }

    private static object? ConvertArgument(JsonElement element, Type targetType, string name)
    {
        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying != null)
        {
            if (element.ValueKind == JsonValueKind.Null)
                return null;
            targetType = underlying;
        }

        if (targetType == typeof(string))
        {
            if (element.ValueKind is JsonValueKind.Null)
                return null;
            return element.GetString();
        }

        if (targetType == typeof(int))
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var intValue))
                return intValue;
            if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out intValue))
                return intValue;
            throw new InvalidOperationException($"Argument \"{name}\" must be an integer.");
        }

        if (targetType == typeof(long))
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var longValue))
                return longValue;
            if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out longValue))
                return longValue;
            throw new InvalidOperationException($"Argument \"{name}\" must be an integer.");
        }

        if (targetType == typeof(double))
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var doubleValue))
                return doubleValue;
            if (element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(), out doubleValue))
                return doubleValue;
            throw new InvalidOperationException($"Argument \"{name}\" must be a number.");
        }

        if (targetType == typeof(float))
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var doubleValue))
                return (float)doubleValue;
            if (element.ValueKind == JsonValueKind.String && float.TryParse(element.GetString(), out var floatValue))
                return floatValue;
            throw new InvalidOperationException($"Argument \"{name}\" must be a number.");
        }

        if (targetType == typeof(bool))
        {
            if (element.ValueKind == JsonValueKind.True)
                return true;
            if (element.ValueKind == JsonValueKind.False)
                return false;
            if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var boolValue))
                return boolValue;
            throw new InvalidOperationException($"Argument \"{name}\" must be a boolean.");
        }

        if (targetType == typeof(JsonElement))
            return element;

        throw new InvalidOperationException($"Unsupported argument type '{targetType.FullName}' for \"{name}\".");
    }

    private sealed class ToolDefinition
    {
        private readonly Func<JsonElement?, CancellationToken, Task<McpResponse>> _handler;

        public ToolDefinition(string name, string description, string title, bool destructive, object inputSchema, Func<JsonElement?, CancellationToken, Task<McpResponse>> handler)
        {
            Name = name;
            Description = description;
            Title = title;
            Destructive = destructive;
            InputSchema = inputSchema;
            _handler = handler;
        }

        public string Name { get; }

        public string Description { get; }

        public string Title { get; }

        public bool Destructive { get; }

        public object InputSchema { get; }

        public Task<McpResponse> InvokeAsync(JsonElement? arguments, CancellationToken cancellationToken) => _handler(arguments, cancellationToken);

        public object ToJson()
        {
            return new
            {
                name = Name,
                description = Description,
                inputSchema = InputSchema,
                annotations = new
                {
                    title = Title,
                    readOnlyHint = !Destructive,
                    destructiveHint = Destructive,
                    openWorldHint = true
                }
            };
        }
    }
}
