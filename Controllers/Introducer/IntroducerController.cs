using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;
using SnowmeetApi.Services.Share;
using SnowmeetApi.Services.StaffAccounts;

namespace SnowmeetApi.Controllers.IntroducerShare;

// 管理员后台「介绍人管理」（小程序 pages/introducer/admin）。管理只对系统管理员（title_level ≥ 300）开放；
// 订单上选介绍人用的 ListChoices 对店长以上（≥ 200）开放。
[ApiController]
[Route("api/[controller]/[action]")]
public sealed class IntroducerController(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor http) : ShareApiBase(db)
{
    public sealed record AddIntroducerInput(int memberId, string? type, string? name, string? realName,
        string? wepayAccountNum, string? wepayAccountName, string? aliAccountNum);
    public sealed record UpdateIntroducerInput(int introducerId, string? name, string? realName, bool? valid,
        string? wepayAccountNum, string? wepayAccountName, string? aliAccountNum);
    public sealed record IntroducerIdInput(int introducerId);

    private const string AdminOnly = "只有系统管理员可以管理介绍人";

    private IntroducerService Service => new(Db, new AlipayRoyaltyBinder(Db, config, http));

    [HttpGet]
    public Task<ApiResult<object>> ListIntroducers(string sessionKey)
        => AsStaff(sessionKey, StaffAccountRules.IsAdmin, AdminOnly, async _ =>
        {
            await Service.SyncCoachesAsync();   // 教练也是介绍人，先把还没建的教练补上
            return new { introducers = await Service.ListAsync(false) };
        });

    [HttpGet]
    public Task<ApiResult<object>> LookupMember(string sessionKey, string cell)
        => AsStaff(sessionKey, StaffAccountRules.IsAdmin, AdminOnly, async _ => await new CoachAdminService(Db, config).LookupMemberAsync(cell));

    [HttpPost]
    public Task<ApiResult<object>> AddIntroducer(string sessionKey, [FromBody] AddIntroducerInput input)
        => AsStaff(sessionKey, StaffAccountRules.IsAdmin, AdminOnly, async op => await Service.AddAsync(op, input.memberId,
            input.type ?? "", input.name ?? "", input.realName, new(input.wepayAccountNum, input.wepayAccountName, input.aliAccountNum)));

    [HttpPost]
    public Task<ApiResult<object>> UpdateIntroducer(string sessionKey, [FromBody] UpdateIntroducerInput input)
        => AsStaff(sessionKey, StaffAccountRules.IsAdmin, AdminOnly, async _ => await Service.UpdateAsync(input.introducerId, input.name,
            input.realName, input.valid, new(input.wepayAccountNum, input.wepayAccountName, input.aliAccountNum)));

    [HttpPost]
    public Task<ApiResult<object>> BindAlipay(string sessionKey, [FromBody] IntroducerIdInput input)
        => AsStaff(sessionKey, StaffAccountRules.IsAdmin, AdminOnly, async _ => await Service.BindAlipayAsync(input.introducerId));

    [HttpGet]
    public Task<ApiResult<object>> ListChoices(string sessionKey)
        => AsStaff(sessionKey, StaffAccountRules.IsManager, "只有店长以上可以设置介绍人分账", async _ =>
        {
            await Service.SyncCoachesAsync();   // 订单上选收款人时，教练直接在候选里
            return new { choices = (await Service.ListAsync(true)).Select(v => new { v.id, v.name, v.type }).ToList() };
        });
}
