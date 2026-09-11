using System;

namespace Imaginary.Core.Mcp;

/// <summary>
/// Kennzeichnet eine Methode als ausführbares MCP-Werkzeug (Model Context Protocol).
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class McpToolAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    public McpToolAttribute(string name, string description)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }
}

/// <summary>
/// Beschreibt einen Parameter für die automatische Schema-Generierung eines MCP-Werkzeugs.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class McpParameterAttribute : Attribute
{
    public string Description { get; }
    public bool Required { get; set; } = true;
    public object? DefaultValue { get; set; }

    public McpParameterAttribute(string description)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }
}
