using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SmoothSailing;

public class HelmRepository
{
    public string Url { get; }
    public string? Login { get; }
    public string? Password { get; }
    public bool UseLocallyRegistered { get; }

    private readonly SemaphoreSlim _repoUpdateSemaphore = new SemaphoreSlim(1, 1);
    private readonly ConcurrentDictionary<string, string> _versionCache = new();
    private bool _repoUpdated = false;

    public HelmRepository(string url, string? login = null, string? password = null, bool useLocallyRegistered = false)
    {
        Url = url;
        Login = login;
        Password = password;
        UseLocallyRegistered = useLocallyRegistered;
    }

    public async Task<string> ResolveLatestVersion(string chartName)
    {
        if (_versionCache.TryGetValue(chartName, out var cached))
            return cached;

        var processLauncher = new ProcessLauncher(ChartInstaller.DefaultOutputWriter);
        var parameters = new HelmCommandParameterBuilder();

        if (this.UseLocallyRegistered)
        {
            var repoListResponse = await processLauncher.ExecuteToEnd("helm", "repo list -o json", mute: true, default);
            if (string.IsNullOrWhiteSpace(repoListResponse))
            {
                throw new InvalidOperationException($"Cannot get list of local repositories. Details: {repoListResponse}");
            }

            var repoList = JsonConvert.DeserializeObject<List<RepoListItem>>(repoListResponse)!;
            var expectedRepo = repoList.FirstOrDefault(r => r.Url == this.Url);
            if (expectedRepo == null)
            {
                throw new InvalidOperationException($"Provided helm repository is not registered in the helm client. Execute 'helm repo add {this.Url}' to register it.");
            }

            parameters.Add($"{expectedRepo.Name}/{chartName}");

            await _repoUpdateSemaphore.WaitAsync();
            try
            {
                if (!_repoUpdated)
                {
                    _ = await processLauncher.ExecuteToEnd("helm", "repo update", mute: false, default);
                    _repoUpdated = true;
                }
            }
            finally
            {
                _repoUpdateSemaphore.Release();
            }
        }
        else
        {
            parameters.Add($"{chartName}");
            parameters.Add($"--repo {this.Url}");
            if (string.IsNullOrWhiteSpace(this.Login) == false)
            {
                parameters.Add($"--username \"{this.Login}\"");
                parameters.Add($"--password \"{this.Password}\"");
            }
        }

        var result = await processLauncher.ExecuteToEnd("helm", $"show chart {parameters.Build()}", mute: true, token: default, preserveLineEnding: true);
        foreach (var line in result.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                if (string.Equals(parts[0].Trim(), "version", StringComparison.InvariantCultureIgnoreCase))
                {
                    var version = parts[1].Trim();
                    _versionCache.TryAdd(chartName, version);
                    return version;
                }
            }
        }

        throw new InvalidOperationException($"Unable to find chart {chartName}. Details: {result}");
    }
}