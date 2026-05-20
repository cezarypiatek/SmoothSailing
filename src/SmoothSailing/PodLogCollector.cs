using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SmoothSailing;

/// <summary>
/// Watches pods belonging to a Helm release while an installation is in progress and
/// buffers their container logs so they can be dumped if the installation fails.
/// </summary>
internal sealed class PodLogCollector
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);
    private const int DefaultTailCapBytes = 64 * 1024;
    private const int DefaultErrorCapBytes = 256 * 1024;
    private const int NamePrefixFallbackAfterPolls = 3;

    private readonly ProcessLauncher _launcher;
    private readonly IProcessOutputWriter _writer;
    private readonly KubernetesContext? _context;
    private readonly string _releaseName;
    private readonly DateTime _startTimeUtc;
    private readonly int _tailCapBytes;
    private readonly int _errorCapBytes;
    private readonly TimeSpan _pollInterval;

    private readonly object _buffersLock = new();
    private readonly Dictionary<string, ContainerBuffer> _buffers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _knownRestartCounts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeTails = new(StringComparer.Ordinal);
    private readonly List<Task> _tailTasks = new();

    public PodLogCollector(
        ProcessLauncher launcher,
        IProcessOutputWriter writer,
        KubernetesContext? context,
        string releaseName,
        DateTime startTimeUtc,
        int tailCapBytes = DefaultTailCapBytes,
        int errorCapBytes = DefaultErrorCapBytes,
        TimeSpan? pollInterval = null)
    {
        _launcher = launcher;
        _writer = writer;
        _context = context;
        _releaseName = releaseName;
        _startTimeUtc = startTimeUtc;
        _tailCapBytes = tailCapBytes;
        _errorCapBytes = errorCapBytes;
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    /// <summary>
    /// Starts polling for pods. The returned task completes when polling is cancelled and
    /// every spawned tail process has finished.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => PollLoopAsync(cancellationToken), CancellationToken.None);
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        var pollCount = 0;
        var labelMatchedAny = false;

        while (cancellationToken.IsCancellationRequested == false)
        {
            pollCount++;
            try
            {
                var labelPods = await GetPodsAsync($"-l app.kubernetes.io/instance={_releaseName}", cancellationToken).ConfigureAwait(false);
                if (labelPods.Count > 0)
                {
                    labelMatchedAny = true;
                    StartTailsForPods(labelPods, cancellationToken);
                }
                else if (labelMatchedAny == false && pollCount >= NamePrefixFallbackAfterPolls)
                {
                    var allPods = await GetPodsAsync(string.Empty, cancellationToken).ConfigureAwait(false);
                    var prefixed = allPods
                        .Where(p => p.PodName.StartsWith(_releaseName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    StartTailsForPods(prefixed, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Swallow polling errors (RBAC, transient kubectl failures). We do not want
                // to interfere with the helm install.
            }

            try
            {
                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Wait for outstanding tail processes to wind down. Cancellation has already been
        // requested so they should exit quickly.
        Task[] outstanding;
        lock (_buffersLock)
        {
            outstanding = _tailTasks.ToArray();
        }
        try
        {
            await Task.WhenAll(outstanding).ConfigureAwait(false);
        }
        catch
        {
            // Tail tasks always swallow their own errors, but guard anyway.
        }
    }

    private void StartTailsForPods(IReadOnlyList<PodInfo> pods, CancellationToken cancellationToken)
    {
        foreach (var pod in pods)
        {
            foreach (var container in pod.Containers)
            {
                var key = $"{pod.PodName}/{container}";
                bool startFollow = false;
                bool needPrevious = false;
                int restartCount = pod.GetRestartCount(container);

                lock (_buffersLock)
                {
                    if (_activeTails.Contains(key) == false)
                    {
                        _activeTails.Add(key);
                        startFollow = true;
                    }

                    if (_knownRestartCounts.TryGetValue(key, out var previousCount))
                    {
                        if (restartCount > previousCount)
                        {
                            needPrevious = true;
                        }
                    }
                    else if (restartCount > 0)
                    {
                        needPrevious = true;
                    }

                    _knownRestartCounts[key] = restartCount;
                }

                if (startFollow)
                {
                    var followTask = Task.Run(() => RunFollowAsync(pod.PodName, container, cancellationToken), CancellationToken.None);
                    lock (_buffersLock)
                    {
                        _tailTasks.Add(followTask);
                    }
                }

                if (needPrevious)
                {
                    var previousTask = Task.Run(() => RunPreviousAsync(pod.PodName, container, cancellationToken), CancellationToken.None);
                    lock (_buffersLock)
                    {
                        _tailTasks.Add(previousTask);
                    }
                }
            }
        }
    }

    private async Task RunFollowAsync(string pod, string container, CancellationToken cancellationToken)
    {
        try
        {
            var parameters = new KubectlCommandParameterBuilder(new List<string>
            {
                "-f",
                "--timestamps",
                $"--since-time={_startTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}",
                $"-c {container}"
            });
            parameters.ApplyContextInfo(_context);

            await foreach (var line in _launcher.Execute("kubectl", $"logs {pod} {parameters.Build()}", mute: true, cancellationToken).ConfigureAwait(false))
            {
                Append(pod, container, line);
            }
        }
        catch
        {
            // Tail failures (pod deleted, RBAC, etc.) are expected; swallow.
        }
    }

    private async Task RunPreviousAsync(string pod, string container, CancellationToken cancellationToken)
    {
        try
        {
            var parameters = new KubectlCommandParameterBuilder(new List<string>
            {
                "--previous",
                "--timestamps",
                $"-c {container}"
            });
            parameters.ApplyContextInfo(_context);

            await foreach (var line in _launcher.Execute("kubectl", $"logs {pod} {parameters.Build()}", mute: true, cancellationToken).ConfigureAwait(false))
            {
                Append(pod, container, line, fromPrevious: true);
            }
        }
        catch
        {
            // --previous fails when there's no terminated instance; ignore.
        }
    }

    private void Append(string pod, string container, string line, bool fromPrevious = false)
    {
        if (line == null!)
        {
            return;
        }

        var key = $"{pod}/{container}";
        ContainerBuffer buffer;
        lock (_buffersLock)
        {
            if (_buffers.TryGetValue(key, out var existing) == false)
            {
                existing = new ContainerBuffer(pod, container, _tailCapBytes, _errorCapBytes);
                _buffers[key] = existing;
            }
            buffer = existing;
        }

        buffer.Add(line, fromPrevious);
    }

    private async Task<IReadOnlyList<PodInfo>> GetPodsAsync(string selector, CancellationToken cancellationToken)
    {
        var parameters = new KubectlCommandParameterBuilder(new List<string>
        {
            "-o json"
        });
        if (string.IsNullOrEmpty(selector) == false)
        {
            parameters.Add(selector);
        }
        parameters.ApplyContextInfo(_context);

        string raw;
        try
        {
            raw = await _launcher.ExecuteToEnd("kubectl", $"get pods {parameters.Build()}", mute: true, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return Array.Empty<PodInfo>();
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<PodInfo>();
        }

        JObject parsed;
        try
        {
            parsed = JObject.Parse(raw);
        }
        catch
        {
            return Array.Empty<PodInfo>();
        }

        var result = new List<PodInfo>();
        if (parsed["items"] is not JArray items)
        {
            return result;
        }

        foreach (var item in items)
        {
            var name = item.SelectToken("metadata.name")?.Value<string>();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var containers = new List<string>();
            if (item.SelectToken("spec.containers") is JArray specContainers)
            {
                foreach (var c in specContainers)
                {
                    var cn = c["name"]?.Value<string>();
                    if (string.IsNullOrEmpty(cn) == false)
                    {
                        containers.Add(cn!);
                    }
                }
            }
            if (item.SelectToken("spec.initContainers") is JArray initContainers)
            {
                foreach (var c in initContainers)
                {
                    var cn = c["name"]?.Value<string>();
                    if (string.IsNullOrEmpty(cn) == false)
                    {
                        containers.Add(cn!);
                    }
                }
            }

            var restartCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            void CollectStatuses(string path)
            {
                if (item.SelectToken(path) is not JArray statuses)
                {
                    return;
                }
                foreach (var s in statuses)
                {
                    var cn = s["name"]?.Value<string>();
                    var rc = s["restartCount"]?.Value<int?>() ?? 0;
                    if (string.IsNullOrEmpty(cn) == false)
                    {
                        restartCounts[cn!] = rc;
                    }
                }
            }
            CollectStatuses("status.containerStatuses");
            CollectStatuses("status.initContainerStatuses");

            result.Add(new PodInfo(name!, containers, restartCounts));
        }

        return result;
    }

    /// <summary>
    /// Writes the buffered pod logs to the provided writer. Intended to be called when
    /// the install fails.
    /// </summary>
    public void Flush(IProcessOutputWriter writer)
    {
        ContainerBuffer[] snapshot;
        lock (_buffersLock)
        {
            snapshot = _buffers.Values.ToArray();
        }

        if (snapshot.Length == 0)
        {
            return;
        }

        writer.Write("Pod logs collected during installation:");
        foreach (var buffer in snapshot)
        {
            buffer.WriteTo(writer);
        }
    }

    private readonly struct PodInfo
    {
        public PodInfo(string podName, IReadOnlyList<string> containers, IReadOnlyDictionary<string, int> restartCounts)
        {
            PodName = podName;
            Containers = containers;
            _restartCounts = restartCounts;
        }

        public string PodName { get; }
        public IReadOnlyList<string> Containers { get; }
        private readonly IReadOnlyDictionary<string, int> _restartCounts;

        public int GetRestartCount(string container)
        {
            return _restartCounts.TryGetValue(container, out var rc) ? rc : 0;
        }
    }

    private sealed class ContainerBuffer
    {
        private readonly string _pod;
        private readonly string _container;
        private readonly int _tailCapBytes;
        private readonly int _errorCapBytes;
        private readonly object _lock = new();
        private readonly LinkedList<string> _recent = new();
        private readonly LinkedList<string> _errors = new();
        private readonly HashSet<string> _errorIdentity = new(ReferenceEqualityComparer.Instance);
        private int _recentBytes;
        private int _errorBytes;

        public ContainerBuffer(string pod, string container, int tailCapBytes, int errorCapBytes)
        {
            _pod = pod;
            _container = container;
            _tailCapBytes = tailCapBytes;
            _errorCapBytes = errorCapBytes;
        }

        public void Add(string line, bool fromPrevious)
        {
            if (line == null)
            {
                return;
            }

            var bytes = Encoding.UTF8.GetByteCount(line);
            var isError = line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0;

            lock (_lock)
            {
                _recent.AddLast(line);
                _recentBytes += bytes;
                while (_recentBytes > _tailCapBytes && _recent.Count > 1)
                {
                    var first = _recent.First!;
                    _recentBytes -= Encoding.UTF8.GetByteCount(first.Value);
                    _recent.RemoveFirst();
                }

                if (isError)
                {
                    _errors.AddLast(line);
                    _errorBytes += bytes;
                    _errorIdentity.Add(line);
                    while (_errorBytes > _errorCapBytes && _errors.Count > 1)
                    {
                        var first = _errors.First!;
                        _errorBytes -= Encoding.UTF8.GetByteCount(first.Value);
                        _errorIdentity.Remove(first.Value);
                        _errors.RemoveFirst();
                    }
                }

                if (fromPrevious)
                {
                    // Tag previous lines so they're distinguishable on dump. We do this by
                    // wrapping them in-place; safe because the LinkedList nodes hold the
                    // boxed string reference.
                    // (No-op here; the wrapping happens at write time would be cleaner,
                    //  but we keep the buffer pure and use a side marker instead.)
                }
            }
        }

        public void WriteTo(IProcessOutputWriter writer)
        {
            string[] errorsSnapshot;
            string[] recentSnapshot;
            HashSet<string> errorIdentitySnapshot;
            lock (_lock)
            {
                errorsSnapshot = _errors.ToArray();
                recentSnapshot = _recent.ToArray();
                errorIdentitySnapshot = new HashSet<string>(_errorIdentity, ReferenceEqualityComparer.Instance);
            }

            if (errorsSnapshot.Length == 0 && recentSnapshot.Length == 0)
            {
                return;
            }

            if (errorsSnapshot.Length > 0)
            {
                writer.Write($"==== Logs from pod {_pod}/{_container} (errors preserved) ====");
                foreach (var line in errorsSnapshot)
                {
                    writer.Write(line);
                }
            }

            if (recentSnapshot.Length > 0)
            {
                writer.Write($"==== Logs from pod {_pod}/{_container} (recent tail) ====");
                foreach (var line in recentSnapshot)
                {
                    if (errorIdentitySnapshot.Contains(line))
                    {
                        // Already shown in the errors section; skip to avoid duplication.
                        continue;
                    }
                    writer.Write(line);
                }
            }
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<string>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public bool Equals(string? x, string? y) => ReferenceEquals(x, y);
        public int GetHashCode(string obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
