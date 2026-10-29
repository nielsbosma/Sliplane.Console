namespace Sliplane.Console.Infrastructure;

/// <summary>
/// Whether a person is at a terminal. Prompts happen only then: an agent or a script runs with
/// redirected streams and must never sit waiting on a question nobody sees.
/// </summary>
public static class Interactive
{
    public static bool IsTerminal =>
        !System.Console.IsInputRedirected &&
        !System.Console.IsOutputRedirected &&
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));
}
