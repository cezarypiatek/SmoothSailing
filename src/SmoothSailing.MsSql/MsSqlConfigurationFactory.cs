namespace SmoothSailing.MsSql;

public static class MsSqlConfigurationFactory
{
    /// <summary>
    ///     Built on top of helm chart provided by https://github.com/microsoft/mssql-docker/tree/master/linux/sample-helm-chart
    /// </summary>
    public static MsSqlConfiguration CreateDefaultConfiguration()
    {
        return new MsSqlConfiguration
        {
            ImageRepository = "mcr.microsoft.com/mssql/server",
            ImageTag = "2019-latest",
            AcceptEulaValue = "Y",
            MssqlPidValue = "Developer",
            MssqlAgentEnabledValue = true,
            Hostname = "mssqllatest",
            SaPassword = "StrongPass1!",
            ContainersPortsContainerPort = 1433,
            ServiceType = "LoadBalancer",
            ServicePort = 1433
        };
    }
}