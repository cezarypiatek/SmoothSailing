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
        var listCommandParameters = new HelmCommandParameterBuilder(new List<string>
        {
            $"--filter {releaseName}",
            "--deployed",
            "--failed",
            "--uninstalling",
            "-o json"
        });
        
        listCommandParameters.ApplyContextInfo(context);
        
        
        var executeToEnd = await _processLauncher.ExecuteToEnd("helm", $"list {listCommandParameters.Build()}", default);
        if (executeToEnd != "[]")
        {
            var uninstallParameters = new HelmCommandParameterBuilder(new List<string>
            {
                "--wait"
            });
        
            uninstallParameters.ApplyContextInfo(context );
            
            await _processLauncher.ExecuteToEnd("helm", $"uninstall {releaseName} {uninstallParameters.Build()}", default);
        }

        var installParameters = new HelmCommandParameterBuilder(new List<string>
        {
            "--install",
            "--force",
            "--atomic",
            "--wait"
        });

        if (timeout.HasValue)
        {
            installParameters.Add($"--timeout {timeout.Value.TotalSeconds}s");
        }

        installParameters.ApplyContextInfo(context );

        chart.ApplyInstallParameters(installParameters._parameters);

        if (overrides != null)
        {
            var serializedOverrides = JsonConvert.SerializeObject(overrides);
            var overridesPath = Path.Combine(Path.GetTempPath(), $"{releaseName}.json");
            File.WriteAllText(overridesPath, serializedOverrides, Encoding.UTF8);
            installParameters.Add($"-f \"{overridesPath}\"");    
        }

        await _processLauncher.ExecuteToEnd("helm", $"upgrade {releaseName} {installParameters.Build()}", default);
        return new Release(releaseName, _processLauncher, context);
    }
}