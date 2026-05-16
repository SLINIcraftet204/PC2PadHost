using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PC2Pad.Host.Discovery;

public sealed class DiscoveryHostedService : BackgroundService
{
    public const string ProbeText = "PC2PAD_DISCOVERY_V1";

    private readonly IConfiguration _configuration;
    private readonly ILogger<DiscoveryHostedService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public DiscoveryHostedService(
        IConfiguration configuration,
        ILogger<DiscoveryHostedService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue<bool?>("PC2Pad:Discovery:Enabled") ?? true;
        if (!enabled)
        {
            _logger.LogInformation("PC2Pad LAN discovery is disabled.");
            return;
        }

        var discoveryPort = _configuration.GetValue<int?>("PC2Pad:Discovery:Port") ?? 8129;
        var hostPort = _configuration.GetValue<int?>("PC2Pad:Port") ?? 8128;

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));

        _logger.LogInformation("PC2Pad LAN discovery listens on UDP {DiscoveryPort}.", discoveryPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PC2Pad discovery receive failed.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            var text = Encoding.UTF8.GetString(result.Buffer).Trim();
            if (!text.Equals(ProbeText, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var localAddress = ResolveLocalAddressFor(result.RemoteEndPoint.Address);
            var baseUrl = $"http://{localAddress}:{hostPort}";
            var response = JsonSerializer.Serialize(new
            {
                protocol = "pc2pad.discovery.v1",
                name = "PC2Pad Host",
                machineName = Environment.MachineName,
                version = "0.2.0-lan-input",
                baseUrl,
                httpPort = hostPort,
                inputWebSocket = $"ws://{localAddress}:{hostPort}/ws/input"
            }, _jsonOptions);

            var bytes = Encoding.UTF8.GetBytes(response);
            try
            {
                await udp.SendAsync(bytes, result.RemoteEndPoint, stoppingToken);
                _logger.LogInformation(
                    "Answered PC2Pad discovery from {RemoteEndPoint} with {BaseUrl}.",
                    result.RemoteEndPoint,
                    baseUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PC2Pad discovery response failed.");
            }
        }
    }

    private static string ResolveLocalAddressFor(IPAddress remoteAddress)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(remoteAddress, 9);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString();
            }
        }
        catch
        {
            // Fall through to first usable IPv4.
        }

        var firstUsable = Dns.GetHostEntry(Dns.GetHostName())
            .AddressList
            .FirstOrDefault(address =>
                address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address));

        return firstUsable?.ToString() ?? "127.0.0.1";
    }
}
