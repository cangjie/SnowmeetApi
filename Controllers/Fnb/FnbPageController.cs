using Microsoft.AspNetCore.Mvc;

namespace SnowmeetApi.Controllers.Fnb;

// StaticFiles 不提供目录默认页；为两端统一标签链接显式提供固定站内跳转。
public sealed class FnbPageController : ControllerBase
{
    [HttpGet("/fnb/b")]
    public IActionResult Batch() => Redirect("/fnb/v4/index.html" + Request.QueryString.Value);
}
