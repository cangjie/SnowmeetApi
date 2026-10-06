using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Fnb;
namespace SnowmeetApi.Controllers.Fnb;

[ApiController, Route("api/FnbReport/[action]")]
public sealed class FnbV4ReportController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> GetDashboard(string sessionKey, int shopId, DateTime? from = null, DateTime? to = null) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).Dashboard(from, to));
    [HttpGet] public Task<ApiResult<object>> GetRecipeChain(string sessionKey, int shopId) => Execute(sessionKey, shopId, false, async () => await new FnbV4Service(Db, shopId).RecipeChain());
    [HttpGet] public async Task<IActionResult> DownloadRecipeChain(string sessionKey, int shopId)
    {
        var result = await GetRecipeChain(sessionKey, shopId); if (result.code != 0) return Ok(result);
        var bytes = FnbV4Service.ExportRecipeChain((FnbV4Service.ChainRow[])result.data!);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "fnb-recipe-chain.xlsx");
    }
}
[ApiController, Route("api/FnbLabel/[action]")]
public sealed class FnbLabelController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> GetLabelData(string sessionKey, int shopId, int batchId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).GetLabel(batchId));
}
