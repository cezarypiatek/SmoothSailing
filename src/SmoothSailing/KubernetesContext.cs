using System;
using System.Diagnostics.Contracts;
using System.Threading.Tasks;

namespace SmoothSailing;

public class KubernetesContext
{
    /// <summary>
    /// Client-side default throttling limit (default 100).
    /// </summary>
    public int? BurstLimit { get; set; }

    /// <summary>
    /// Enable verbose output.
    /// </summary>
    public bool? Debug { get; set; }

    /// <summary>
    /// The address and the port for the Kubernetes API server.
    /// </summary>
    public string? APIServer { get; set; }

    /// <summary>
    /// Group to impersonate for the operation. This flag can be repeated to specify multiple groups.
    /// </summary>
    public string[]? AsGroup { get; set; }

    /// <summary>
    /// Username to impersonate for the operation.
    /// </summary>
    public string? AsUser { get; set; }

    /// <summary>
    /// The certificate authority file for the Kubernetes API server connection.
    /// </summary>
    public string? CAFile { get; set; }

    /// <summary>
    /// Name of the kubeconfig context to use.
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// If true, the Kubernetes API server's certificate will not be checked for validity.
    /// This will make your HTTPS connections insecure.
    /// </summary>
    public bool? InsecureSkipTLSVerify { get; set; }

    /// <summary>
    /// Server name to use for Kubernetes API server certificate validation.
    /// If it is not provided, the hostname used to contact the server is used.
    /// </summary>
    public string? TLSServerName { get; set; }

    /// <summary>
    /// Bearer token used for authentication.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Path to the kubeconfig file.
    /// </summary>
    public string? KubeConfig { get; set; }

    /// <summary>
    /// Namespace scope for this request.
    /// </summary>
    public string? Namespace { get; set; }

    public string ResolveServiceAddress(string serviceName) =>
        string.IsNullOrWhiteSpace(serviceName)
            ? $"{serviceName}.default.svc.cluster.local"
            : $"{serviceName}.{Namespace}.svc.cluster.local";

    /// <summary>
    /// Resolve service address and ensure that DNS is available
    /// </summary>
    public async Task<string> ResolveWorkingServiceAddress(string serviceName, TimeSpan timeout)
    {
        var serviceAddress = this.ResolveServiceAddress(serviceName);
        await DnsHelper.WaitForDnsAvailability(serviceName, timeout);
        return serviceAddress;
    }
}