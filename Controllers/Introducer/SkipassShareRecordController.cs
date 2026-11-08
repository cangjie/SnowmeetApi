using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;
using SnowmeetApi.Services.StaffAccounts;

namespace SnowmeetApi.Controllers.IntroducerShare;

// 后台「大好河山雪票」页每张票下面的「雪票分账」卡片：系统给推荐员工的分账（每张票 1 元）分给谁、成没成、计划何时、何时成功。
// 只读；店长以上（title_level ≥ 200）可看，和订单详情里的「介绍人分账」同一口径。
[ApiController]
[Route("api/[controller]/[action]")]
public sealed class SkipassShareRecordController(ApplicationDBContext db) : ShareApiBase(db)
{
    [HttpGet]
    public Task<ApiResult<object>> GetByOrder(string sessionKey, int orderId)
        => AsStaff(sessionKey, StaffAccountRules.IsManager, "只有店长以上可以查看雪票分账记录",
            async _ => await new SkipassShareRecordService(Db).ListAsync(orderId));
}
