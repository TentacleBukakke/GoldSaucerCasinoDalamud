using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoldSaucerCasino.Plugin.Relay;

public sealed class RelayClient : IDisposable
{
    private readonly HttpClient httpClient = new();
    private readonly CancellationTokenSource cancellation = new();
    private ClientWebSocket? socket;

    public event Action<IReadOnlyList<string>>? PlayersChanged;

    public event Action<string>? StatusChanged;

    public string? RoomCode { get; private set; }

    public bool IsConnected => this.socket?.State == WebSocketState.Open;

    public async Task<string> HostAsync(string relayUrl, string playerName)
    {
        var normalizedRelayUrl = relayUrl.TrimEnd('/');
        var response = await this.httpClient.PostAsync($"{normalizedRelayUrl}/rooms", null, this.cancellation.Token);
        response.EnsureSuccessStatusCode();
        var room = await response.Content.ReadFromJsonAsync<CreateRoomResponse>(RelayJson.Options, this.cancellation.Token)
            ?? throw new InvalidOperationException("Relay did not return a room code.");

        await this.ConnectAsync(normalizedRelayUrl, room.RoomCode, playerName);
        return room.RoomCode;
    }

    public async Task JoinAsync(string relayUrl, string roomCode, string playerName)
    {
        var normalizedRelayUrl = relayUrl.TrimEnd('/');
        var response = await this.httpClient.GetAsync($"{normalizedRelayUrl}/rooms/{roomCode}", this.cancellation.Token);
        response.EnsureSuccessStatusCode();
        await this.ConnectAsync(normalizedRelayUrl, roomCode, playerName);
    }

    public async Task DisconnectAsync()
    {
        if (this.socket is { State: WebSocketState.Open } activeSocket)
        {
            await activeSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Leaving room", CancellationToken.None);
        }

        this.socket?.Dispose();
        this.socket = null;
        this.RoomCode = null;
        this.PlayersChanged?.Invoke([]);
        this.StatusChanged?.Invoke("Disconnected from relay.");
    }

    public void Dispose()
    {
        this.cancellation.Cancel();
        this.socket?.Dispose();
        this.httpClient.Dispose();
        this.cancellation.Dispose();
    }

    private async Task ConnectAsync(string relayUrl, string roomCode, string playerName)
    {
        await this.DisconnectAsync();

        this.socket = new ClientWebSocket();
        this.RoomCode = roomCode;
        await this.socket.ConnectAsync(ToWebSocketUri(relayUrl, roomCode, playerName), this.cancellation.Token);
        this.StatusChanged?.Invoke($"Connected to relay room {roomCode}.");
        _ = Task.Run(this.ReceiveLoopAsync);
    }

    private async Task ReceiveLoopAsync()
    {
        if (this.socket is null)
        {
            return;
        }

        var buffer = new byte[16 * 1024];
        try
        {
            while (!this.cancellation.IsCancellationRequested && this.socket.State == WebSocketState.Open)
            {
                var result = await this.socket.ReceiveAsync(buffer, this.cancellation.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var envelope = JsonSerializer.Deserialize<RelayEnvelope>(json, RelayJson.Options);
                if (envelope?.Payload is null)
                {
                    continue;
                }

                if (envelope.Type is "room.joined" or "room.left")
                {
                    var snapshot = JsonSerializer.Deserialize<RoomSnapshot>(envelope.Payload, RelayJson.Options);
                    if (snapshot is not null)
                    {
                        this.PlayersChanged?.Invoke(snapshot.Players.Select(player => player.Name).ToArray());
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            this.StatusChanged?.Invoke($"Relay error: {ex.Message}");
        }
    }

    private static Uri ToWebSocketUri(string relayUrl, string roomCode, string playerName)
    {
        var uri = new Uri(relayUrl);
        var scheme = uri.Scheme == "https" ? "wss" : "ws";
        return new Uri($"{scheme}://{uri.Authority}/rooms/{roomCode}/ws?name={Uri.EscapeDataString(playerName)}");
    }
}

public sealed record CreateRoomResponse(string RoomCode);

public sealed record RoomSnapshot(string RoomCode, IReadOnlyList<PlayerSnapshot> Players);

public sealed record PlayerSnapshot(Guid Id, string Name);

public sealed record RelayEnvelope(string Type, string RoomCode, Guid SenderId, string SenderName, string Payload);

public static class RelayJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };
}
