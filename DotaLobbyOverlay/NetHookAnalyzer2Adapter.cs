// Add this helper to the NetHookAnalyzer2 process (or call equivalent code there).
// It sends one JSON line through an OS named pipe. No .bin files or roster files are written.
// The NetHookAnalyzer2 integration point must call Publish only after it has parsed a complete lobby.
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace DotaLobbyOverlay.Integration;

public sealed record LivePlayer(string Name, int Team, int Slot, string? AccountId = null);
public sealed record LiveRoster(IReadOnlyList<LivePlayer> Players, string? MyAccountId = null);

public static class NetHookAnalyzer2Adapter
{
    public static async Task PublishAsync(LiveRoster roster, CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(".", "DotaLobbyOverlay.LiveRoster", PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1000, cancellationToken);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(roster, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
