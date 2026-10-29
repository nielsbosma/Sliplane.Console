using System.ComponentModel;
using Amazon.S3.Model;
using Sliplane.Console.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Sliplane.Console.Commands.Buckets;

public class BucketFileSettings : ApiSettings
{
    [CommandOption("--bucket-id|--bucket <BUCKET>")]
    [Description("Bucket name or ID")]
    public required string BucketId { get; init; }
}

public sealed class ListBucketFilesCommand : AsyncCommand<ListBucketFilesCommand.Settings>
{
    public sealed class Settings : BucketFileSettings
    {
        [CommandOption("--prefix <PREFIX>")]
        [Description("Only keys starting with this")]
        public string? Prefix { get; init; }

        [CommandOption("--recursive")]
        [Description("List every key under the prefix instead of one 'folder' level")]
        public bool Recursive { get; init; }

        [CommandOption("--limit <N>")]
        [Description("Stop after this many entries (default 1000)")]
        public int Limit { get; init; } = 1000;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var bucket = await BucketFiles.FindAsync(client, settings.BucketId);

        var entries = await BucketFiles.WithS3Async(client, settings, bucket, async s3 =>
        {
            var list = new List<Dictionary<string, object?>>();
            var request = new ListObjectsV2Request
            {
                BucketName = bucket.Name, Prefix = settings.Prefix,
                Delimiter = settings.Recursive ? null : "/"
            };
            ListObjectsV2Response response;
            do
            {
                response = await s3.ListObjectsV2Async(request);
                foreach (var p in response.CommonPrefixes ?? [])
                    list.Add(new Dictionary<string, object?> { ["key"] = p, ["type"] = "folder" });
                foreach (var o in response.S3Objects ?? [])
                    list.Add(new Dictionary<string, object?>
                    {
                        ["key"] = o.Key, ["size"] = o.Size, ["sizeText"] = BucketFiles.Size(o.Size ?? 0),
                        ["lastModified"] = o.LastModified?.ToUniversalTime().ToString("o")
                    });
                request.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated == true && list.Count < settings.Limit);

            if (list.Count > settings.Limit || response.IsTruncated == true)
                System.Console.Error.WriteLine($"warning: stopped at {settings.Limit} entries; raise --limit or narrow --prefix.");
            return list.Take(settings.Limit).ToList();
        });

        Output.WriteObject(entries);
        return 0;
    }
}

public sealed class UploadBucketFileCommand : AsyncCommand<UploadBucketFileCommand.Settings>
{
    public sealed class Settings : BucketFileSettings
    {
        [CommandOption("--file <PATH>")]
        [Description("Local file to upload")]
        public required string File { get; init; }

        [CommandOption("--key <KEY>")]
        [Description("Object key (default: the file name; a key ending in '/' gets the file name appended)")]
        public string? Key { get; init; }

        [CommandOption("--content-type <TYPE>")]
        [Description("Content-Type to store (default: guessed from the extension)")]
        public string? ContentType { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (!System.IO.File.Exists(settings.File))
            throw SliplaneException.Invalid($"'{settings.File}' does not exist.");
        var name = Path.GetFileName(settings.File);
        var key = settings.Key is null ? name : settings.Key.EndsWith('/') ? settings.Key + name : settings.Key;

        var client = settings.CreateClient();
        var bucket = await BucketFiles.FindAsync(client, settings.BucketId);
        var size = new FileInfo(settings.File).Length;

        await BucketFiles.WithS3Async(client, settings, bucket, async s3 =>
        {
            using var transfer = new Amazon.S3.Transfer.TransferUtility(s3);
            var request = new Amazon.S3.Transfer.TransferUtilityUploadRequest
            {
                BucketName = bucket.Name, Key = key, FilePath = settings.File,
                ContentType = settings.ContentType ?? MimeTypes.For(name)
            };
            await transfer.UploadAsync(request);
            return true;
        });

        Output.WriteObject(new Dictionary<string, object?> { ["bucket"] = bucket.Name, ["key"] = key, ["size"] = size });
        return 0;
    }
}

public sealed class DownloadBucketFileCommand : AsyncCommand<DownloadBucketFileCommand.Settings>
{
    public sealed class Settings : BucketFileSettings
    {
        [CommandOption("--key <KEY>")]
        [Description("Object key to download")]
        public required string Key { get; init; }

