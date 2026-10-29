using System.Diagnostics;
using System.Text.Json;

namespace Sliplane.Console.Infrastructure;

/// <summary>
/// One SSH session into a service with a key that exists only for that session.
///
/// The key is generated locally, registered with an expiry (<c>ttl</c>), attached, used, then
/// detached and deleted. The expiry is only the fallback for a CLI that is killed before it can
/// clean up. Uses the system OpenSSH client; Windows ships one in System32\OpenSSH.
///
/// Win32 OpenSSH has no ControlMaster, so readiness is probed with a throwaway connection
/// (<c>exit</c>) instead of a multiplexed master: exit code 255 is ssh's own failure, anything
/// else means the key was accepted.
/// </summary>
public static class SshSession
{
    public sealed record Target(string ProjectId, string ServiceId, string Name);

    public static async Task<int> RunAsync(SliplaneClient client, Target target, TimeSpan ttl,
        IReadOnlyList<string> sessionArgs, string? remoteCommand, bool tty)
    {
        var ssh = FindTool("ssh");
        var keygen = FindTool("ssh-keygen");

        var dir = Directory.CreateTempSubdirectory("sliplane-ssh-");
        string? keyId = null;
        try
        {
            var identity = Path.Combine(dir.FullName, "key");
            var (code, output) = await RunCaptured(keygen, ["-q", "-t", "ed25519", "-N", "", "-C", "sliplane-console", "-f", identity]);
            if (code != 0)
                throw new SliplaneException(ErrorCode.Error, "ssh-keygen failed.", output.Trim());
            var publicKey = (await File.ReadAllTextAsync(identity + ".pub")).Trim();

            var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
            using (var key = await client.PostAsync("ssh-keys", new
                   {
                       name = $"sliplane-console: {target.Name}",
                       publicKey,
                       expiresAt = expiresAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
                   }))
                keyId = key.RootElement.GetProperty("id").GetString();

            using var connection = await client.PutAsync(
                $"projects/{target.ProjectId}/services/{target.ServiceId}/ssh-keys/{keyId}", new { });
            var host = connection.RootElement.GetProperty("host").GetString()!;
            var port = connection.RootElement.GetProperty("port").GetInt32();
            var user = connection.RootElement.GetProperty("username").GetString()!;
            if (host.StartsWith('-') || user.Any(char.IsWhiteSpace))
                throw new SliplaneException(ErrorCode.Error, "The API returned unusable SSH connection details.");

            var baseArgs = new List<string>
            {
                "-F", "none", "-i", identity,
                "-o", "IdentitiesOnly=yes", "-o", "IdentityAgent=none",
                "-o", "StrictHostKeyChecking=accept-new", "-o", "ConnectTimeout=10",
                "-o", "ExitOnForwardFailure=yes", "-o", "ServerAliveInterval=30",
                "-p", port.ToString(), "-l", user
            };

            System.Console.Error.WriteLine("Waiting for SSH access...");
            await WaitUntilAccepted(ssh, baseArgs, host);

            var args = new List<string>(baseArgs);
            args.AddRange(sessionArgs);
            if (tty) args.Add("-t");
            else if (remoteCommand is not null) args.Add("-T");
            args.Add("--");
            args.Add(host);
            if (remoteCommand is not null) args.Add(remoteCommand);

            // Ctrl+C belongs to ssh (and the remote shell) while it runs; this process must stay
            // alive long enough to remove the key afterwards.
            ConsoleCancelEventHandler keepAlive = (_, e) => e.Cancel = true;
            System.Console.CancelKeyPress += keepAlive;
            try
            {
                using var process = Process.Start(Info(ssh, args, capture: false))!;
                await process.WaitForExitAsync();
                return process.ExitCode;
            }
            finally
            {
                System.Console.CancelKeyPress -= keepAlive;
            }
        }
        finally
        {
            if (keyId is not null) await Cleanup(client, target, keyId, ttl);
            try { dir.Delete(recursive: true); } catch { /* the key is useless once deleted server-side */ }
        }
    }

    private static async Task WaitUntilAccepted(string ssh, List<string> baseArgs, string host)
    {
        string last = "";
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var probe = new List<string>(baseArgs) { "-o", "BatchMode=yes", "-T", "--", host, "exit" };
            var (code, output) = await RunCaptured(ssh, probe);
            if (code != 255) return;
            last = output.Trim();
            if (!last.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) &&
                !last.Contains("Connection closed", StringComparison.OrdinalIgnoreCase) &&
                !last.Contains("Connection reset", StringComparison.OrdinalIgnoreCase))
                throw new SliplaneException(ErrorCode.Network, "Could not open an SSH connection.", last);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        throw new SliplaneException(ErrorCode.Network, "The SSH key did not become active within a minute.", last,
            "Retry shortly. The service must be running.");
    }

    private static async Task Cleanup(SliplaneClient client, Target target, string keyId, TimeSpan ttl)
    {
        var problems = new List<string>();
        try { await client.DeleteAsync($"projects/{target.ProjectId}/services/{target.ServiceId}/ssh-keys/{keyId}"); }
        catch (SliplaneException ex) when (ex.Code != ErrorCode.NotFound) { problems.Add(ex.Message); }
        try { await client.DeleteAsync($"ssh-keys/{keyId}"); }
        catch (SliplaneException ex) when (ex.Code != ErrorCode.NotFound) { problems.Add(ex.Message); }

        if (problems.Count > 0)
            System.Console.Error.WriteLine(
                $"warning: could not remove temporary SSH key {keyId} ({string.Join("; ", problems)}). " +
                $"It expires by itself at {DateTimeOffset.UtcNow.Add(ttl):u}; or run: sliplane ssh-keys delete --key-id {keyId}");
    }

    public static string FindTool(string name)
    {
        var exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (OperatingSystem.IsWindows())
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH"));
        foreach (var d in dirs)
        {
            var candidate = Path.Combine(d.Trim('"'), exe);
            if (File.Exists(candidate)) return candidate;
        }
        throw new SliplaneException(ErrorCode.Error, $"{name} was not found.",
            remediation: OperatingSystem.IsWindows()
                ? "Add the OpenSSH Client: Settings > System > Optional features."
                : "Install the OpenSSH client.");
    }

    private static ProcessStartInfo Info(string file, IEnumerable<string> args, bool capture)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        if (capture)
        {
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.RedirectStandardInput = true;
        }
        return info;
    }

    private static async Task<(int Code, string Output)> RunCaptured(string file, IEnumerable<string> args)
    {
        using var process = Process.Start(Info(file, args, capture: true))!;
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }

    /// <summary>Reads the service's internal port from a service document, or null.</summary>
    public static int? InternalPort(JsonElement service) =>
        service.TryGetProperty("network", out var n) && n.TryGetProperty("internalPort", out var p) && p.TryGetInt32(out var port)
            ? port : null;

    public static TimeSpan ParseTtl(string value)
    {
        var since = Commands.Services.ServiceLogsCommand.ParseSince(value);
        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(since);
        if (span <= TimeSpan.FromMinutes(5) || span > TimeSpan.FromDays(30))
            throw SliplaneException.Invalid("--ttl must be longer than 5m and at most 30d.");
        return span;
    }
}
