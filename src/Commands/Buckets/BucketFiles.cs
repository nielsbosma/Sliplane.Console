using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Sliplane.Console.Infrastructure;
using Sliplane.Console.Storage;

namespace Sliplane.Console.Commands.Buckets;

/// <summary>
/// S3 access for the file commands. The first use on a bucket creates an access key named
/// after this machine and keeps it in the OS keystore next to the account's API key, so later
/// commands reuse it. A stored key that S3 rejects (revoked in the dashboard) is replaced once.
/// </summary>
internal static class BucketFiles
{
    public sealed record Bucket(string Id, string Name, string Endpoint, string Region);

    public static async Task<Bucket> FindAsync(SliplaneClient client, string query)
    {
        var match = await Resolve.OneAsync(client, "buckets", "bucket", query);
        var e = match.Element;
        return new Bucket(match.Id, match.Name, e.GetProperty("endpoint").GetString()!, e.GetProperty("region").GetString()!);
    }

    public static async Task<T> WithS3Async<T>(SliplaneClient client, ApiSettings settings, Bucket bucket, Func<IAmazonS3, Task<T>> action)
    {
        var store = SecretStoreFactory.Create();
        var secretKey = $"bucket:{settings.ResolvedAccount!.Name.ToLowerInvariant()}:{bucket.Id}";

        for (var attempt = 0; ; attempt++)
        {
            var stored = store.Get(secretKey);
            if (stored is null)
            {
                stored = await CreateKeyAsync(client, bucket);
                store.Set(secretKey, stored);
            }
            var parts = stored.Split('\n', 3);

            using var s3 = new AmazonS3Client(new BasicAWSCredentials(parts[0], parts[1]), new AmazonS3Config
            {
                ServiceURL = bucket.Endpoint,
                AuthenticationRegion = bucket.Region,
                ForcePathStyle = true,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });
            try
            {
                return await action(s3);
            }
            catch (AmazonS3Exception ex) when (attempt == 0 && ex.ErrorCode is "InvalidAccessKeyId" or "SignatureDoesNotMatch")
            {
                store.Delete(secretKey);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw SliplaneException.NotFound(ex.Message);
            }
            catch (AmazonS3Exception ex)
            {
                throw new SliplaneException(ErrorCode.Error, $"S3: {ex.Message}", $"{(int)ex.StatusCode} {ex.ErrorCode}");
            }
        }
    }

    private static async Task<string> CreateKeyAsync(SliplaneClient client, Bucket bucket)
    {
        using var key = await client.PostAsync($"buckets/{bucket.Id}/keys", new { name = $"sliplane-console {Environment.MachineName}" });
        var root = key.RootElement;
        var id = root.GetProperty("accessKeyId").GetString()!;
        var secret = root.GetProperty("secretAccessKey").GetString()!;
        var keyId = root.TryGetProperty("id", out var i) ? i.GetString() : "";
        System.Console.Error.WriteLine($"Created access key '{root.GetProperty("name").GetString()}' on {bucket.Name} and saved it in the keystore.");
        return $"{id}\n{secret}\n{keyId}";
    }

    public static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v:0.#} {units[u]}";
    }
}
