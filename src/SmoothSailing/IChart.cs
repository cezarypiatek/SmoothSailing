using System.Collections.Generic;

namespace SmoothSailing;

public interface IChart
{
    void ApplyInstallParameters(IList<string> parameters);
}
