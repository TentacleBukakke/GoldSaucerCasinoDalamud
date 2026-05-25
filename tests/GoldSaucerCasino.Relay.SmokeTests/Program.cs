using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var relayUrl = args.Length > 0 ? args[0].TrimEnd('/') : "http://127.0.0.1:5217";
using var http = new HttpClient { BaseAddress = new Uri(relayUrl) };

var createRoom = await http.PostAsync("/rooms", null);
createRoom.EnsureSuccessStatusCode();
var room = await createRoom.Content.ReadFromJsonAsync<CreateRoomResponse>() ?? throw new InvalidOperationException("No room response.");

Assert(room.RoomCode.Length == 6, "room code should be six digits");

using var alice = new ClientWebSocket();
using var bob = new ClientWebSocket();
await alice.ConnectAsync(ToWebSocketUri(relayUrl, room.RoomCode, "Alice"), CancellationToken.None);
await bob.ConnectAsync(ToWebSocketUri(relayUrl, room.RoomCode, "Bob"), CancellationToken.None);

await SendAsync(alice, "{\"type\":\"ready\",\"ready\":true}");
var received = await ReceiveUntilAsync(bob, message => message.Contains("Alice", StringComparison.Ordinal) && message.Contains("ready", StringComparison.Ordinal));
Assert(received.Contains(room.RoomCode, StringComparison.Ordinal), "broadcast should include the room code");

Console.WriteLine($"pass: relay room {room.RoomCode} broadcasts between clients");

static Uri ToWebSocketUri(string relayUrl, string roomCode, string name)
{
    var uri = new Uri(relayUrl);
    var scheme = uri.Scheme == "https" ? "wss" : "ws";
    return new Uri($"{scheme}://{uri.Authority}/rooms/{roomCode}/ws?name={Uri.EscapeDataString(name)}");
}

static async Task SendAsync(ClientWebSocket socket, string message)
{
    var bytes = Encoding.UTF8.GetBytes(message);
    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
}

static async Task<string> ReceiveUntilAsync(ClientWebSocket socket, Func<string, bool> predicate)
{
    var buffer = new byte[16 * 1024];
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < TimeSpan.FromSeconds(5))
    {
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
        if (predicate(message))
        {
            return message;
        }
    }

    throw new TimeoutException("Did not receive expected relay message.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

public sealed record CreateRoomResponse(string RoomCode);
