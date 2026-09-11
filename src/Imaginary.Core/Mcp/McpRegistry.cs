using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Imaginary.Core.Mcp;

public class McpRegistry
{
    private readonly ConcurrentDictionary<string, RegisteredTool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RegisteredResource> _resources = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RegisteredPrompt> _prompts = new(StringComparer.OrdinalIgnoreCase);

    private class RegisteredTool
    {
        public required McpToolDefinition Definition { get; init; }
        public required MethodInfo Method { get; init; }
        public required object Target { get; init; }
        public required ParameterInfo[] Parameters { get; init; }
    }

    private class RegisteredResource
    {
        public required McpResourceDefinition Definition { get; init; }
        public required Func<string, Task<string>> Reader { get; init; }
    }

    private class RegisteredPrompt
    {
        public required McpPromptDefinition Definition { get; init; }
        public required Func<Dictionary<string, string>, Task<List<McpPromptMessage>>> Generator { get; init; }
    }

    /// <summary>
    /// Registriert alle mit [McpTool] dekorierten Methoden eines Objekts vollautomatisch.
    /// </summary>
    public void RegisterTools(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var type = target.GetType();

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = method.GetCustomAttribute<McpToolAttribute>();
            if (attr == null) continue;

            var parameters = method.GetParameters();
            var inputSchema = BuildInputSchema(parameters);

            var definition = new McpToolDefinition
            {
                Name = attr.Name,
                Description = attr.Description,
                InputSchema = inputSchema
            };

            _tools[attr.Name] = new RegisteredTool
            {
                Definition = definition,
                Method = method,
                Target = target,
                Parameters = parameters
            };
        }
    }

    /// <summary>
    /// Registriert eine Resource für das LLM (z. B. imaginary://system/status).
    /// </summary>
    public void RegisterResource(string uri, string name, string description, string mimeType, Func<string, Task<string>> reader)
    {
        _resources[uri] = new RegisteredResource
        {
            Definition = new McpResourceDefinition
            {
                Uri = uri,
                Name = name,
                Description = description,
                MimeType = mimeType
            },
            Reader = reader
        };
    }

    /// <summary>
    /// Registriert einen vordefinierten Prompt / Workflow für KI-Agenten.
    /// </summary>
    public void RegisterPrompt(string name, string description, List<McpPromptArgument> args, Func<Dictionary<string, string>, Task<List<McpPromptMessage>>> generator)
    {
        _prompts[name] = new RegisteredPrompt
        {
            Definition = new McpPromptDefinition
            {
                Name = name,
                Description = description,
                Arguments = args
            },
            Generator = generator
        };
    }

    public List<McpToolDefinition> GetTools() => _tools.Values.Select(t => t.Definition).OrderBy(t => t.Name).ToList();
    public List<McpResourceDefinition> GetResources() => _resources.Values.Select(r => r.Definition).OrderBy(r => r.Name).ToList();
    public List<McpPromptDefinition> GetPrompts() => _prompts.Values.Select(p => p.Definition).OrderBy(p => p.Name).ToList();

    public async Task<McpToolResult> CallToolAsync(string name, JsonElement? arguments, CancellationToken ct = default)
    {
        if (!_tools.TryGetValue(name, out var tool))
        {
            return McpToolResult.Error($"Unbekanntes Werkzeug: '{name}'. Verfügbare Werkzeuge können mit tools/list abgerufen werden.");
        }

        try
        {
            var args = BindArguments(tool.Parameters, arguments);
            object? result = tool.Method.Invoke(tool.Target, args);

            if (result is Task<McpToolResult> taskMcp)
            {
                return await taskMcp;
            }
            if (result is Task task)
            {
                await task;
                var prop = task.GetType().GetProperty("Result");
                if (prop != null)
                {
                    var taskVal = prop.GetValue(task);
                    if (taskVal is McpToolResult mcpVal) return mcpVal;
                    return McpToolResult.Text(taskVal?.ToString() ?? "Erfolgreich ausgeführt.");
                }
                return McpToolResult.Text("Erfolgreich ausgeführt.");
            }
            if (result is McpToolResult directMcp)
            {
                return directMcp;
            }

            return McpToolResult.Text(result?.ToString() ?? "Erfolgreich ausgeführt.");
        }
        catch (TargetInvocationException tie)
        {
            var inner = tie.InnerException ?? tie;
            return McpToolResult.Error($"Ausführungsfehler in {name}: {inner.Message}");
        }
        catch (Exception ex)
        {
            return McpToolResult.Error($"Fehler beim Aufruf von {name}: {ex.Message}");
        }
    }

    public async Task<string> ReadResourceAsync(string uri)
    {
        if (!_resources.TryGetValue(uri, out var resource))
        {
            throw new KeyNotFoundException($"Resource '{uri}' wurde nicht gefunden.");
        }
        return await resource.Reader(uri);
    }

    public async Task<List<McpPromptMessage>> GetPromptAsync(string name, Dictionary<string, string> args)
    {
        if (!_prompts.TryGetValue(name, out var prompt))
        {
            throw new KeyNotFoundException($"Prompt '{name}' wurde nicht gefunden.");
        }
        return await prompt.Generator(args);
    }

    private static object?[] BindArguments(ParameterInfo[] parameters, JsonElement? argsElement)
    {
        var result = new object?[parameters.Length];
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        if (argsElement.HasValue && argsElement.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in argsElement.Value.EnumerateObject())
            {
                dict[prop.Name] = prop.Value;
            }
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var pName = p.Name ?? $"arg{i}";

            if (TryFindArgument(dict, pName, out var val))
            {
                result[i] = ConvertJsonElement(val, p.ParameterType);
            }
            else if (p.HasDefaultValue)
            {
                result[i] = p.DefaultValue;
            }
            else
            {
                result[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
            }
        }

        return result;
    }

    private static bool TryFindArgument(Dictionary<string, JsonElement> dict, string paramName, out JsonElement value)
    {
        // 1. Direkter Treffer
        if (dict.TryGetValue(paramName, out value)) return true;

        // 2. Snake_case Treffer
        string snake = ToSnakeCase(paramName);
        if (dict.TryGetValue(snake, out value)) return true;

        // 3. Häufige semantische Synonyme für Bildbearbeitungs-Tools
        string[] aliases = paramName.ToLowerInvariant() switch
        {
            "inputpath" => new[] { "sourcepath", "source_path", "source", "filepath", "file_path", "imagepath", "image_path", "path", "file", "bildpfad" },
            "outputpath" => new[] { "destpath", "dest_path", "targetpath", "target_path", "destination", "output", "zielpfad" },
            "format" => new[] { "targetformat", "target_format", "outputformat", "output_format", "zielformat" },
            "width" => new[] { "maxwidth", "max_width", "targetwidth", "target_width", "breite" },
            "height" => new[] { "maxheight", "max_height", "targetheight", "target_height", "hoehe" },
            _ => Array.Empty<string>()
        };

        foreach (var alias in aliases)
        {
            if (dict.TryGetValue(alias, out value)) return true;
        }

        value = default;
        return false;
    }

    private static string ToSnakeCase(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;
        var sb = new StringBuilder();
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static object? ConvertJsonElement(JsonElement elem, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (elem.ValueKind == JsonValueKind.Null) return null;

        if (underlying == typeof(string)) return elem.GetString();
        if (underlying == typeof(int)) return elem.GetInt32();
        if (underlying == typeof(long)) return elem.GetInt64();
        if (underlying == typeof(float)) return (float)elem.GetDouble();
        if (underlying == typeof(double)) return elem.GetDouble();
        if (underlying == typeof(bool)) return elem.GetBoolean();
        if (underlying.IsEnum)
        {
            string? str = elem.GetString();
            if (str != null && Enum.TryParse(underlying, str, true, out var enumVal))
            {
                return enumVal;
            }
            return Activator.CreateInstance(underlying);
        }

        return JsonSerializer.Deserialize(elem.GetRawText(), targetType);
    }

    private static Dictionary<string, object> BuildInputSchema(ParameterInfo[] parameters)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var p in parameters)
        {
            var pName = p.Name ?? "arg";
            var attr = p.GetCustomAttribute<McpParameterAttribute>();
            var propSchema = new Dictionary<string, object>();

            var type = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;

            if (type == typeof(string))
            {
                propSchema["type"] = "string";
            }
            else if (type == typeof(int) || type == typeof(long))
            {
                propSchema["type"] = "integer";
            }
            else if (type == typeof(float) || type == typeof(double))
            {
                propSchema["type"] = "number";
            }
            else if (type == typeof(bool))
            {
                propSchema["type"] = "boolean";
            }
            else if (type.IsEnum)
            {
                propSchema["type"] = "string";
                propSchema["enum"] = Enum.GetNames(type).Select(n => n.ToLowerInvariant()).ToArray();
            }
            else
            {
                propSchema["type"] = "object";
            }

            if (attr != null)
            {
                propSchema["description"] = attr.Description;
                if (attr.Required && !p.HasDefaultValue)
                {
                    required.Add(pName);
                }
            }
            else if (!p.HasDefaultValue && Nullable.GetUnderlyingType(p.ParameterType) == null && type != typeof(string))
            {
                required.Add(pName);
            }

            properties[pName] = propSchema;
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }
}
