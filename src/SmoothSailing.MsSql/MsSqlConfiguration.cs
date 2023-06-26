namespace SmoothSailing.MsSql;

using System.Text.Json.Serialization;

public class MsSqlConfiguration
{
    /// <summary>
    /// The SQL image to be downloaded and used for container deployment.
    /// </summary>
    [JsonPropertyName("image.repository")]
    public string ImageRepository { get; set; } = "mcr.microsoft.com/mssql/server";

    /// <summary>
    /// The tag of the image to be downloaded for the specific SQL image.
    /// </summary>
    [JsonPropertyName("image.tag")]
    public string ImageTag { get; set; } = "2019-latest";

    /// <summary>
    /// Set the ACCEPT_EULA variable to any value to confirm your acceptance of the SQL Server EULA. Please refer to the environment variable for more details.
    /// </summary>
    [JsonPropertyName("ACCEPT_EULA.value")]
    public string AcceptEulaValue { get; set; } = "Y";

    /// <summary>
    /// Set the SQL Server edition or product key. Please refer to the environment variable for more details.
    /// </summary>
    [JsonPropertyName("MSSQL_PID.value")]
    public string MssqlPidValue { get; set; } = "Developer";

    /// <summary>
    /// Enable SQL Server Agent. For example, 'true' is enabled and 'false' is disabled. By default, the agent is disabled. Please refer to the environment variable for more details.
    /// </summary>
    [JsonPropertyName("MSSQL_AGENT_ENABLED.value")]
    public bool MssqlAgentEnabledValue { get; set; } = true;

    /// <summary>
    /// The name that you would like to see when you run the select @@servername for the SQL instance running inside the container.
    /// </summary>
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = "mssqllatest";

    /// <summary>
    /// Configure the SA user password.
    /// </summary>
    [JsonPropertyName("sa_password")]
    public string SaPassword { get; set; } = "StrongPass1!";

    /// <summary>
    /// Port on which the SQL Server is listening inside the container.
    /// </summary>
    [JsonPropertyName("containers.ports.containerPort")]
    public int ContainersPortsContainerPort { get; set; } = 1433;

    /// <summary>
    /// The type of the service to be created within the Kubernetes cluster.
    /// </summary>
    [JsonPropertyName("service.type")]
    public string ServiceType { get; set; } = "LoadBalancer";

    /// <summary>
    /// The service port number.
    /// </summary>
    [JsonPropertyName("service.port")]
    public int ServicePort { get; set; } = 1433;
   
}