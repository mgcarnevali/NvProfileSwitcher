using System.Diagnostics;

namespace GameProfileSwitcher;

public sealed class ProcessWatcher : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 750 };
    private readonly Func<IReadOnlyList<GameProfile>> _profilesProvider;
    private string? _activeProcess;

    public event Action<GameProfile?>? ProfileChanged;

    public ProcessWatcher(Func<IReadOnlyList<GameProfile>> profilesProvider)
    {
        _profilesProvider = profilesProvider;
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void Poll()
    {
        var profiles = _profilesProvider();
        GameProfile? match = null;

        foreach (var profile in profiles.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.ProcessName)))
        {
            try
            {
                if (Process.GetProcessesByName(profile.ProcessName).Length > 0)
                {
                    match = profile;
                    break;
                }
            }
            catch
            {
                // Ignore transient process enumeration failures.
            }
        }

        var newProcess = match?.ProcessName;
        if (string.Equals(newProcess, _activeProcess, StringComparison.OrdinalIgnoreCase))
            return;

        _activeProcess = newProcess;
        ProfileChanged?.Invoke(match);
    }

    public void Dispose() => _timer.Dispose();
}
