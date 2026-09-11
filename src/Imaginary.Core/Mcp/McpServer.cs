using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Imaginary.Core.Mcp;

public class McpServer
{
    private readonly McpRegistry _registry;
    private readonly string _serverName;
    private readonly string _serverVersion;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public McpServer(McpRegistry registry, string serverName = "imaginary-mcp", string serverVersion = "2.2.1")
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _serverName = serverName;
        _serverVersion = serverVersion;
    }

    /// <summary>
    /// Startet den MCP-Server auf stdin/stdout. Blockiert bis der Stream beendet wird.
    /// </summary>
    public async Task RunStdioAsync(CancellationToken ct = default)
    {
        try
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // Ignorieren falls kein Konsolenfenster existiert (z.B. bei IPC-Pipes oder WinExe)
        }

        using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

        await RunAsync(reader, writer, ct);
    }

    /// <summary>
    /// Liest MCP-Anfragen zeilenweise aus dem Reader und schreibt JSON-RPC Antworten in den Writer.
    /// </summary>
    public async Task RunAsync(TextReader reader, TextWriter writer, CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line == null) break; // EOF
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? responseJson = await ProcessLineAsync(line, ct);
            if (responseJson != null)
            {
                await writer.WriteLineAsync(responseJson);
            }
        }
    }

    public async Task<string?> ProcessLineAsync(string jsonLine, CancellationToken ct = default)
    {
        McpRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<McpRequest>(jsonLine, JsonOptions);
        }
        catch (Exception ex)
        {
            var err = McpResponse.Fail(null, -32700, "Parse error: Ungültiges JSON", ex.Message);
            return JsonSerializer.Serialize(err, JsonOptions);
        }

        if (request == null) return null;

        // Notifications haben keine id und erfordern keine Antwort
        if (request.Id == null && request.Method.StartsWith("notifications/"))
        {
            return null;
        }

        var response = await HandleRequestAsync(request, ct);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private async Task<McpResponse> HandleRequestAsync(McpRequest req, CancellationToken ct)
    {
        switch (req.Method.ToLowerInvariant())
        {
            case "initialize":
                return McpResponse.Success(req.Id, new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new
                    {
                        tools = new { listChanged = false },
                        resources = new { subscribe = false, listChanged = false },
                        prompts = new { listChanged = false }
                    },
                    serverInfo = new
                    {
                        name = _serverName,
                        version = _serverVersion
                    }
                });

            case "ping":
                return McpResponse.Success(req.Id, new { });

            case "tools/list":
                return McpResponse.Success(req.Id, new
                {
                    tools = _registry.GetTools()
                });

            case "tools/call":
                if (!req.Params.HasValue)
                {
                    return McpResponse.Fail(req.Id, -32602, "Invalid params: 'params' Objekt fehlt.");
                }

                string toolName = "";
                JsonElement? toolArgs = null;

                if (req.Params.Value.TryGetProperty("name", out var nameProp))
                {
                    toolName = nameProp.GetString() ?? "";
                }
                if (req.Params.Value.TryGetProperty("arguments", out var argsProp))
                {
                    toolArgs = argsProp;
                }

                if (string.IsNullOrEmpty(toolName))
                {
                    return McpResponse.Fail(req.Id, -32602, "Parameter 'name' fehlt.");
                }

                var toolResult = await _registry.CallToolAsync(toolName, toolArgs, ct);
                return McpResponse.Success(req.Id, toolResult);

            case "resources/list":
                return McpResponse.Success(req.Id, new
                {
                    resources = _registry.GetResources()
                });

            case "resources/read":
                if (!req.Params.HasValue || !req.Params.Value.TryGetProperty("uri", out var uriProp))
                {
                    return McpResponse.Fail(req.Id, -32602, "Parameter 'uri' fehlt.");
                }
                string uri = uriProp.GetString() ?? "";
                try
                {
                    string content = await _registry.ReadResourceAsync(uri);
                    return McpResponse.Success(req.Id, new
                    {
                        contents = new[]
                        {
                            new { uri = uri, mimeType = "application/json", text = content }
                        }
                    });
                }
                catch (KeyNotFoundException)
                {
                    return McpResponse.Fail(req.Id, -32002, $"Resource '{uri}' nicht gefunden.");
                }

            case "prompts/list":
                return McpResponse.Success(req.Id, new
                {
                    prompts = _registry.GetPrompts()
                });

            case "prompts/get":
                if (!req.Params.HasValue || !req.Params.Value.TryGetProperty("name", out var promptNameProp))
                {
                    return McpResponse.Fail(req.Id, -32602, "Parameter 'name' fehlt.");
                }
                string pName = promptNameProp.GetString() ?? "";
                var pArgs = new Dictionary<string, string>();
                if (req.Params.Value.TryGetProperty("arguments", out var pArgsObj) && pArgsObj.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in pArgsObj.EnumerateObject())
                    {
                        pArgs[prop.Name] = prop.Value.GetString() ?? "";
                    }
                }
                try
                {
                    var messages = await _registry.GetPromptAsync(pName, pArgs);
                    return McpResponse.Success(req.Id, new { messages });
                }
                catch (KeyNotFoundException)
                {
                    return McpResponse.Fail(req.Id, -32002, $"Prompt '{pName}' nicht gefunden.");
                }

            default:
                return McpResponse.Fail(req.Id, -32601, $"Unbekannte MCP-Methode: '{req.Method}'");
        }
    }

    /// <summary>
    /// Generiert einen fertigen JSON-Block zur Einbindung in claude_desktop_config.json
    /// </summary>
    public static string GenerateClaudeDesktopConfig(string executablePath)
    {
        var config = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["imaginary"] = new
                {
                    command = executablePath,
                    args = new[] { "--mcp" }
                }
            }
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Alias für GenerateClaudeDesktopConfig
    /// </summary>
    public static string GenerateClaudeConfig(string executablePath) => GenerateClaudeDesktopConfig(executablePath);

    /// <summary>
    /// Installiert die Konfiguration automatisch in %APPDATA%\Claude\claude_desktop_config.json
    /// </summary>
    public static bool TryInstallClaudeDesktopConfig(string executablePath, out string message)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string claudeDir = Path.Combine(appData, "Claude");
            string configFile = Path.Combine(claudeDir, "claude_desktop_config.json");

            if (!Directory.Exists(claudeDir)) Directory.CreateDirectory(claudeDir);

            Dictionary<string, object> root = new();
            if (File.Exists(configFile))
            {
                try
                {
                    string existingJson = File.ReadAllText(configFile);
                    root = JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson) ?? new();
                }
                catch { root = new(); }
            }

            Dictionary<string, object> servers = new();
            if (root.TryGetValue("mcpServers", out var serversObj) && serversObj is JsonElement elem && elem.ValueKind == JsonValueKind.Object)
            {
                servers = JsonSerializer.Deserialize<Dictionary<string, object>>(elem.GetRawText()) ?? new();
            }

            servers["imaginary"] = new
            {
                command = executablePath,
                args = new[] { "--mcp" }
            };

            root["mcpServers"] = servers;
            string updatedJson = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFile, updatedJson, Encoding.UTF8);

            message = $"Erfolgreich in {configFile} eingetragen!";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Fehler bei der Installation: {ex.Message}";
            return false;
        }
    }
}
