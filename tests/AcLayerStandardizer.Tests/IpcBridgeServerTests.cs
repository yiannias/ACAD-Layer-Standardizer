using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

public class IpcBridgeServerTests
{
    [Fact]
    public async Task Server_responds_to_ping()
    {
        IpcBridgeServer.Start();
        Assert.True(IpcBridgeServer.IsRunning);

        try
        {
            using var client = new NamedPipeClientStream(".", IpcBridgeServer.PipeName, PipeDirection.InOut);
            await client.ConnectAsync(3000);

            using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(client, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "Ping" }));

            string? response = await reader.ReadLineAsync();
            Assert.NotNull(response);

            using var doc = JsonDocument.Parse(response);
            Assert.Equal("Pong", doc.RootElement.GetProperty("type").GetString());
        }
        finally
        {
            IpcBridgeServer.Stop();
        }
    }
}
