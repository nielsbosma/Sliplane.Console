using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sliplane.Console.Infrastructure;
using Sliplane.Console.Storage;

namespace Sliplane.Console.Auth;

/// <summary>
/// Browser login: OAuth 2 authorization code with PKCE and a loopback redirect.
///
/// It needs an OAuth client registered in the Sliplane dashboard with the redirect URI
/// <c>http://127.0.0.1:&lt;port&gt;/callback</c> - this tool does not borrow another application's
/// client id. The access and refresh token are stored together, as one keystore entry, in place
/// of the API key; an expired access token is renewed on the first 401.
/// </summary>
public static class OAuthLogin
{
    public const string AuthorizeUrl = "https://api.sliplane.io/web/oauth/authorize";
    public const string TokenUrl = "https://api.sliplane.io/web/oauth/token";

    public sealed record Credential(string ClientId, string AccessToken, string? RefreshToken)
    {
        public string Serialize() => JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["kind"] = "oauth", ["clientId"] = ClientId, ["accessToken"] = AccessToken, ["refreshToken"] = RefreshToken
        });

        /// <summary>A stored secret is either a bare API key or this JSON.</summary>
        public static Credential? TryParse(string secret)
        {
            if (!secret.StartsWith('{')) return null;
            using var doc = JsonDocument.Parse(secret);
            var r = doc.RootElement;
            return r.TryGetProperty("kind", out var k) && k.GetString() == "oauth"
                ? new Credential(r.GetProperty("clientId").GetString()!, r.GetProperty("accessToken").GetString()!,
                    r.TryGetProperty("refreshToken", out var t) ? t.GetString() : null)
                : null;
        }
    }

    public static async Task<Credential> LoginAsync(string clientId, int port, TimeSpan timeout)
    {
        var redirect = $"http://127.0.0.1:{port}/callback";
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var url = AuthorizeUrl + "?" + string.Join("&", new Dictionary<string, string>
        {
            ["response_type"] = "code", ["client_id"] = clientId, ["redirect_uri"] = redirect, ["scope"] = "full",
            ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256"
        }.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/callback/");
        try { listener.Start(); }
        catch (HttpListenerException ex)
        {
            throw SliplaneException.Invalid($"Cannot listen on 127.0.0.1:{port}: {ex.Message}", "Pick another --port (it must also be registered on the OAuth client).");
        }

        System.Console.Error.WriteLine("Opening your browser to log in. If it does not open, visit:");
        System.Console.Error.WriteLine(url);
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* the URL is printed */ }

        var contextTask = listener.GetContextAsync();
        if (await Task.WhenAny(contextTask, Task.Delay(timeout)) != contextTask)
            throw new SliplaneException(ErrorCode.AuthRequired, $"No browser login within {timeout.TotalMinutes:0} minutes.");

        var context = await contextTask;
        var query = System.Web.HttpUtility.ParseQueryString(context.Request.Url?.Query ?? "");
        var ok = query["state"] == state && !string.IsNullOrEmpty(query["code"]);
        var page = ok ? "Logged in. You can close this tab." : $"Login failed: {WebUtility.HtmlEncode(query["error_description"] ?? query["error"] ?? "state mismatch")}";
        var bytes = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=utf-8><title>Sliplane</title><p style=\"font:16px system-ui;margin:3em\">{page}</p>");
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();

        if (query["state"] != state)
            throw new SliplaneException(ErrorCode.AuthRequired, "The login response did not match this request (state mismatch).");
        if (string.IsNullOrEmpty(query["code"]))
            throw new SliplaneException(ErrorCode.AuthRequired, "The login was refused.", query["error_description"] ?? query["error"]);

        return await TokenAsync(clientId, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["code"] = query["code"]!,
            ["code_verifier"] = verifier, ["redirect_uri"] = redirect
        });
    }

    /// <summary>
    /// Renews a stored login and writes the new pair back before returning it: the old refresh
    /// token stops working once used, so a crash between the two would lose the login.
    /// </summary>
    public static async Task<string?> RefreshAndStoreAsync(string accountName, Credential current)
    {
        if (string.IsNullOrEmpty(current.RefreshToken)) return null;
        Credential renewed;
        try
        {
            renewed = await TokenAsync(current.ClientId, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["client_id"] = current.ClientId, ["refresh_token"] = current.RefreshToken
            });
        }
        catch (SliplaneException) { return null; }

        using (var _ = await FileLock.AcquireAsync(ConfigStore.LockPath, CancellationToken.None))
            SecretStoreFactory.Create().Set(SecretKeys.Account(accountName), renewed.Serialize());
        return renewed.AccessToken;
    }

    private static async Task<Credential> TokenAsync(string clientId, Dictionary<string, string> form)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new SliplaneException(ErrorCode.AuthRequired, "The token request failed.", $"HTTP {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;
        if (!r.TryGetProperty("access_token", out var access) || string.IsNullOrEmpty(access.GetString()))
            throw new SliplaneException(ErrorCode.AuthRequired, "The token response had no access token.");
        return new Credential(clientId, access.GetString()!, r.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
