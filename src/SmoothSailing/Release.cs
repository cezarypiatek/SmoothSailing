using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SmoothSailing;

public class Release : IAsyncDisposable
{
    public string DeploymentName { get; }
    private readonly ProcessLauncher _processExecutor;
    private readonly KubernetesContext? _kubernetesContext;
    private readonly List<(Task, CancellationTokenSource)> _portForwards = new();

    internal Release(string deploymentName, ProcessLauncher processExecutor, KubernetesContext? kubernetesContext)
    {
        DeploymentName = deploymentName;
        _processExecutor = processExecutor;
        _kubernetesContext = kubernetesContext;
    }

    /// <summary>
    /// Starts port forwarding for a service.
    /// </summary>
    /// <param name="serviceName">The name of the service.</param>
    /// <param name="servicePort">The port of the service.</param>
    /// <param name="localPort">The local port to forward to. If null, a random port will be used.</param>
    /// <returns>The local port number that is being forwarded to the service port.</returns>
    public async Task<int> StartPortForwardForService(string serviceName, int servicePort, int? localPort = null) 
        => await StartPortForwardFor("service", serviceName, servicePort, localPort);
    
    /// <summary>
    /// Starts port forwarding for a pod.
    /// </summary>
    /// <param name="serviceName">The name of the pod.</param>
    /// <param name="servicePort">The port of the pod.</param>
    /// <param name="localPort">The local port to forward to. If null, a random port will be used.</param>
    /// <returns>The local port number that is being forwarded to the pod port.</returns>
    public async Task<int> StartPortForwardForPod(string serviceName, int servicePort, int? localPort = null) 
        => await StartPortForwardFor("pod", serviceName, servicePort, localPort);

    private async Task<int> StartPortForwardFor(string elementType, string elementName, int servicePort, int? localPort)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var portForwardParameters = new KubectlCommandParameterBuilder();
        portForwardParameters.ApplyContextInfo(_kubernetesContext);
        var asyncEnumerable = _processExecutor.Execute("kubectl", $"port-forward {elementType}/{elementName} {localPort}:{servicePort} {portForwardParameters.Build()}", mute:false,  cancellationTokenSource.Token);

        var enumerator = asyncEnumerable.GetAsyncEnumerator(default);
        await enumerator.MoveNextAsync();
        if (enumerator.Current.StartsWith("Forwarding from"))
        {
            _portForwards.Add((ReadToEnd(enumerator), cancellationTokenSource));
            return ExtractPortNumber(enumerator.Current);
        }

        await ReadToEnd(enumerator);
        return 0;
    }

    
    /// <summary>
    /// Executes a command using kubectl exec on all pods that match the specified selector.
    /// </summary>
    /// <param name="command">The command to execute on each pod.</param>
    /// <param name="podSelector">An optional selector to filter the pods. If not provided, defaults to the deployment name. </param>
    public async Task ExecuteCommandOnAllPods(string command, string? podSelector = null)
    {
        var podNames = await GetPodNames(podSelector);
        var execParameters = new KubectlCommandParameterBuilder();
        execParameters.ApplyContextInfo(_kubernetesContext);
        foreach (var podName in podNames)
        {
            await _processExecutor.ExecuteToEnd("kubectl", $"exec pod/{podName} {execParameters.Build()} -- {command}", mute: false, default);
        }
    }
    
    private async Task<IReadOnlyList<string>> GetPodNames(string? podSelector = null)
    {
        podSelector ??= $"app.kubernetes.io/instance={DeploymentName}";
        var getPodsParameters = new KubectlCommandParameterBuilder();
        getPodsParameters.Add($"-l {podSelector}");
        getPodsParameters.Add($"-o jsonpath='{{.items[*].metadata.name}}");
        getPodsParameters.ApplyContextInfo(_kubernetesContext);
        var output = await _processExecutor.ExecuteToEnd("kubectl", $"get pods {getPodsParameters.Build()}", mute: false, default);
        return output.Split(new[] { '\'', ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }
    
    private static int ExtractPortNumber(string input)
    {
        var pattern = @":(\d+) ->";
        var match = Regex.Match(input, pattern);

        if (match.Success)
        {
            var portNumber = match.Groups[1].Value;
            return int.Parse(portNumber);
        }

        throw new ArgumentException("Invalid input. No port number found.");
    }

    private async Task ReadToEnd(IAsyncEnumerator<string> enumerator)
    {
        while (await enumerator.MoveNextAsync()){}
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (var (_, cts) in _portForwards)
            {
                cts.Cancel();
            }

            await Task.WhenAll(_portForwards.Select(x => x.Item1).ToArray());
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        var uninstallParameters = new HelmCommandParameterBuilder();
        uninstallParameters.ApplyContextInfo(_kubernetesContext);
        uninstallParameters.Add("--wait");
        await _processExecutor.ExecuteToEnd("helm", $"uninstall {DeploymentName} {uninstallParameters.Build()}", mute: false, default);
    }
}
