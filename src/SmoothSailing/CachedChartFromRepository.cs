using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SmoothSailing;

public class CachedChartFromRepository : IChart
{
    private readonly string _fileName;

    private CachedChartFromRepository(string fileName)
    {
        _fileName = fileName;
    }

    public void ApplyInstallParameters(IList<string> parameters)
    {
        parameters.Add(_fileName);
    }
    
    public static async Task<IChart> CreateAsync(HelmRepository repository, string chartName, string version)
    {
        var expectedFileName = $"{chartName}-{version}.tgz";
        
        if(File.Exists(expectedFileName) == false)
        {
            var processLauncher = new ProcessLauncher(ChartInstaller.DefaultOutputWriter);
            var pullParameters = await BuildPullParameters(processLauncher, repository, chartName, version);
            async Task TryToDownloadChart()
            {
                await processLauncher.ExecuteToEnd("helm", $"pull {pullParameters.Build()}", mute: false, default);
                if (File.Exists(expectedFileName) == false)
                {
                    throw new InvalidOperationException("Fail to download the chart from the repository.");
                }
            }
            try
            {
                await TryToDownloadChart();
            }
            catch (InvalidOperationException e)
            {
                if (e.Message.Contains("try 'helm repo update'"))
                {
                    _ = await processLauncher.ExecuteToEnd("helm", "repo update", mute: false, default);
                    await TryToDownloadChart();
                }
                else
                {
                    throw;
                }
                
            }
        }
        return new CachedChartFromRepository(expectedFileName);

        
    }
    
    private static async Task<HelmCommandParameterBuilder> BuildPullParameters(ProcessLauncher processLauncher, HelmRepository repository, string chartName, string version)
    {
        var parameters = new HelmCommandParameterBuilder();

        if (repository.UseLocallyRegistered)
        {
            var repoListResponse = await processLauncher.ExecuteToEnd("helm", "repo list -o json", mute: true, default);
            if (string.IsNullOrWhiteSpace(repoListResponse) == false)
            {
                var repoList = JsonConvert.DeserializeObject<List<RepoListItem>>(repoListResponse)!;
                var expectedRepo = repoList.FirstOrDefault(r => r.Url == repository.Url);
                if (expectedRepo == null)
                {
                    throw new InvalidOperationException($"Provided helm repository is not registered in the helm client. Execute 'helm repo add {repository.Url}' to register it.");
                }
                parameters.Add($"--version {version}");
                parameters.Add($"{expectedRepo.Name}/{chartName}");
                return parameters;
            }
        }
        
        parameters.Add($"--repo {repository.Url}");
        if (string.IsNullOrWhiteSpace(repository.Login) == false)
        {
            parameters.Add($"--username \"{repository.Login}\"");
            parameters.Add($"--password \"{repository.Password}\"");
        }
        parameters.Add($"--version {version}");
        parameters.Add(chartName);
        return parameters;
    }

    class RepoListItem
    {
        public string Name { get; set; } = null!;
        public string Url { get; set; }= null!;
    }
}
