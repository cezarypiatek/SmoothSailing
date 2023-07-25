using System.Collections.Generic;
using System.Threading;

namespace SmoothSailing;

internal interface IProcessLauncher
{
    IAsyncEnumerable<string> Execute(string command, string parameters, CancellationToken token);
}
