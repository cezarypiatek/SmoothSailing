using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SmoothSailing;

internal static class ProcessLauncherExtensions
{
    public static async Task<string> ExecuteToEnd(this ProcessLauncher @this, string command, string parameters, bool mute, CancellationToken token, bool preserveLineEnding = false)
    {
        var outputBuilder = new StringBuilder();
        await foreach (var line in @this.Execute(command, parameters, mute, token))
        {
            if (preserveLineEnding)
                outputBuilder.AppendLine(line);    
            else outputBuilder.Append(line);
        }
        return outputBuilder.ToString();
    }
}
