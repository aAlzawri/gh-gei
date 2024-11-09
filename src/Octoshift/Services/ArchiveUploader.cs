using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json.Linq;
using OctoshiftCLI.Extensions;

namespace OctoshiftCLI.Services;

public class ArchiveUploader
{
    private readonly GithubClient _client;
    private readonly OctoLogger _log;
    internal int _streamSizeLimit = 100 * 1024 * 1024; // 100 MiB
    private const string BASE_URL = "https://uploads.github.com";

    public ArchiveUploader(GithubClient client, OctoLogger log)
    {
        _client = client;
        _log = log;
    }

    public virtual async Task<string> Upload(Stream archiveContent, string archiveName, string orgDatabaseId)
    {
        if (archiveContent == null)
        {
            throw new ArgumentNullException(nameof(archiveContent), "The archive content stream cannot be null.");
        }

        var isMultipart = archiveContent.Length > _streamSizeLimit;
        var url = $"{BASE_URL}/organizations/{orgDatabaseId.EscapeDataString()}/gei/archive" +
                  (isMultipart ? "/blobs/uploads" : $"?name={archiveName.EscapeDataString()}");

        return isMultipart ? await UploadMultipart(archiveContent, archiveName, url)
                           : await UploadSingle(archiveContent, url);
    }

    private async Task<string> UploadSingle(Stream archiveContent, string url)
    {
        using var streamContent = CreateStreamContent(archiveContent);
        var response = await _client.PostAsync(url, streamContent);
        return (string)JObject.Parse(response)["uri"];
    }

    private async Task<string> UploadMultipart(Stream archiveContent, string archiveName, string uploadUrl)
    {
        var buffer = new byte[_streamSizeLimit];
        var guid = string.Empty;

        try
        {
            var startHeaders = await StartUpload(uploadUrl, archiveName, archiveContent.Length);
            var nextUrl = GetNextUrl(startHeaders);

            guid = HttpUtility.ParseQueryString(nextUrl.Query)["guid"];
            nextUrl = await UploadParts(buffer, archiveContent, nextUrl);

            await CompleteUpload(nextUrl.ToString());
            return $"gei://archive/{guid}";
        }
        catch (Exception ex)
        {
            throw new OctoshiftCliException("Failed during multipart upload.", ex);
        }
    }

    private async Task<Uri> UploadParts(byte[] buffer, Stream archiveContent, Uri nextUrl)
    {
        int bytesRead;
        var partsRead = 0;
        var totalParts = (long)Math.Ceiling((double)archiveContent.Length / _streamSizeLimit);

        while ((bytesRead = await archiveContent.ReadAsync(buffer)) > 0)
        {
            nextUrl = await UploadPart(buffer, bytesRead, nextUrl.ToString(), partsRead, totalParts);
            partsRead++;
        }

        return nextUrl;
    }

    private async Task<IEnumerable<KeyValuePair<string, IEnumerable<string>>>> StartUpload(string uploadUrl, string archiveName, long contentSize)
    {
        _log.LogInformation($"Starting archive upload into GitHub owned storage: {archiveName}...");

        var body = new { content_type = "application/octet-stream", name = archiveName, size = contentSize };

        try
        {
            var (responseContent, headers) = await _client.PostWithFullResponseAsync(uploadUrl, body);
            return headers.ToList();
        }
        catch (Exception ex)
        {
            throw new OctoshiftCliException("Failed to start upload.", ex);
        }
    }

    private async Task<Uri> UploadPart(byte[] body, int bytesRead, string nextUrl, int partsRead, long totalParts)
    {
        _log.LogInformation($"Uploading part {partsRead + 1}/{totalParts}...");
        using var content = CreateStreamContent(body, bytesRead);

        try
        {
            var (_, headers) = await _client.PatchWithFullResponseAsync(nextUrl, content);
            return GetNextUrl(headers.ToList());
        }
        catch (Exception ex)
        {
            throw new OctoshiftCliException("Failed to upload part.", ex);
        }
    }

    private async Task CompleteUpload(string lastUrl)
    {
        try
        {
            await _client.PutAsync(lastUrl, "");
            _log.LogInformation("Finished uploading archive");
        }
        catch (Exception ex)
        {
            throw new OctoshiftCliException("Failed to complete upload.", ex);
        }
    }

    private StreamContent CreateStreamContent(Stream content) => new(content) { Headers = { ContentType = new("application/octet-stream") } };

    private StreamContent CreateStreamContent(byte[] buffer, int length) => new(buffer, 0, length) { Headers = { ContentType = new("application/octet-stream") } };

    private Uri GetNextUrl(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var locationHeader = headers.FirstOrDefault(header => header.Key.Equals("Location", StringComparison.OrdinalIgnoreCase));
        var locationValue = locationHeader.Value?.FirstOrDefault();

        return locationValue.HasValue() ? new Uri(new Uri(BASE_URL), locationValue) : throw new OctoshiftCliException("Location header is missing in the response.");
    }
}
