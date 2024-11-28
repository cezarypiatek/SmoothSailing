using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SmoothSailing;

public class ChartInstaller
{
    public static IProcessOutputWriter DefaultOutputWriter { get; set; } = new ConsoleProcessOutputWriter();
    
    private readonly ProcessLauncher _processLauncher;
    private readonly IProcessOutputWriter _processOutputWriter;

    public ChartInstaller(IProcessOutputWriter? processOutputWriter = null)
    {
        this._processOutputWriter = processOutputWriter ?? DefaultOutputWriter;
        _processLauncher = new ProcessLauncher(_processOutputWriter);
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
        
        
        var executeToEnd = await _processLauncher.ExecuteToEnd("helm", $"list {listCommandParameters.Build()}", mute: false, default);
        if (executeToEnd != "[]")
        {
            // INFO: try to uninstall previous installation
            var uninstallParameters = new HelmCommandParameterBuilder(new List<string>
            {
                "--wait"
            });
        
            uninstallParameters.ApplyContextInfo(context );
            await _processLauncher.ExecuteToEnd("helm", $"uninstall {releaseName} {uninstallParameters.Build()}", mute: false, default);
        }
        else
        {
            // INFO: Sometimes, for unknown reasons, helm uninstall release but do not update info about uninstall state
            //       This info is kept in secrets. Removing this specific secret should fix things up
            try
            {
                var getSecretsParameters = new KubectlCommandParameterBuilder(new List<string>()
                {
                    "-A",
                    "-o json"
                });
                getSecretsParameters.ApplyContextInfo(context);
                var getSecretsResults = await _processLauncher.ExecuteToEnd("kubectl", $"get secrets {getSecretsParameters.Build()}", mute: true,default);
                if (JObject.Parse(getSecretsResults) is { } secrets)
                {
                    var result =  secrets.SelectTokens($"$.items[?(@.metadata.labels.name == '{releaseName}' && @.kind == 'Secret' && @.metadata.labels.owner == 'helm')].metadata.name").FirstOrDefault();
                    if (result is JValue {Value: string secretName} && string.IsNullOrWhiteSpace(secretName) == false && secretName.StartsWith("sh.helm.release."))
                    {
                        _processOutputWriter.Write($"Detected dangling release by discovering secret '{secretName}");
                        var deleteSecretParameters = new KubectlCommandParameterBuilder(new List<string>());
                        deleteSecretParameters.ApplyContextInfo(context);
                        await _processLauncher.ExecuteToEnd("kubectl", $"delete secrets {secretName} {deleteSecretParameters.Build()}", mute: false,default);
                    }
                }
            }
            catch (Exception e)
            {
                _processOutputWriter.WriteError(e.Message);
            }
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

        await _processLauncher.ExecuteToEnd("helm", $"upgrade {releaseName} {installParameters.Build()}", mute: false, default);
        return new Release(releaseName, _processLauncher, context);
    }
}
