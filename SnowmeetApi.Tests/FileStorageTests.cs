using System.Net;
using System.Reflection;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using SnowmeetApi.Services.Storage;

namespace SnowmeetApi.Tests;

public class FileStorageTests
{
    private static readonly FileStorageOptions Options = new FileStorageOptions()
    {
        Bucket = "snowmeet-upload",
        PublicBaseUrl = "https://img.snowmeet.top"
    };

    [Theory]
    [InlineData("IMG_001.JPG", "jpg")]
    [InlineData("a.b.png", "png")]
    [InlineData("noext", "bin")]
    [InlineData("trailing.", "bin")]
    [InlineData("evil.p/../hp", "hp")]
    [InlineData("x.verylongextension", "bin")]
    [InlineData("", "bin")]
    [InlineData(null, "bin")]
    public void ExtensionIsSanitized(string? fileName, string expected)
    {
        Assert.Equal(expected, UploadPaths.SafeExtension(fileName!));
    }

    [Fact]
    public void NewPathIsDatedAndRandom()
    {
        DateTime now = new DateTime(2026, 10, 3, 12, 0, 0);
        string a = UploadPaths.NewRelativePath("x.JPG", now);
        string b = UploadPaths.NewRelativePath("x.JPG", now);
        Assert.Matches("^/upload/20261003/[0-9a-f]{32}\\.jpg$", a);
        Assert.NotEqual(a, b);
        Assert.Matches("^/upload/20261003/ticket_poster_7_[0-9a-f]{32}\\.png$",
            UploadPaths.NewRelativePath("poster.png", now, "ticket_poster_7_"));
    }

