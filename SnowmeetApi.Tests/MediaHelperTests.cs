using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SnowmeetApi.Controllers;

namespace SnowmeetApi.Tests;

public class MediaHelperTests
{
    [Fact]
    public void QrCodeReturnsValidPngWithMatchingContentType()
    {
        var result = Assert.IsType<FileContentResult>(new MediaHelper().GetQRCode("local-test"));
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, result.FileContents[..8]);
        using var image = Image.Load(result.FileContents);
        Assert.True(image.Width > 0 && image.Height > 0);
    }
}
