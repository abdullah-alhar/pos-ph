using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace VetPos.AI;

public static class AiServiceCollectionExtensions
{
    /// <summary>Registers the assistant. The app registers its own <see cref="IAiKeyProtector"/> (DPAPI).</summary>
    public static IServiceCollection AddVetPosAi(this IServiceCollection services)
    {
        services.AddSingleton(_ => new AiClient(CreateHttpClient()));
        services.AddSingleton<AssistantActions>();
        services.AddSingleton<AssistantTools>();
        services.AddSingleton<AssistantService>();
        services.AddSingleton<ReceiptAssistant>();
        services.AddSingleton<ProductAssistant>();
        return services;
    }

    /// <summary>
    /// Shop PCs, virtual machines and mobile hotspots often have broken IPv6: connecting to an IPv6
    /// address then hangs until it times out, and the AI looks "too slow". Connect over IPv4 first,
    /// give each address a few seconds, and only then try IPv6.
    /// </summary>
    public static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = ConnectIPv4FirstAsync,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
    }

    private static async ValueTask<Stream> ConnectIPv4FirstAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        var ordered = addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToList();
        Exception? last = null;
        foreach (var address in ordered)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), attempt.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw new HttpRequestException($"Could not connect to {context.DnsEndPoint.Host}.", last);
    }
}
