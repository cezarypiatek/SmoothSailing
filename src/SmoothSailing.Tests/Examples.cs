using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SmoothSailing.MsSql;

namespace SmoothSailing.Tests;

public class Examples
{
    public Examples()
    {
        ChartInstaller.DefaultOutputWriter = new NUnitOutputWriter();
    }

    [Test]
    public async Task Download_chart()
    {
        //INFO: Register the repository locally first
        // > helm repo add bitnami https://charts.bitnami.com/bitnami
        var chart = await CachedChartFromRepository.CreateAsync(new HelmRepository("https://charts.bitnami.com/bitnami", useLocallyRegistered: true), "nginx", "19.0.2");
        
        var chartInstaller = new ChartInstaller();
        await using var release = await chartInstaller.Install
        (
            chart: chart,
            releaseName: "samplenginx"
        );
    }

    [Test]
    public async Task install_mssql()
    {
        
        var chartInstaller = new ChartInstaller();
        await using var release = await chartInstaller.Install
        (
            chart: new ChartFromLocalPath("./charts/mssql"),
            releaseName: "samplerelease",
            overrides: new MsSqlConfiguration
            {
                ServicePort = 1433,
                SaPassword = "StrongPass1!"
            },
            timeout: TimeSpan.FromMinutes(2)
        );

        var localPort= await release.StartPortForwardForService("samplerelease-mssql-latest", servicePort: 1433);
        Console.WriteLine($"SqlServer available at {localPort}");
    }
}

class NUnitOutputWriter:IProcessOutputWriter
{
    public void Write(string text) => TestContext.Progress.WriteLine(text);

    public void WriteError(string text) => TestContext.Progress.WriteLine(text);
}