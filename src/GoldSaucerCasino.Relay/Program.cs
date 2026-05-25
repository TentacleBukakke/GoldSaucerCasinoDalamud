using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var rooms = new RoomRegistry();
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(renderPort))
{
    app.Urls.Add($"http://0.0.0.0:{renderPort}");
}

app.UseWebSockets();

app.MapGet("/", () => Results.Json(new
{
    service = "GoldSaucerCasino.Relay",
    status = "ok",
    endpoints = new[] { "POST /rooms", "GET /rooms/{roomCode}", "WS /rooms/{roomCode}/ws?name=Character" },
}));

app.MapPost("/rooms", () =>
{
    var room = rooms.CreateRoom();
    return Results.Json(new CreateRoomResponse(room.Code));
});

app.MapGet("/rooms/{roomCode}", (string roomCode) =>
{
    var room = rooms.GetRoom(roomCode);
    return room is null
        ? Results.NotFound(new { error = "Room not found." })
        : Results.Json(room.ToSnapshot());
});

app.Map("/rooms/{roomCode}/ws", async (HttpContext context, string roomCode) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Expected a WebSocket request.");
        return;
    }

    var room = rooms.GetRoom(roomCode);
    if (room is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("Room not found.");
        return;
    }

    var playerName = context.Request.Query["name"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(playerName))
    {
        playerName = $"Player-{Random.Shared.Next(1000, 10000)}";
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var client = await room.AddClientAsync(playerName, socket, context.RequestAborted);

    try
    {
        await ReceiveLoopAsync(room, client, context.RequestAborted);
    }
    finally
    {
        await room.RemoveClientAsync(client.Id, context.RequestAborted);
    }
});

app.Run();

static async Task ReceiveLoopAsync(Room room, RoomClient client, CancellationToken cancellationToken)
{
    var buffer = new byte[16 * 1024];
    while (!cancellationToken.IsCancellationRequested && client.Socket.State == WebSocketState.Open)
    {
        var result = await client.Socket.ReceiveAsync(buffer, cancellationToken);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            break;
        }

        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
        await room.BroadcastAsync(new RelayEnvelope(
            Type: "client.message",
            RoomCode: room.Code,
            SenderId: client.Id,
            SenderName: client.Name,
            Payload: message), cancellationToken);
    }
}

public sealed class RoomRegistry
{
    private readonly ConcurrentDictionary<string, Room> rooms = [];

    public Room CreateRoom()
    {
        for (var attempts = 0; attempts < 50; attempts++)
        {
            var code = Random.Shared.Next(100000, 1000000).ToString();
            var room = new Room(code);
            if (this.rooms.TryAdd(code, room))
            {
                return room;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique room code.");
    }

    public Room? GetRoom(string roomCode) =>
        this.rooms.TryGetValue(roomCode.Trim(), out var room) ? room : null;
}

public sealed class Room
{
    private readonly ConcurrentDictionary<Guid, RoomClient> clients = [];

    public Room(string code)
    {
        this.Code = code;
    }

    public string Code { get; }

    public async Task<RoomClient> AddClientAsync(string playerName, WebSocket socket, CancellationToken cancellationToken)
    {
        var client = new RoomClient(Guid.NewGuid(), playerName, socket);
        this.clients[client.Id] = client;
        await this.BroadcastAsync(new RelayEnvelope(
            Type: "room.joined",
            RoomCode: this.Code,
            SenderId: client.Id,
            SenderName: client.Name,
            Payload: JsonSerializer.Serialize(this.ToSnapshot())), cancellationToken);
        return client;
    }

    public async Task RemoveClientAsync(Guid clientId, CancellationToken cancellationToken)
    {
        if (!this.clients.TryRemove(clientId, out var client))
        {
            return;
        }

        await this.BroadcastAsync(new RelayEnvelope(
            Type: "room.left",
            RoomCode: this.Code,
            SenderId: client.Id,
            SenderName: client.Name,
            Payload: JsonSerializer.Serialize(this.ToSnapshot())), cancellationToken);
    }

    public RoomSnapshot ToSnapshot() =>
        new(this.Code, this.clients.Values.Select(client => new PlayerSnapshot(client.Id, client.Name)).OrderBy(player => player.Name).ToArray());

    public async Task BroadcastAsync(RelayEnvelope envelope, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(envelope, RelayJson.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        var deadClients = new List<Guid>();

        foreach (var client in this.clients.Values)
        {
            if (client.Socket.State != WebSocketState.Open)
            {
                deadClients.Add(client.Id);
                continue;
            }

            try
            {
                await client.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
            catch
            {
                deadClients.Add(client.Id);
            }
        }

        foreach (var clientId in deadClients)
        {
            this.clients.TryRemove(clientId, out _);
        }
    }
}

public sealed record RoomClient(Guid Id, string Name, WebSocket Socket);

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
