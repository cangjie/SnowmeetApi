using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Controllers.DistributorPortal;

// 分销商（教练 / 机构）本人的入口：小程序「我的」里的分销商菜单区域。
// 身份 = 会话里的会员有一条启用的介绍人，类型为教练或机构；订票二维码和收款资料都只认登录态，不接受客户端指定分销商。
[ApiController]
[Route("api/[controller]/[action]")]
public sealed class DistributorController(ApplicationDBContext db, IConfiguration config) : CoachApiBase(db)
{
    public sealed record SaveStandardInput(List<CarePriceInput>? care, double? rental_rate);
    public sealed record MakeCouponsInput(string? shop, string? tier, string? mode, double value, int count, string? requestId);
    public sealed record SaveCardTemplateInput(int? id, string? name, List<CarePriceInput>? care, double? rental_rate);
    public sealed record DeleteCardTemplateInput(int id);
    public sealed record CreateCouponBatchInput(string? name, List<CoachCouponMakerService.CouponItemInput>? items, int count, string? requestId);
    public sealed record DeleteCouponBatchInput(int id);

    /// <summary>当前登录会员的启用中分销商（只看有菜单的类型）；没有返回 null。</summary>
    private async Task<(Introducer? introducer, ApiResult<object>? error)> CurrentAsync(string? sessionKey)
    {
        MiniSession? session = await SessionAsync(sessionKey);
        if (session == null) return (null, Result(2, "登录已失效，请重新进入小程序"));
        Member? member = await MemberOfAsync(session);
        if (member == null) return (null, Result(5, "请先授权手机号"));
        Introducer? introducer = await Db.introducer.AsNoTracking()
            .Where(i => i.member_id == member.id && i.valid == 1)
            .OrderByDescending(i => i.id).FirstOrDefaultAsync();
        if (introducer == null || !DistributorMenuRules.HasMenu(introducer.type)) return (null, Result(3, "你还不是教练或机构"));
        return (introducer, null);
    }

    /// <summary>「我的」页据此决定显示不显示分销商区域，以及区域里有哪些入口。不是分销商时 isDistributor=false（不报错）。</summary>
    [HttpGet]
    public async Task<ApiResult<object>> GetMyDistributor(string sessionKey)
    {
        var (introducer, error) = await CurrentAsync(sessionKey);
        if (error != null) return error.code == 3 ? Result(0, "", new { isDistributor = false }) : error;
        return Result(0, "", new
        {
            isDistributor = true,
            distributor = new { id = introducer!.id, type = introducer.type, name = introducer.name, coachId = introducer.coach_id },
            entries = DistributorMenuRules.Entries(introducer.type),
        });
    }

    /// <summary>收款资料，只读：方式、是否已填、支付宝是否已绑定；登录号和姓名脱敏。修改仍由系统管理员在分销商管理里做。</summary>
    [HttpGet]
    public async Task<ApiResult<object>> GetPayee(string sessionKey)
    {
        var (introducer, error) = await CurrentAsync(sessionKey);
        if (error != null) return error;
        OrderShareRelation? relation = await Db.orderShareRelation.AsNoTracking().FirstOrDefaultAsync(r => r.id == introducer!.relation_id);
        if (relation == null) return Result(0, "", new { name = introducer!.name, wechat = new { filled = false }, alipay = new { filled = false } });
        bool aliBound = await Db.shareRelationBind.AsNoTracking()
            .AnyAsync(b => b.share_relation_id == relation.id && b.pay_method == "支付宝" && b.valid);
        bool wechatFilled = !string.IsNullOrWhiteSpace(relation.wepay_account_num);
        bool aliFilled = !string.IsNullOrWhiteSpace(relation.ali_account_num);
        return Result(0, "", new
        {
            name = introducer!.name, type = introducer.type,
            wechat = new
            {
                filled = wechatFilled,
                kind = relation.wepay_account_type == "MERCHANT_ID" ? "商户号" : (wechatFilled ? "个人微信" : ""),
                // 商户全称不是敏感信息；个人微信 openid 不返回
                merchantName = relation.wepay_account_type == "MERCHANT_ID" ? relation.wepay_account_name : null,
            },
            alipay = new
            {
                filled = aliFilled, bound = aliFilled && aliBound,
                loginName = DistributorMenuRules.MaskLoginName(relation.ali_account_num),
                realName = DistributorMenuRules.MaskName(introducer.real_name),
            },
        });
    }

