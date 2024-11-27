using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SmoothSailing;

internal static class DnsHelper
{
    public static async Task WaitForDnsAvailability(string hostName, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                try
                {
                    await Dns.GetHostAddressesAsync(hostName);
                    return;
                }
                catch (SocketException)
                {
                    await Task.Delay(500, cts.Token);
                }
            }

        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"The DNS address {hostName} could not be resolved within the timeout period");
        }
    }
}
