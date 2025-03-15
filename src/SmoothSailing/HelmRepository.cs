namespace SmoothSailing;

public class HelmRepository
{
    public string Url { get; }
    public string? Login { get; }
    public string? Password { get; }
    public bool UseLocallyRegistered { get; }

    public HelmRepository(string url, string? login = null, string? password = null, bool useLocallyRegistered = false)
    {
        Url = url;
        Login = login;
        Password = password;
        UseLocallyRegistered = useLocallyRegistered;
    }
}