    /// <summary>我的顾客与消费：名下顾客（归属本分销商）加上有订单记在我名下的顾客；消费 = 归属本分销商的订单实付净额。</summary>
    [HttpGet]
    public async Task<ApiResult<object>> GetMyCustomers(string sessionKey)
    {
        var (introducer, error) = await CurrentAsync(sessionKey);
        if (error != null) return error;
        return await Run(async () =>
        {
            var (summary, customers) = await new DistributorConsumptionService(Db).GetCustomersAsync(introducer!);
            return new { summary, customers };
        });
    }

    [HttpGet]
    public async Task<ApiResult<object>> GetCustomerOrders(string sessionKey, int memberId)
    {
        var (introducer, error) = await CurrentAsync(sessionKey);
        if (error != null) return error;
        return await Run(async () =>
        {
            var (customer, orders) = await new DistributorConsumptionService(Db).GetCustomerOrdersAsync(introducer!, memberId);
            return new { customer, orders };
        });
    }

    private async Task<(Introducer introducer, Coach coach)?> OwnCoachAsync(Introducer introducer)
    {
        if (introducer.coach_id == null) return null;
        Coach? coach = await Db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == introducer.coach_id && c.member_id == introducer.member_id);
        return coach == null ? null : (introducer, coach);
    }

    [HttpGet]
    public async Task<ApiResult<object>> ListMyCardTemplates(string sessionKey)
    {
        var (intro, error) = await CurrentAsync(sessionKey); if (error != null) return error;
        var own = await OwnCoachAsync(intro!); if (own == null) return Result(1, "机构资料不完整");
        return await Run(async () => await new CoachCardTemplateService(Db, config).ListAsync(own.Value.coach, intro!));
    }

    [HttpGet]
    public async Task<ApiResult<object>> GetMyCardTemplate(string sessionKey, int? id = null)
    {
        var (intro, error) = await CurrentAsync(sessionKey); if (error != null) return error;
        var own = await OwnCoachAsync(intro!); if (own == null) return Result(1, "机构资料不完整");
        return await Run(async () => await new CoachCardTemplateService(Db, config).EditorAsync(own.Value.coach, intro!, id));
    }

    [HttpPost]
    public async Task<ApiResult<object>> SaveMyCardTemplate(string sessionKey, [FromBody] SaveCardTemplateInput input)
    {
        var (intro, error) = await CurrentAsync(sessionKey); if (error != null) return error;
        var own = await OwnCoachAsync(intro!); if (own == null) return Result(1, "机构资料不完整");
        if (input == null) return Result(1, "请填写卡种信息");
        return await Run(async () => { var t = await new CoachCardTemplateService(Db, config).SaveAsync(own.Value.coach, intro!, input.id, input.name, input.care, input.rental_rate); return new { t.id }; });
    }

    /// <summary>删除卡种：只有从来没人领取过的卡种能删，它还没人领取的分享一并撤销（见 CoachCardTemplateService.DeleteAsync）。</summary>
    [HttpPost]
    public async Task<ApiResult<object>> DeleteMyCardTemplate(string sessionKey, [FromBody] DeleteCardTemplateInput input)
    {
        var (intro, error) = await CurrentAsync(sessionKey); if (error != null) return error;
        var own = await OwnCoachAsync(intro!); if (own == null) return Result(1, "机构资料不完整");
        if (input == null || input.id <= 0) return Result(1, "没有找到这个卡种");
        return await Run(async () => await new CoachCardTemplateService(Db, config).DeleteAsync(own.Value.coach, intro!, input.id));
    }

    /// <summary>机构「制作会员卡」页：当前设置，加每一格的底价和是否开放。教练（价格由管理员设）看到 canEdit=false。</summary>
    [HttpGet]
    public async Task<ApiResult<object>> GetMyCardStandard(string sessionKey)
    {
        var (introducer, error) = await CurrentAsync(sessionKey);
        if (error != null) return error;
        var own = await OwnCoachAsync(introducer!);
        if (own == null) return Result(1, "你的分销商资料还没有关联会员卡能力，请联系管理员");
        return await Run(async () => await new CoachFloorService(Db, config).GetMyStandardAsync(own.Value.coach, own.Value.introducer));
    }

    /// <summary>机构保存自己的会员卡标准：逐格校验管理员设的底价；保存即生效，只影响之后领取的新卡。</summary>
    [HttpPost]
    public async Task<ApiResult<object>> SaveMyCardStandard(string sessionKey, [FromBody] SaveStandardInput input)
    {
        var (introducer, error) = await CurrentAsync(sessionKey);
        if (error != null) return error;
        var own = await OwnCoachAsync(introducer!);
        if (own == null) return Result(1, "你的分销商资料还没有关联会员卡能力，请联系管理员");
        return await Run(async () =>
        {
            await new CoachFloorService(Db, config).SaveMyStandardAsync(own.Value.coach, own.Value.introducer,
                new PricingInput(input?.care, input?.rental_rate), own.Value.introducer.member_id);
            return await new CoachFloorService(Db, config).GetMyStandardAsync(
                (await Db.coach.AsNoTracking().FirstAsync(c => c.id == own.Value.coach.id)), own.Value.introducer);
        });
    }

    /// <summary>机构「制作优惠券」页：能做哪几格（来自管理员的底价）、额度、最近做过的券。教练 canMake=false。</summary>
    [HttpGet]
    public async Task<ApiResult<object>> GetMyCouponMaker(string sessionKey)
    {
        var (introducer, error) = await CurrentAsync(sessionKey);
        if (error != null) return error;
        var own = await OwnCoachAsync(introducer!);
        if (own == null) return Result(1, "你的分销商资料还没有关联优惠券能力，请联系管理员");
        return await Run(async () => await new CoachCouponMakerService(Db, config).GetMakerViewAsync(own.Value.coach, own.Value.introducer));
    }

    /// <summary>机构按底价生成一批自己的优惠券，放进自己的券包（再到「我的优惠券」转发给顾客）。</summary>
    [HttpPost]
    public async Task<ApiResult<object>> MakeMyCoupons(string sessionKey, [FromBody] MakeCouponsInput input)
    {
        var (introducer, error) = await CurrentAsync(sessionKey);
        if (error != null) return error;
        var own = await OwnCoachAsync(introducer!);
        if (own == null) return Result(1, "你的分销商资料还没有关联优惠券能力，请联系管理员");
        if (input == null) return Result(1, "请填写优惠券信息");
        return await Run(async () =>
        {
            var svc = new CoachCouponMakerService(Db, config);
            var made = await svc.MakeAsync(own.Value.coach, own.Value.introducer,
                new MakeCouponInput(input.shop, input.tier, input.mode, input.value, input.count, input.requestId), DateTime.Now);
            return new { made = new { made.templateId, made.name, made.memo, made.created, made.repeated }, maker = await svc.GetMakerViewAsync(own.Value.coach, own.Value.introducer) };
        });
    }

    /// <summary>
    /// 创建优惠券分享批次（2026-11-18）：规定张数，创建时不生成券，顾客扫码 / 点开领取时才生成。
    /// 价格受管理员设的优惠券底价约束；同一请求编号只建一次。
    /// </summary>
    [HttpPost]
    public async Task<ApiResult<object>> CreateMyCouponBatch(string sessionKey, [FromBody] CreateCouponBatchInput input)
    {
        var (introducer, error) = await CurrentAsync(sessionKey);
        if (error != null) return error;
        var own = await OwnCoachAsync(introducer!);
        if (own == null) return Result(1, "你的分销商资料还没有关联优惠券能力，请联系管理员");
        if (input == null) return Result(1, "请填写优惠券信息");
        return await Run(async () =>
        {
            var batches = new CoachCouponBatchService(Db, config, SnowmeetApi.Services.Storage.FileStorageOptions.From(config).PublicBaseUrl);
            var made = await batches.CreateAsync(own.Value.coach, own.Value.introducer,
                new CoachCouponBatchService.CreateInput(input.name, input.items, input.count, input.requestId), DateTime.Now);
            return new { made = new { id = made.batch.id, total = made.batch.total_count, repeated = made.repeated, sharePath = CoachCouponBatchService.SharePath(made.batch.token) },
                maker = await new CoachCouponMakerService(Db, config).GetMakerViewAsync(own.Value.coach, own.Value.introducer) };
        });
    }

    /// <summary>删除批次：只有没人领取过的能删，二维码和小程序卡片立即失效。</summary>
    [HttpPost]
    public async Task<ApiResult<object>> DeleteMyCouponBatch(string sessionKey, [FromBody] DeleteCouponBatchInput input)
    {
        var (introducer, error) = await CurrentAsync(sessionKey);
        if (error != null) return error;
        var own = await OwnCoachAsync(introducer!);
        if (own == null) return Result(1, "你的分销商资料还没有关联优惠券能力，请联系管理员");
        if (input == null || input.id <= 0) return Result(1, "没有找到这个批次");
        return await Run(async () => await new CoachCouponBatchService(Db, config).DeleteAsync(own.Value.coach, own.Value.introducer, input.id, DateTime.Now));
    }

    /// <summary>批次的分享二维码（PNG）：scene = c=批次 token，落到优惠券领取页；只给批次所属的分销商取，已删除的批次不出码。</summary>
    [HttpGet]
    public async Task<IActionResult> GetCouponBatchQr(string sessionKey, int batchId, [FromServices] MiniStaffBookingQrService qr, string envVersion = "release")
    {
        var (introducer, error) = await CurrentAsync(sessionKey);
        if (error != null) return BadRequest(new { code = error.code, message = error.message });
        var own = await OwnCoachAsync(introducer!);
        if (own == null) return BadRequest(new { code = 1, message = "你的分销商资料还没有关联优惠券能力，请联系管理员" });
        CoachCouponBatch? batch = await new CoachCouponBatchService(Db, config).OwnBatchAsync(own.Value.coach.id, batchId);
        if (batch == null) return BadRequest(new { code = 1, message = "没有找到这个批次，可能已经删除" });
        try { return File(await qr.GetForCouponBatchAsync(batch.token, envVersion), "image/png"); }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
        { return BadRequest(new { code = 1, message = e.Message }); }
        catch (Exception e) when (e is HttpRequestException || e is JsonException)
        { return BadRequest(new { code = 1, message = "小程序码服务暂时不可用，请重试" }); }
    }

    /// <summary>我的订票二维码（PNG）：和员工订票码同款，scene 带分销商编号，顾客扫码直达雪票选购页。</summary>
    [HttpGet]
    public async Task<IActionResult> GetQrCode(string sessionKey, [FromServices] MiniStaffBookingQrService qr, string envVersion = "release")
    {
        var (introducer, error) = await CurrentAsync(sessionKey);
        if (error != null) return BadRequest(new { code = error.code, message = error.message });
        try { return File(await qr.GetForDistributorAsync(introducer!.id, envVersion), "image/png"); }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
        { return BadRequest(new { code = 1, message = e.Message }); }
        catch (Exception e) when (e is HttpRequestException || e is JsonException)
        { return BadRequest(new { code = 1, message = "小程序码服务暂时不可用，请重试" }); }
    }
}
