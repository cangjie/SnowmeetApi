using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;

namespace SnowmeetApi.Tests;

/// <summary>
/// 上传接口不设应用层大小上限：Kestrel 默认只收约 30MB、表单默认 128MB，养护维修留证的视频会被拦下。
/// 真正的上限是前面 nginx 的 client_max_body_size（mini.snowmeet.top 是 1000M）；文件照旧直接写进 S3（见 FileStorageTests）。
/// 这里只钉住三个收文件的上传接口都带着「不限请求体、不限表单」两个标记，别被误删。
/// </summary>
public class UploadSizeLimitTests
{
    [Theory]
    [InlineData("UploadFileWithThumb")]
    [InlineData("UploadFile")]
    [InlineData("Upload")]
    public void UploadActionsHaveNoApplicationSizeLimit(string action)
    {
        MethodInfo method = typeof(UploadFileController).GetMethod(action)!;
        Assert.NotNull(method.GetCustomAttribute<DisableRequestSizeLimitAttribute>());
        RequestFormLimitsAttribute? form = method.GetCustomAttribute<RequestFormLimitsAttribute>();
        Assert.NotNull(form);
        Assert.Equal(long.MaxValue, form!.MultipartBodyLengthLimit);
    }

    [Fact]
    public void TheOtherEndpointsOfTheControllerKeepTheDefaultLimit()
    {
        // 只放开收文件的接口：查询类接口（GetFile / GetUploadList）不需要
        foreach (string action in new[] { "GetFile", "GetUploadList" })
        {
            MethodInfo method = typeof(UploadFileController).GetMethod(action)!;
            Assert.Null(method.GetCustomAttribute<DisableRequestSizeLimitAttribute>());
        }
    }
}
