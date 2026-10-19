using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using SnowmeetApi.Services.Qisemi;
using SnowmeetApi.Services.Storage;

namespace SnowmeetApi.Tests;

public class QisemiFileReaderTests
{
    [Fact]
    public void QueryServiceStartupDoesNotRequireAwsCredentials()
    {
        using var reader = new QisemiS3FileReader(new FileStorageOptions());
        Assert.NotNull(reader);
    }
    private sealed class Storage : AmazonS3Client
    {
        public int Reads { get; private set; }
        public Storage() : base(new AnonymousAWSCredentials(), RegionEndpoint.CNNorthWest1) { }
        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken ct = default)
        {
            Reads++; return Task.FromResult(new GetObjectResponse { ResponseStream = new MemoryStream([1, 2, 3]) });
        }
    }
    [Fact]
    public async Task MainNamedImageUsesSavedMimeAndHtmlIsDownloaded()
    {
        using var s3 = new Storage(); using var reader = new QisemiS3FileReader(new FileStorageOptions(), s3);
        var image = await reader.OpenAsync("s3", "private/qisemi/datasets/test/abc.jpg", "main", "image/jpeg", default);
        using (image.Owner) { Assert.True(image.Inline); Assert.Equal("image/jpeg", image.ContentType); }
        var html = await reader.OpenAsync("s3", "private/qisemi/datasets/test/abc.html", "page.html", "text/html", default);
        using (html.Owner) Assert.False(html.Inline);
        Assert.Equal(2, s3.Reads);
    }
    [Theory]
    [InlineData("upload/public.jpg")]
    [InlineData("private/qisemi/datasets/../secret")]
    [InlineData("private/qisemi/datasets/test\\secret")]
    public async Task InvalidKeysNeverReachStorage(string key)
    {
        using var s3 = new Storage(); using var reader = new QisemiS3FileReader(new FileStorageOptions(), s3);
        await Assert.ThrowsAsync<QisemiReadException>(() => reader.OpenAsync("s3", key, "image.jpg", "image/jpeg", default));
        Assert.Equal(0, s3.Reads);
    }
}
