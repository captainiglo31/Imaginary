using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Imaginary.Core.Mcp;
using Imaginary.Core.Tests.TestHelpers;
using SkiaSharp;
using Xunit;

namespace Imaginary.Core.Tests;

public class McpServerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly McpServer _server;

    public McpServerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Imaginary_McpTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _server = ImaginaryMcpFactory.CreateServer();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task Initialize_ShouldReturnServerInfoAndProtocolVersion()
    {
        // Arrange
        var requestJson = """
        {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "initialize",
            "params": {
                "protocolVersion": "2024-11-05",
                "clientInfo": { "name": "test-client", "version": "1.0.0" }
            }
        }
        """;

        // Act
        string? responseJson = await _server.ProcessLineAsync(requestJson);

        // Assert
        responseJson.Should().NotBeNull();
        using var doc = JsonDocument.Parse(responseJson!);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.GetProperty("id").GetInt32().Should().Be(1);

        var result = root.GetProperty("result");
        result.GetProperty("protocolVersion").GetString().Should().Be("2024-11-05");
        result.GetProperty("serverInfo").GetProperty("name").GetString().Should().Be("imaginary-mcp");
        result.GetProperty("capabilities").TryGetProperty("tools", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Ping_ShouldReturnEmptySuccessResult()
    {
        var requestJson = """
        {
            "jsonrpc": "2.0",
            "id": 2,
            "method": "ping"
        }
        """;

        string? responseJson = await _server.ProcessLineAsync(requestJson);
        responseJson.Should().NotBeNull();

        using var doc = JsonDocument.Parse(responseJson!);
        doc.RootElement.GetProperty("id").GetInt32().Should().Be(2);
        doc.RootElement.TryGetProperty("result", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("error", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ToolsList_ShouldReturnRegisteredToolsWithValidSchemas()
    {
        var requestJson = """
        {
            "jsonrpc": "2.0",
            "id": 3,
            "method": "tools/list"
        }
        """;

        string? responseJson = await _server.ProcessLineAsync(requestJson);
        responseJson.Should().NotBeNull();

        using var doc = JsonDocument.Parse(responseJson!);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        tools.GetArrayLength().Should().BeGreaterOrEqualTo(8);

        var toolNames = new System.Collections.Generic.List<string>();
        foreach (var tool in tools.EnumerateArray())
        {
            string name = tool.GetProperty("name").GetString()!;
            toolNames.Add(name);

            tool.TryGetProperty("description", out _).Should().BeTrue();
            var schema = tool.GetProperty("inputSchema");
            schema.GetProperty("type").GetString().Should().Be("object");
            schema.TryGetProperty("properties", out _).Should().BeTrue();
        }

        toolNames.Should().Contain(new[]
        {
            "convert_image",
            "remove_background",
            "segment_object_by_region",
            "redact_region",
            "crop_image",
            "apply_watermark",
            "generate_icon_set",
            "inspect_image"
        });
    }

    [Fact]
    public async Task ToolsCall_InspectImage_ShouldReturnCorrectDimensionsAndFormat()
    {
        // Arrange: generate a 120x80 PNG
        byte[] pngBytes = TestImageGenerator.CreatePatternImage(120, 80, SKEncodedImageFormat.Png);
        string imagePath = Path.Combine(_tempDir, "sample.png");
        await File.WriteAllBytesAsync(imagePath, pngBytes);

        var requestJson = $@"
        {{
            ""jsonrpc"": ""2.0"",
            ""id"": 4,
            ""method"": ""tools/call"",
            ""params"": {{
                ""name"": ""inspect_image"",
                ""arguments"": {{
                    ""sourcePath"": {JsonSerializer.Serialize(imagePath)}
                }}
            }}
        }}";

        // Act
        string? responseJson = await _server.ProcessLineAsync(requestJson);

        // Assert
        responseJson.Should().NotBeNull();
        using var doc = JsonDocument.Parse(responseJson!);
        var result = doc.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeFalse();

        var contentArray = result.GetProperty("content");
        contentArray.GetArrayLength().Should().Be(1);

        string text = contentArray[0].GetProperty("text").GetString()!;
        using var metadataDoc = JsonDocument.Parse(text);
        var meta = metadataDoc.RootElement;

        meta.GetProperty("width").GetInt32().Should().Be(120);
        meta.GetProperty("height").GetInt32().Should().Be(80);
        meta.GetProperty("format").GetString().Should().Be("Png");
    }

    [Fact]
    public async Task ToolsCall_ConvertImage_ShouldProduceValidTargetFile()
    {
        // Arrange
        byte[] pngBytes = TestImageGenerator.CreatePatternImage(100, 100, SKEncodedImageFormat.Png);
        string inputPath = Path.Combine(_tempDir, "input.png");
        string outputPath = Path.Combine(_tempDir, "output.webp");
        await File.WriteAllBytesAsync(inputPath, pngBytes);

        var requestJson = $@"
        {{
            ""jsonrpc"": ""2.0"",
            ""id"": 5,
            ""method"": ""tools/call"",
            ""params"": {{
                ""name"": ""convert_image"",
                ""arguments"": {{
                    ""sourcePath"": {JsonSerializer.Serialize(inputPath)},
                    ""outputPath"": {JsonSerializer.Serialize(outputPath)},
                    ""targetFormat"": ""Webp"",
                    ""maxWidth"": 50,
                    ""maxHeight"": 50,
                    ""quality"": 85
                }}
            }}
        }}";

        // Act
        string? responseJson = await _server.ProcessLineAsync(requestJson);

        // Assert
        responseJson.Should().NotBeNull();
        File.Exists(outputPath).Should().BeTrue();
        new FileInfo(outputPath).Length.Should().BeGreaterThan(0);

        // Verify converted image dimensions
        using var codec = SKCodec.Create(outputPath);
        codec.Should().NotBeNull();
        codec.Info.Width.Should().Be(50);
        codec.Info.Height.Should().Be(50);
    }

    [Fact]
    public async Task ToolsCall_RedactRegion_ShouldModifyImage()
    {
        // Arrange
        byte[] pngBytes = TestImageGenerator.CreatePatternImage(200, 200, SKEncodedImageFormat.Png);
        string inputPath = Path.Combine(_tempDir, "original.png");
        string outputPath = Path.Combine(_tempDir, "redacted.png");
        await File.WriteAllBytesAsync(inputPath, pngBytes);

        var requestJson = $@"
        {{
            ""jsonrpc"": ""2.0"",
            ""id"": 6,
            ""method"": ""tools/call"",
            ""params"": {{
                ""name"": ""redact_region"",
                ""arguments"": {{
                    ""sourcePath"": {JsonSerializer.Serialize(inputPath)},
                    ""outputPath"": {JsonSerializer.Serialize(outputPath)},
                    ""x"": 20,
                    ""y"": 20,
                    ""width"": 50,
                    ""height"": 50,
                    ""style"": ""Blackout""
                }}
            }}
        }}";

        // Act
        string? responseJson = await _server.ProcessLineAsync(requestJson);

        // Assert
        responseJson.Should().NotBeNull();
        File.Exists(outputPath).Should().BeTrue();

        using var bitmap = SKBitmap.Decode(outputPath);
        // Pixel at (25, 25) inside blackout region should be black
        var pixel = bitmap.GetPixel(25, 25);
        pixel.Red.Should().Be(0);
        pixel.Green.Should().Be(0);
        pixel.Blue.Should().Be(0);
    }

    [Fact]
    public async Task ResourcesList_And_Read_ShouldReturnCapabilities()
    {
        // List resources
        var listReq = """
        {
            "jsonrpc": "2.0",
            "id": 7,
            "method": "resources/list"
        }
        """;
        string? listResp = await _server.ProcessLineAsync(listReq);
        listResp.Should().NotBeNull();
        using var listDoc = JsonDocument.Parse(listResp!);
        var resArray = listDoc.RootElement.GetProperty("result").GetProperty("resources");
        resArray.GetArrayLength().Should().BeGreaterOrEqualTo(1);

        // Read resource
        var readReq = """
        {
            "jsonrpc": "2.0",
            "id": 8,
            "method": "resources/read",
            "params": {
                "uri": "imaginary://system/capabilities"
            }
        }
        """;
        string? readResp = await _server.ProcessLineAsync(readReq);
        readResp.Should().NotBeNull();
        using var readDoc = JsonDocument.Parse(readResp!);
        var contents = readDoc.RootElement.GetProperty("result").GetProperty("contents");
        contents.GetArrayLength().Should().Be(1);
        string text = contents[0].GetProperty("text").GetString()!;
        text.Should().Contain("supportedFormats");
    }

    [Fact]
    public async Task PromptsList_And_Get_ShouldReturnFormattedPrompt()
    {
        // List prompts
        var listReq = """
        {
            "jsonrpc": "2.0",
            "id": 9,
            "method": "prompts/list"
        }
        """;
        string? listResp = await _server.ProcessLineAsync(listReq);
        listResp.Should().NotBeNull();

        // Get prompt
        var getReq = """
        {
            "jsonrpc": "2.0",
            "id": 10,
            "method": "prompts/get",
            "params": {
                "name": "ecommerce_product_cleanup",
                "arguments": {
                    "imagePath": "C:\\images\\shoe.png"
                }
            }
        }
        """;
        string? getResp = await _server.ProcessLineAsync(getReq);
        getResp.Should().NotBeNull();
        using var getDoc = JsonDocument.Parse(getResp!);
        var messages = getDoc.RootElement.GetProperty("result").GetProperty("messages");
        messages.GetArrayLength().Should().Be(1);
        string userMsg = messages[0].GetProperty("content").GetProperty("text").GetString()!;
        userMsg.Should().Contain("C:\\images\\shoe.png");
    }

    [Fact]
    public async Task FullStream_RunAsync_ShouldProcessMultipleRequestsSequentially()
    {
        var input = """
        {"jsonrpc":"2.0","id":11,"method":"ping"}
        {"jsonrpc":"2.0","id":12,"method":"ping"}
        """;

        using var reader = new StringReader(input);
        using var writer = new StringWriter();

        await _server.RunAsync(reader, writer);

        string output = writer.ToString();
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().Be(2);

        using var doc1 = JsonDocument.Parse(lines[0]);
        doc1.RootElement.GetProperty("id").GetInt32().Should().Be(11);

        using var doc2 = JsonDocument.Parse(lines[1]);
        doc2.RootElement.GetProperty("id").GetInt32().Should().Be(12);
    }

    [Fact]
    public void GenerateClaudeConfig_ShouldContainExecutablePathAndMcpArg()
    {
        string configJson = McpServer.GenerateClaudeConfig("C:\\Tools\\Imaginary.exe");
        configJson.Should().Contain("\"imaginary\"");
        configJson.Should().Contain("\"--mcp\"");
        configJson.Should().Contain("C:\\\\Tools\\\\Imaginary.exe");
    }
}
