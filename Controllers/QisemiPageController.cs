using Microsoft.AspNetCore.Mvc;

namespace SnowmeetApi.Controllers;

public sealed class QisemiPageController : ControllerBase
{
    [HttpGet("/wecom/qisemi")]
    public IActionResult Index() => Redirect("/wecom/qisemi/index.html" + Request.QueryString.Value);
}