        [CommandOption("-o|--out <PATH>")]
        [Description("Where to write it (default: the key's file name in the current folder; '-' for stdout)")]
        public string? Out { get; init; }

        [CommandOption("--force")]
        [Description("Overwrite an existing local file")]
        public bool Force { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var target = settings.Out ?? Path.GetFileName(settings.Key.TrimEnd('/'));
        if (string.IsNullOrEmpty(target)) throw SliplaneException.Invalid("Cannot derive a file name from that key.", "Pass --out <path>.");
        if (target != "-" && System.IO.File.Exists(target) && !settings.Force)
            throw SliplaneException.Invalid($"'{target}' already exists.", "Pass --force to overwrite, or --out <path>.");

        var client = settings.CreateClient();
        var bucket = await BucketFiles.FindAsync(client, settings.BucketId);

        var size = await BucketFiles.WithS3Async(client, settings, bucket, async s3 =>
        {
            using var response = await s3.GetObjectAsync(bucket.Name, settings.Key);
            if (target == "-")
            {
                await using var stdout = System.Console.OpenStandardOutput();
                await response.ResponseStream.CopyToAsync(stdout);
            }
            else
            {
                var temp = target + ".partial";
                await using (var file = System.IO.File.Create(temp))
                    await response.ResponseStream.CopyToAsync(file);
                System.IO.File.Move(temp, target, overwrite: true);
            }
            return response.ContentLength;
        });

        if (target != "-")
            Output.WriteObject(new Dictionary<string, object?> { ["key"] = settings.Key, ["file"] = Path.GetFullPath(target), ["size"] = size });
        return 0;
    }
}

public sealed class RemoveBucketFileCommand : AsyncCommand<RemoveBucketFileCommand.Settings>
{
    public sealed class Settings : BucketFileSettings
    {
        [CommandOption("--key <KEY>")]
        [Description("Object key to delete")]
        public required string Key { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var client = settings.CreateClient();
        var bucket = await BucketFiles.FindAsync(client, settings.BucketId);
        settings.Confirm($"Delete {settings.Key} from {bucket.Name}?");

        await BucketFiles.WithS3Async(client, settings, bucket, async s3 =>
        {
            // S3 answers 204 for a key that never existed; check first so a typo is not reported as done.
            try { await s3.GetObjectMetadataAsync(bucket.Name, settings.Key); }
            catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw SliplaneException.NotFound($"No object '{settings.Key}' in {bucket.Name}.");
            }
            await s3.DeleteObjectAsync(bucket.Name, settings.Key);
            return true;
        });

        Output.WriteObject(new Dictionary<string, object?> { ["deleted"] = settings.Key, ["bucket"] = bucket.Name });
        return 0;
    }
}

internal static class MimeTypes
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html", [".htm"] = "text/html", [".css"] = "text/css", [".js"] = "text/javascript",
        [".json"] = "application/json", [".txt"] = "text/plain", [".csv"] = "text/csv", [".xml"] = "application/xml",
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".svg"] = "image/svg+xml", [".ico"] = "image/x-icon", [".pdf"] = "application/pdf",
        [".zip"] = "application/zip", [".gz"] = "application/gzip", [".tar"] = "application/x-tar",
        [".mp4"] = "video/mp4", [".mp3"] = "audio/mpeg", [".wasm"] = "application/wasm", [".woff2"] = "font/woff2"
    };

    public static string For(string fileName) =>
        Known.TryGetValue(Path.GetExtension(fileName), out var type) ? type : "application/octet-stream";
}
