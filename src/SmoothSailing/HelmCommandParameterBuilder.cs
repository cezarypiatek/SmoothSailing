using System.Collections.Generic;
using System.Linq;

namespace SmoothSailing;

internal class HelmCommandParameterBuilder
{
    internal readonly List<string> _parameters;

    public HelmCommandParameterBuilder(IReadOnlyList<string>? parameters = null)
    {
        _parameters = parameters?.ToList() ?? new List<string>();
    }

    public void Add(string parameter) => _parameters.Add(parameter);

    public void ApplyContextInfo(KubernetesContext? context)
    {
        if (context == null)
            return ;
            
        if (context.BurstLimit.HasValue)
            _parameters.Add($"--burst-limit \"{context.BurstLimit.Value}\"");

        if (context.Debug.HasValue && context.Debug.Value)
            _parameters.Add("--debug");

        if (!string.IsNullOrEmpty(context.APIServer))
            _parameters.Add($"--kube-apiserver \"{context.APIServer}\"");

        if (context.AsGroup != null && context.AsGroup.Length > 0)
        {
            foreach (string group in context.AsGroup)
                _parameters.Add($"--kube-as-group \"{group}\"");
        }

        if (!string.IsNullOrEmpty(context.AsUser))
            _parameters.Add($"--kube-as-user \"{context.AsUser}\"");

        if (!string.IsNullOrEmpty(context.CAFile))
            _parameters.Add($"--kube-ca-file \"{context.CAFile}\"");

        if (!string.IsNullOrEmpty(context.Context))
            _parameters.Add($"--kube-context \"{context.Context}\"");

        if (context.InsecureSkipTLSVerify.HasValue && context.InsecureSkipTLSVerify.Value)
            _parameters.Add("--kube-insecure-skip-tls-verify");

        if (!string.IsNullOrEmpty(context.TLSServerName))
            _parameters.Add($"--kube-tls-server-name \"{context.TLSServerName}\"");

        if (!string.IsNullOrEmpty(context.Token))
            _parameters.Add($"--kube-token \"{context.Token}\"");

        if (!string.IsNullOrEmpty(context.KubeConfig))
            _parameters.Add($"--kubeconfig \"{context.KubeConfig}\"");

        if (!string.IsNullOrEmpty(context.Namespace))
            _parameters.Add($"-n \"{context.Namespace}\"");
    }

    public string Build() => string.Join(" ", _parameters);
}
