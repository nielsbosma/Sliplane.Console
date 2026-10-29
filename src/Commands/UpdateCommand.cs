using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Sliplane.Console.Infrastructure;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands;

public sealed class VersionCommand : Command<LocalSettings>
{
    public override int Execute(CommandContext context, LocalSettings settings)
    {
        settings.ApplyOutputMode();
        Output.WriteObject(new Dictionary<string, object?>
        {
            ["version"] = UpdateCommand.Current(),
            ["runtime"] = Environment.Version.ToString(),
            ["path"] = Environment.ProcessPath
        });
        return 0;
    }
}

/// <summary>
/// Updates the installed global tool from NuGet. A source build installed with a version NuGet
/// does not have yet is left alone: 'newer' is decided by version number, never by date.
/// </summary>
public sealed class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    public sealed class Settings : LocalSettings
    {
        [CommandOption("--check")]
        [Description("Only report whether a newer version exists")]
        public bool Check { get; init; }
    }

    public static string Current()
    {
        var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        return info.Split('+')[0];
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        settings.ApplyOutputMode();
        var current = Current();
        var latest = await LatestAsync();
        var newer = Version.TryParse(latest, out var l) && Version.TryParse(current, out var c) && l > c;

        if (settings.Check || !newer)
        {
            Output.WriteObject(new Dictionary<string, object?>
            {
                ["current"] = current, ["latest"] = latest, ["updateAvailable"] = newer,
                ["command"] = newer ? "sliplane update" : null
            });
            return 0;
        }

        System.Console.Error.WriteLine($"Updating {current} -> {latest}...");
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (var a in new[] { "tool", "update", "--global", "Sliplane.Console", "--version", latest }) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new SliplaneException(ErrorCode.Error, "dotnet tool update failed.",
                remediation: $"dotnet tool update --global Sliplane.Console --version {latest}");

        Output.WriteObject(new Dictionary<string, object?> { ["updated"] = true, ["from"] = current, ["to"] = latest });
        return 0;
    }

    private static async Task<string> LatestAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync("https://api.nuget.org/v3-flatcontainer/sliplane.console/index.json"));
            return doc.RootElement.GetProperty("versions").EnumerateArray()
                .Select(v => v.GetString()!)
                .Where(v => !v.Contains('-'))
                .OrderBy(v => Version.TryParse(v, out var p) ? p : new Version(0, 0))
                .Last();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SliplaneException(ErrorCode.Network, "Could not reach NuGet to check for updates.", ex.Message);
        }
    }
}
