using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SmoothSailing;

public class ChartInstaller
{
    public static IProcessOutputWriter DefaultOutputWriter { get; set; } = new ConsoleProcessOutputWriter();
    
    private readonly IProcessLauncher _processLauncher;

    public ChartInstaller(IProcessOutputWriter? processOutputWriter = null)
    {
        _processLauncher = new ProcessLauncher(processOutputWriter ?? DefaultOutputWriter);
    }

    /// <summary>
    ///     Perform helm chart installation
    /// </summary>
    /// <remarks>
    ///     Requires Helm and kubectl to be installed and added to PATH environment variable
    /// </remarks>
    /// <param name="chart"></param>
    /// <param name="releaseName"></param>
    /// <param name="overrides"></param>
    /// <param name="timeout"></param>
    public async Task<Release> Install(IChart chart, string releaseName, object? overrides = null, TimeSpan? timeout = null, KubernetesContext? context = null)
    {
        var filterParameters = new List<string>
        {
            $"--filter {releaseName}",
            "-o json"
        };
        
        if (context is not null)
        {
            ApplyContextInfo(context, filterParameters);
        }
        
        
        var executeToEnd = await _processLauncher.ExecuteToEnd("helm", $"list {string.Join(" ", filterParameters)}", default);
        if (executeToEnd != "[]")
        {
            var uninstallParameters = new List<string>
            {
                "--wait"
            };
        
            if (context is not null)
            {
                ApplyContextInfo(context, uninstallParameters);
            }
            await _processLauncher.ExecuteToEnd("helm", $"uninstall {releaseName} {string.Join(" ", filterParameters)}", default);
        }

        var parameters = new List<string>
        {
            "--install",
            "--force",
            "--atomic",
            "--wait"
        };

        if (timeout.HasValue)
        {
            parameters.Add($"--timeout {timeout.Value.TotalSeconds}s");
        }

        if (context is not null)
        {
            ApplyContextInfo(context, parameters);
        }

        chart.ApplyInstallParameters(parameters);

        if (overrides != null)
        {
            var serializedOverrides = JsonConvert.SerializeObject(overrides);
            var overridesPath = Path.Combine(Path.GetTempPath(), $"{releaseName}.json");
            File.WriteAllText(overridesPath, serializedOverrides, Encoding.UTF8);
            parameters.Add($"-f \"{overridesPath}\"");    
        }

        await _processLauncher.ExecuteToEnd("helm", $"upgrade {releaseName} {string.Join(" ", parameters)}", default);
        return new Release(releaseName, _processLauncher);
    }

    private void ApplyContextInfo(KubernetesContext options, List<string> parameters)
    {
        if (options.BurstLimit.HasValue)
            parameters.Add($"--burst-limit \"{options.BurstLimit.Value}\"");

        if (options.Debug.HasValue && options.Debug.Value)
            parameters.Add("--debug");

        if (!string.IsNullOrEmpty(options.APIServer))
            parameters.Add($"--kube-apiserver \"{options.APIServer}\"");

        if (options.AsGroup != null && options.AsGroup.Length > 0)
        {
            foreach (string group in options.AsGroup)
                parameters.Add($"--kube-as-group \"{group}\"");
        }

        if (!string.IsNullOrEmpty(options.AsUser))
            parameters.Add($"--kube-as-user \"{options.AsUser}\"");

        if (!string.IsNullOrEmpty(options.CAFile))
            parameters.Add($"--kube-ca-file \"{options.CAFile}\"");

        if (!string.IsNullOrEmpty(options.Context))
            parameters.Add($"--kube-context \"{options.Context}\"");

        if (options.InsecureSkipTLSVerify.HasValue && options.InsecureSkipTLSVerify.Value)
            parameters.Add("--kube-insecure-skip-tls-verify");

        if (!string.IsNullOrEmpty(options.TLSServerName))
            parameters.Add($"--kube-tls-server-name \"{options.TLSServerName}\"");

        if (!string.IsNullOrEmpty(options.Token))
            parameters.Add($"--kube-token \"{options.Token}\"");

        if (!string.IsNullOrEmpty(options.KubeConfig))
            parameters.Add($"--kubeconfig \"{options.KubeConfig}\"");

        if (!string.IsNullOrEmpty(options.Namespace))
            parameters.Add($"-n \"{options.Namespace}\"");
    }
}