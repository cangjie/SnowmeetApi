using System;

namespace SnowmeetApi.Services.Share;

/// <summary>介绍人分账的业务规则不允许：code 1 规则 / 3 无权限。控制器转成 ApiResult 的 code 和 message。</summary>
public sealed class ShareException(string message, int code = 1) : Exception(message)
{
    public int Code { get; } = code;
}
