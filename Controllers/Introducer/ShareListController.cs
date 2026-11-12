using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;
using SnowmeetApi.Services.StaffAccounts;

namespace SnowmeetApi.Controllers.IntroducerShare;

// 店长以上查看分账；自动审核、手动渠道提交及银行转账记账由管理员执行。
// 设计：snowmeet_ai_doc/docs/superpowers/specs/2026-11-03-share-list-manual-batch-design.md。code：0 成功 / 1 业务规则不允许 / 2 会话失效 / 3 无权限
[ApiController]
[Route("api/[controller]/[action]")]
public sealed class ShareListController(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor http) : ShareApiBase(db)
{
    public sealed record CompleteManualInput(List<int> orderShareIds, string? batchNo);
    public sealed record ShareActionInput(int orderShareId);

    private const string ManagerOnly = "只有店长以上可以查看分账";
    private const string AdminOnly = "只有管理员可以处理分账";
    private const int MaxPageSize = 100;

    private ShareListService Service => new(Db);

    /// <param name="status">未分 / 已分，不传 = 全部</param>
    /// <param name="mode">自动 / 手动 / 银行转账，不传 = 全部</param>
    /// <param name="startDate">yyyy-MM-dd，按「计划分账日期，没有就取创建日」筛，含首尾两天</param>
    [HttpGet]
    public Task<ApiResult<object>> List(string sessionKey, string? status = null, string? type = null, string? mode = null, string? keyword = null,
        string? startDate = null, string? endDate = null, int pageIndex = 1, int pageSize = 20, bool distributorOnly = false, string? distributorType = null)
        => AsStaff(sessionKey, StaffAccountRules.IsManager, ManagerOnly, async _ =>
        {
            DateTime? start = ParseDate(startDate), end = ParseDate(endDate);
            var page = await Service.ListAsync(new ShareListQuery(status, type, mode, keyword, start, end, Math.Max(1, pageIndex), Math.Clamp(pageSize, 1, MaxPageSize))
                { DistributorOnly = distributorOnly, DistributorType = distributorType });
            return new { items = page.items, total = page.total, totalAmount = page.totalAmount, selectableCount = page.selectableCount };
        });

    [HttpGet]
    public Task<ApiResult<object>> NextBatchNo(string sessionKey)
        => AsStaff(sessionKey, StaffAccountRules.IsAdmin, AdminOnly, async _ => new { batchNo = await Service.NextBatchNoAsync() });

    [HttpPost]
    public Task<ApiResult<object>> CompleteManual(string sessionKey, [FromBody] CompleteManualInput input)
        => AsStaff(sessionKey, StaffAccountRules.IsAdmin, AdminOnly, async op =>
        {
            var result = await Service.CompleteManualAsync(op, input.orderShareIds ?? new List<int>(), input.batchNo);
            return new { batchNo = result.batchNo, count = result.count, total = result.total };
        });

    [HttpPost]
    public Task<ApiResult<object>> ApproveAuto(string sessionKey, [FromBody] ShareActionInput input)
        => AsStaff(sessionKey, StaffAccountRules.IsAdmin, AdminOnly, async op =>
        {
            await Service.ApproveAutoAsync(op, input.orderShareId);
            return new { approved = true };
        });

    [HttpPost]
    public Task<ApiResult<object>> SubmitManual(string sessionKey, [FromBody] ShareActionInput input)
        => AsStaff(sessionKey, StaffAccountRules.IsAdmin, AdminOnly, async op =>
        {
            var paymentController = new OrderShareController(Db, config, http);
            await new ShareChannelSubmitService(Db, paymentController.SubmitClaimedPayment).SubmitManualAsync(op, input.orderShareId);
            return new { submitted = true };
        });

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!ShareDateRules.TryParse(text, out DateTime date)) throw new ShareException("日期格式不对");
        return date;
    }
}
