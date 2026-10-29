using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Sliplane.Console.Infrastructure;

public sealed class SliplaneClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly HttpClient _http;
    private string _apiKey;
    private readonly string? _orgId;

    public SliplaneClient(string apiKey, string? orgId = null)
    {
        _apiKey = apiKey;
        _orgId = orgId;
        _http = new HttpClient { BaseAddress = new Uri("https://ctrl.sliplane.io/v0/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (!string.IsNullOrEmpty(orgId))
            _http.DefaultRequestHeaders.Add("X-Organization-ID", orgId);
    }

    /// <summary>
    /// Set for a browser (OAuth) login: called once when the API answers 401, returning a fresh
    /// access token or null when the login cannot be renewed.
    /// </summary>
    public Func<Task<string?>>? Refresh { get; init; }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            var response = await _http.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && attempt == 0 && Refresh is not null &&
                await Refresh() is { } token)
            {
                response.Dispose();
                _apiKey = token;
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                continue;
            }
            return response;
        }
    }

    public async Task<JsonDocument> GetAsync(string path)
    {
        var response = await Send(HttpMethod.Get, path);
        await EnsureSuccess(response);
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    /// <summary>
    /// The public address the API sees when this machine connects over one address family, from
    /// the <c>Sliplane-Client-IP</c> header. Proxies are bypassed: a database connection would not
    /// go through them. Null when this machine cannot reach the API over that family.
    /// </summary>
    public async Task<System.Net.IPAddress?> ClientIpAsync(System.Net.Sockets.AddressFamily family)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = async (ctx, ct) =>
            {
                var addresses = await System.Net.Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, family, ct);
                if (addresses.Length == 0) throw new HttpRequestException($"no {family} address");
                var socket = new System.Net.Sockets.Socket(family, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(addresses[0], ctx.DnsEndPoint.Port, ct);
                    return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        };
        using var http = new HttpClient(handler) { BaseAddress = _http.BaseAddress, Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        if (!string.IsNullOrEmpty(_orgId)) http.DefaultRequestHeaders.Add("X-Organization-ID", _orgId);
        try
        {
            using var response = await http.GetAsync("me");
            await EnsureSuccess(response);
            return response.Headers.TryGetValues("Sliplane-Client-IP", out var values) &&
                   System.Net.IPAddress.TryParse(values.FirstOrDefault(), out var ip) ? ip : null;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    public async Task<JsonDocument> PostAsync(string path, object? body = null)
    {
        var response = await Send(HttpMethod.Post, path, body);
        await EnsureSuccess(response);
        return await ReadJson(response);
    }

    public async Task<JsonDocument> PatchAsync(string path, object body)
    {
        var response = await Send(HttpMethod.Patch, path, body);
        await EnsureSuccess(response);
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    public async Task<JsonDocument> PutAsync(string path, object body)
    {
        var response = await Send(HttpMethod.Put, path, body);
        await EnsureSuccess(response);
        return await ReadJson(response);
    }

    public async Task DeleteAsync(string path)
    {
        var response = await Send(HttpMethod.Delete, path);
        await EnsureSuccess(response);
    }

    public async Task<JsonDocument> DeleteWithResponseAsync(string path)
    {
        var response = await Send(HttpMethod.Delete, path);
        await EnsureSuccess(response);
        return await ReadJson(response);
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return JsonDocument.Parse("{}");
        var body = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(body) ? JsonDocument.Parse("{}") : JsonDocument.Parse(body);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var status = (int)response.StatusCode;
        var body = (await response.Content.ReadAsStringAsync()).Trim();
        var detail = $"HTTP {status}" + (body.Length == 0 ? "" : ": " + body) + Hint(response.StatusCode, body);

        throw response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => SliplaneException.Auth(
                "The API key was rejected.", detail,
                "Replace it: sliplane accounts add <name> --api-key <key> --force"),

            System.Net.HttpStatusCode.Forbidden => SliplaneException.Auth(
                "The API key is not allowed to do that.", detail,
                "A legacy token also needs an organization: sliplane accounts add <name> --api-key <key> --org-id <id>"),

            System.Net.HttpStatusCode.NotFound => SliplaneException.NotFound(
                "The resource does not exist.", detail),

            System.Net.HttpStatusCode.TooManyRequests => new SliplaneException(
                ErrorCode.RateLimited, "Rate limited by the Sliplane API.", detail,
                "Back off before retrying."),

            System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity =>
                new SliplaneException(ErrorCode.InvalidInput, "The API refused the request.", detail),

            _ when status >= 500 => new SliplaneException(
                ErrorCode.Network, "The Sliplane API returned a server error.", detail,
                "Retry; if it persists the platform is having trouble."),

            _ => new SliplaneException(ErrorCode.Error, "The request failed.", detail)
        };
    }

    /// <summary>
    /// Turns the API's terser refusals into something actionable. The status and
    /// body are still shown verbatim; this only appends what to do about it.
    /// </summary>
    private static string Hint(System.Net.HttpStatusCode status, string body)
    {
        if (status == System.Net.HttpStatusCode.Conflict &&
            body.Contains("deployment", StringComparison.OrdinalIgnoreCase))
            return "\n\nA service cannot move between a registry image and a repository build. " +
                   "Delete and recreate it with the deployment you want - volumes are " +
                   "server-level resources and survive, so data on them is not lost.";

        if (status == System.Net.HttpStatusCode.BadRequest &&
            body.Contains("Deployment configuration is required", StringComparison.OrdinalIgnoreCase))
            return "\n\nEvery update needs the deployment object. Pass --repo/--branch " +
                   "(or --image) alongside whatever you are changing.";

        return string.Empty;
    }
}
