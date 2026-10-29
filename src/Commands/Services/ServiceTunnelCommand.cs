using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Services;

/// <summary>
/// Forwards a local port to the service's internal port over a temporary SSH key - the way to
/// reach a private service, or a public one past its proxy.
/// </summary>
public sealed class ServiceTunnelCommand : AsyncCommand<ServiceTunnelCommand.Settings>
{
    public sealed class Settings : ApiSettings
    {
        [CommandOption("--project-id|--project <PROJECT>")]
        [Description("Project name or ID (optional: the service is looked up in every project)")]
        public string? ProjectId { get; init; }

        [CommandOption("--service-id|--service <SERVICE>")]
        [Description("Service name or ID")]
        public required string ServiceId { get; init; }

        [CommandOption("--local-port <PORT>")]
        [Description("Local port (default: the service's port if free, else any free port)")]
        public int? LocalPort { get; init; }

        [CommandOption("--remote-port <PORT>")]
        [Description("Port inside the service (default: its internal port, else 80)")]
        public int? RemotePort { get; init; }

        [CommandOption("--ttl <DURATION>")]
        [Description("Fallback lifetime of the temporary key if cleanup cannot run (default 1h, 6m to 30d)")]
        public string Ttl { get; init; } = "1h";

        public override ValidationResult Validate()
        {
            if (LocalPort is < 0 or > 65535) return ValidationResult.Error("--local-port must be 0-65535 (0 picks a free port).");
            if (RemotePort is < 1 or > 65535) return ValidationResult.Error("--remote-port must be 1-65535.");
            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var ttl = SshSession.ParseTtl(settings.Ttl);
        var client = settings.CreateClient();
        var (projectId, service) = await Resolve.ServiceMatchAsync(client, settings.ProjectId, settings.ServiceId);

        if (service.Element.TryGetProperty("network", out var network) &&
            network.TryGetProperty("protocol", out var protocol) && protocol.GetString() == "udp")
            throw SliplaneException.Invalid("SSH tunnels carry TCP only; this service is UDP.");

        var remote = settings.RemotePort ?? SshSession.InternalPort(service.Element) ?? 80;
        var local = PickLocalPort(settings.LocalPort, remote);

        System.Console.Error.WriteLine($"Forwarding 127.0.0.1:{local} to {service.Name}:{remote} (Ctrl+C to stop)");

        return await SshSession.RunAsync(client, new SshSession.Target(projectId, service.Id, service.Name), ttl,
            ["-N", "-L", $"127.0.0.1:{local}:localhost:{remote}"], remoteCommand: null, tty: false);
    }

    private static int PickLocalPort(int? requested, int remote)
    {
        if (requested is > 0) return requested.Value;
        if (requested is null && IsFree(remote)) return remote;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static bool IsFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException) { return false; }
    }
}