    [Theory]
    [InlineData("/upload/20261003/a.jpg", true, "upload/20261003/a.jpg")]
    [InlineData(" /upload/20261003/a.jpg ", true, "upload/20261003/a.jpg")]
    [InlineData("/upload/20261003/a.jpg", false, "private/upload/20261003/a.jpg")]
    public void ObjectKeyMirrorsRelativePath(string path, bool isWeb, string expected)
    {
        Assert.Equal(expected, UploadPaths.ObjectKey(path, isWeb));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/upload/../config.sqlServer")]
    [InlineData("/upload/./a.jpg")]
    public void ObjectKeyRejectsTraversal(string path)
    {
        Assert.Throws<ArgumentException>(() => UploadPaths.ObjectKey(path));
    }

    [Theory]
    [InlineData("/upload/20261003/a.jpg", "https://img.snowmeet.top/upload/20261003/a.jpg")]
    [InlineData("upload/20261003/a.jpg", "https://img.snowmeet.top/upload/20261003/a.jpg")]
    [InlineData("https://mini.snowmeet.top/upload/1.jpg", "https://mini.snowmeet.top/upload/1.jpg")]
    public void PublicUrlPrefixesOnlyRelativePaths(string path, string expected)
    {
        Assert.Equal(expected, UploadPaths.PublicUrl("https://img.snowmeet.top/", path));
    }

    [Theory]
    [InlineData("/upload/1/a.JPEG", "image/jpeg")]
    [InlineData("/upload/1/a.png", "image/png")]
    [InlineData("/upload/1/a.xyz", "application/octet-stream")]
    public void ContentTypeFollowsExtension(string path, string expected)
    {
        Assert.Equal(expected, UploadPaths.ContentType(path));
    }

    [Fact]
    public async Task S3SaveWritesObjectUnderUploadKey()
    {
        IAmazonS3 s3 = DispatchProxy.Create<IAmazonS3, FakeS3>();
        FakeS3 fake = (FakeS3)(object)s3;
        S3FileStorage storage = new S3FileStorage(Options, s3);

        string path = await storage.SaveAsync(new MemoryStream(new byte[] { 1, 2, 3 }), "photo.jpg");

        PutObjectRequest put = Assert.Single(fake.Puts);
        Assert.Equal("snowmeet-upload", put.BucketName);
        Assert.Equal(path.TrimStart('/'), put.Key);
        Assert.Equal("image/jpeg", put.ContentType);
        Assert.Contains("immutable", put.Headers.CacheControl);
        Assert.Equal(new byte[] { 1, 2, 3 }, await storage.ReadAsync(path));
        Assert.Equal("https://img.snowmeet.top" + path, storage.PublicUrl(path));
    }

    [Fact]
    public async Task S3NonWebFileGoesToPrivatePrefix()
    {
        IAmazonS3 s3 = DispatchProxy.Create<IAmazonS3, FakeS3>();
        FakeS3 fake = (FakeS3)(object)s3;
        string path = await new S3FileStorage(Options, s3).SaveAsync(new MemoryStream(new byte[] { 9 }), "a.pdf", false);
        Assert.StartsWith("private/upload/", Assert.Single(fake.Puts).Key);
        Assert.StartsWith("/upload/", path);
    }

    [Fact]
    public async Task S3ReadFallsBackToLocalDiskWhenObjectMissing()
    {
        string oldWorkingPath = Util.workingPath;
        string root = Path.Combine(Path.GetTempPath(), "snowmeet-storage-" + Guid.NewGuid().ToString("N"));
        try
        {
            Util.workingPath = root;
            Directory.CreateDirectory(Path.Combine(root, "wwwroot", "upload", "20250101"));
            File.WriteAllBytes(Path.Combine(root, "wwwroot", "upload", "20250101", "old.jpg"), new byte[] { 7 });
            S3FileStorage storage = new S3FileStorage(Options, DispatchProxy.Create<IAmazonS3, FakeS3>());

            Assert.Equal(new byte[] { 7 }, await storage.ReadAsync("/upload/20250101/old.jpg"));
            Assert.Null(await storage.ReadAsync("/upload/20250101/missing.jpg"));
        }
        finally
        {
            Util.workingPath = oldWorkingPath;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("GET", "/upload/20261003/a.jpg?w=1", HttpStatusCode.Redirect, "https://img.snowmeet.top/upload/20261003/a.jpg?w=1")]
    [InlineData("HEAD", "/upload/20261003/a.jpg", HttpStatusCode.Redirect, "https://img.snowmeet.top/upload/20261003/a.jpg")]
    [InlineData("GET", "/uploads/a.jpg", HttpStatusCode.OK, null)]
    [InlineData("GET", "/api/UploadFile/Upload/abc", HttpStatusCode.OK, null)]
    [InlineData("POST", "/upload/20261003/a.jpg", HttpStatusCode.OK, null)]
    public async Task MissingUploadIsRedirectedToImageHost(string method, string url, HttpStatusCode status, string? location)
    {
        using IHost host = await new HostBuilder()
            .ConfigureWebHost(web => web.UseTestServer().Configure(app =>
            {
                app.UseUploadRedirect(Options);
                app.Run(context => context.Response.WriteAsync("next"));
            }))
            .StartAsync();

        HttpResponseMessage response = await host.GetTestClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(location, response.Headers.Location?.ToString());
    }

    public class FakeS3 : DispatchProxy
    {
        public List<PutObjectRequest> Puts { get; } = new();
        private readonly Dictionary<string, byte[]> _objects = new();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IAmazonS3.PutObjectAsync) && args![0] is PutObjectRequest put)
            {
                using MemoryStream ms = new MemoryStream();
                put.InputStream.CopyTo(ms);
                _objects[put.BucketName + "/" + put.Key] = ms.ToArray();
                Puts.Add(put);
                return Task.FromResult(new PutObjectResponse());
            }
            if (method.Name == nameof(IAmazonS3.GetObjectAsync) && args![0] is string bucket && args[1] is string key)
            {
                if (_objects.TryGetValue(bucket + "/" + key, out byte[]? bytes))
                {
                    return Task.FromResult(new GetObjectResponse() { ResponseStream = new MemoryStream(bytes) });
                }
                return Task.FromException<GetObjectResponse>(new AmazonS3Exception("NoSuchKey") { StatusCode = HttpStatusCode.NotFound });
            }
            if (method.Name == nameof(IDisposable.Dispose))
            {
                return null;
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
