using System.Collections.Generic;

namespace SmoothSailing;

public class ChartFromRepository : IChart
{
    private readonly HelmRepository _repository;
    private readonly string _chartName;
    private readonly string? _version;

    public ChartFromRepository(HelmRepository repository, string chartName, string? version = null)
    {
        _repository = repository;
        _chartName = chartName;
        _version = version;
    }

    public void ApplyInstallParameters(IList<string> parameters)
    {
        if (string.IsNullOrWhiteSpace(_repository.Url) == false)
        {
            parameters.Add($"--repo {_repository.Url}");
            if (string.IsNullOrWhiteSpace(_repository.Login) == false)
            {
                parameters.Add($"--username \"{_repository.Login}\"");
                parameters.Add($"--password \"{_repository.Password}\"");
            }
        }
        
        if (_version != null)
        {
            parameters.Add($"--version {_version}");
        }
        parameters.Add(_chartName);
    }
}
