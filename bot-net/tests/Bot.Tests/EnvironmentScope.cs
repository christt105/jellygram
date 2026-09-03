namespace Bot.Tests;

/// <summary>
/// Sets environment variables for the duration of a test and restores their previous values,
/// so a test that exercises configuration cannot leak into the rest of the run.
/// </summary>
public sealed class EnvironmentScope : IDisposable
{
    private readonly Dictionary<string, string?> _previous = new();

    public EnvironmentScope(Dictionary<string, string?> values)
    {
        foreach (var (name, value) in values)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _previous)
            Environment.SetEnvironmentVariable(name, value);
    }
}
