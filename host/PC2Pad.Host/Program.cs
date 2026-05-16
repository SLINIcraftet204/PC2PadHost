using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PC2Pad.Host.Capture;
using PC2Pad.Host.Input;
using PC2Pad.Host.Models;

var builder = WebApplication.CreateBuilder(args);

var bindAddress = builder.Configuration.GetValue<string>("PC2Pad:BindAddress") ?? "0.0.0.0";
var port = builder.Configuration.GetValue<int?>("PC2Pad:Port") ?? 8128;

builder.WebHost.UseUrls($"http://{bindAddress}:{port}");
builder.Services.AddSingleton<InputRouter>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20)
});

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
};

app.MapGet("/", () => Results.Text("""
PC2Pad Host läuft.

API:
  GET  /api/health
  GET  /api/games
  POST /api/games/{id}/launch
  WS   /ws/input
  GET  /stream/test.mjpeg
""", "text/plain; charset=utf-8"));

app.MapGet("/api/health", () => Results.Json(new
{
    name = "PC2Pad.Host",
    status = "ok",
    version = "0.1.0-mvp",
    time = DateTimeOffset.Now
}, jsonOptions));

app.MapGet("/api/games", () =>
{
    var games = LoadGames();
    return Results.Json(games.Where(g => g.Enabled), jsonOptions);
});

app.MapPost("/api/games/{id}/launch", (string id, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("GameLauncher");
    var game = LoadGames().FirstOrDefault(g => g.Enabled && g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    if (game is null)
    {
        return Results.NotFound(new { error = $"Game '{id}' wurde nicht gefunden oder ist deaktiviert." });
    }

    if (string.IsNullOrWhiteSpace(game.Executable))
    {
        return Results.BadRequest(new { error = $"Game '{id}' hat keinen executable-Wert." });
    }

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = game.Executable,
            Arguments = game.Arguments ?? "",
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(game.WorkingDirectory))
        {
            psi.WorkingDirectory = game.WorkingDirectory;
        }

        Process.Start(psi);
        logger.LogInformation("Launched game {GameId}: {Executable} {Arguments}", game.Id, game.Executable, game.Arguments);

        return Results.Ok(new
        {
            launched = true,
            id = game.Id,
            title = game.Title
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to launch game {GameId}", game.Id);
        return Results.Problem($"Start fehlgeschlagen: {ex.Message}");
    }
});

app.Map("/ws/input", async (HttpContext context, InputRouter inputRouter, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("InputWebSocket");

    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Expected WebSocket request.");
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    logger.LogInformation("Input WebSocket connected from {RemoteIp}", context.Connection.RemoteIpAddress);

    var buffer = new byte[16 * 1024];

    while (!context.RequestAborted.IsCancellationRequested && socket.State == WebSocketState.Open)
    {
        WebSocketReceiveResult result;
        using var ms = new MemoryStream();

        do
        {
            result = await socket.ReceiveAsync(buffer, context.RequestAborted);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", context.RequestAborted);
                logger.LogInformation("Input WebSocket closed.");
                return;
            }

            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        if (result.MessageType != WebSocketMessageType.Text)
        {
            continue;
        }

        var json = Encoding.UTF8.GetString(ms.ToArray());

        try
        {
            var input = JsonSerializer.Deserialize<InputMessage>(json, jsonOptions);
            if (input is not null)
            {
                await inputRouter.HandleAsync(input, context.RequestAborted);
            }

            var ack = Encoding.UTF8.GetBytes("""{"ok":true}""");
            await socket.SendAsync(ack, WebSocketMessageType.Text, true, context.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid input message: {Json}", json);
            var err = Encoding.UTF8.GetBytes("""{"ok":false}""");
            await socket.SendAsync(err, WebSocketMessageType.Text, true, context.RequestAborted);
        }
    }
});

app.MapGet("/stream/test.mjpeg", async (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Expires = "0";
    context.Response.ContentType = "multipart/x-mixed-replace; boundary=pc2pad";

    var frame = 0L;
    var cancellationToken = context.RequestAborted;

    while (!cancellationToken.IsCancellationRequested)
    {
        var jpg = TestCardRenderer.RenderJpeg(frame++);

        await context.Response.WriteAsync("--pc2pad\r\n", cancellationToken);
        await context.Response.WriteAsync("Content-Type: image/jpeg\r\n", cancellationToken);
        await context.Response.WriteAsync($"Content-Length: {jpg.Length}\r\n\r\n", cancellationToken);
        await context.Response.Body.WriteAsync(jpg, cancellationToken);
        await context.Response.WriteAsync("\r\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(33), cancellationToken);
    }
});

app.Logger.LogInformation("PC2Pad Host startet auf http://{BindAddress}:{Port}", bindAddress, port);
app.Run();

List<GameEntry> LoadGames()
{
    var path = Path.Combine(AppContext.BaseDirectory, "games.json");

    if (!File.Exists(path))
    {
        path = Path.Combine(Directory.GetCurrentDirectory(), "games.json");
    }

    if (!File.Exists(path))
    {
        return [];
    }

    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<List<GameEntry>>(json, jsonOptions) ?? [];
}
