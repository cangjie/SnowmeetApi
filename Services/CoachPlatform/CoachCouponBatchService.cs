using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Storage;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Services.CoachPlatform;

/// <summary>批次领取页打开后的状态，前端据此显示各个画面。</summary>
public static class CouponBatchState
{
    public const string Claimable = "claimable";
    /// <summary>本人已领过这个批次（含刚刚领取成功），带券码</summary>
    public const string ClaimedByMe = "claimed_by_me";
    /// <summary>今天已经领过一张机构优惠券（每个 openid 每天只能领一次，不分批次）</summary>
    public const string DailyLimit = "daily_limit";
    public const string SoldOut = "sold_out";
    /// <summary>批次已删除 / 分销商已停用 / 券模板已停用 / 链接不对</summary>
    public const string Invalid = "invalid";
    public const string Self = "self";
    public const string Expired = "expired";
}

public sealed class CouponShareView
{
    public string state { get; set; } = CouponBatchState.Invalid;
    public string message { get; set; } = "";
    public string name { get; set; } = "";
    public string memo { get; set; } = "";
    public string distributorName { get; set; } = "";
    public int total { get; set; }
    public int claimed { get; set; }
    public int remaining { get; set; }
    public DateTime? validUntil { get; set; }
    /// <summary>本人领到的券码（只有 claimed_by_me）</summary>
    public string? code { get; set; }
}

/// <summary>
/// 分销商的优惠券批次（用户 2026-11-18）：每做一次 = 一个分享批次，规定张数，配一个分享二维码 / 小程序卡片，
/// 转发给个人或放进群里让大家领；创建批次时不生成券，领取时才生成一张。
/// 规则：领满规定张数不能再领；每个 openid（会员）每天只能领一次（不分批次、不分分销商），同一批次也只能领一次；
/// 没人领取过的批次可以删除。券价沿用「优惠券底价」约束和现有的隐藏券模板 + 价格规则，开单选券、核销计价都不用改。
/// </summary>
public sealed class CoachCouponBatchService(ApplicationDBContext db, IConfiguration config, string? publicBaseUrl = null)
{
    /// <summary>一个批次最多多少张（券在领取时才生成，所以比原来「一次生成 50 张」放得宽）</summary>
    public const int MaxBatchSize = 500;
    /// <summary>名下所有还没领完的批次，剩余可领张数合计的上限，防止无限开批次</summary>
    public const int MaxOpenCapacity = 1000;
    /// <summary>领取页（小程序卡片的路径、二维码落地页）</summary>
    public const string ClaimPage = "/pages/coach/coupon_claim/coupon_claim";

    /// <summary>name = 券名；items = 这张券适用的服务（每个服务各自的方式和价格，留空的服务不适用）。</summary>
    public sealed record CreateInput(string? name, List<CoachCouponMakerService.CouponItemInput>? items, int count, string? requestId);
    public sealed record CreateResult(CoachCouponBatch batch, bool repeated);

    private static readonly Regex TokenPattern = new("^[0-9a-zA-Z]{8,32}$", RegexOptions.Compiled);

    public static string SharePath(string token) => ClaimPage + "?c=" + token;

    // ---------- 分销商：创建 / 列表 / 删除 ----------

    public async Task<CreateResult> CreateAsync(Coach coach, Introducer intro, CreateInput input, DateTime now)
    {
        var maker = new CoachCouponMakerService(db, config);
        string? blocked = await maker.BlockedAsync(coach, intro);
        if (blocked != null) throw new CoachException(blocked, 3);
        string name = CoachCouponMakerService.ValidateName(input.name);
        string req = (input.requestId ?? "").Trim();
        if (input.count < 1 || input.count > MaxBatchSize) throw new CoachException("一个批次 1～" + MaxBatchSize + " 张");
        if (req.Length == 0 || req.Length > 60) throw new CoachException("缺少请求编号，请重新提交");
        var prepared = await maker.PrepareManyAsync(coach, input.items);
        var (memo, end) = maker.DescribeMany(prepared, now);

        return await MiniTicketTransaction.Run(db, async () =>
        {
            // 同一份表单重试（网络不好）用同一个请求编号：不会重复建批次
            CoachCouponBatch? existing = await db.coachCouponBatch.AsNoTracking().FirstOrDefaultAsync(b => b.coach_id == coach.id && b.request_id == req);
            if (existing != null) return new CreateResult(existing, true);
            int open = await db.coachCouponBatch.AsNoTracking().Where(b => b.coach_id == coach.id && b.valid == 1).SumAsync(b => b.total_count - b.claimed_count);
            if (open + input.count > MaxOpenCapacity)
                throw new CoachException("还没领完的批次里剩余可领的券已有 " + open + " 张，等领取或删掉一些再做（合计上限 " + MaxOpenCapacity + " 张）");
            TicketTemplate template = await maker.FindOrCreateManyAsync(coach, prepared, name, memo, end);
            var batch = new CoachCouponBatch { coach_id = coach.id, template_id = template.id, token = CoachCardRules.NewToken(), total_count = input.count, request_id = req, create_date = now };
            db.coachCouponBatch.Add(batch);
            await db.SaveChangesAsync();
            db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("coach_coupon_batch", "batch", batch.id, "机构制作优惠券批次", coach.member_id, null, null,
                name + "：" + string.Join("、", prepared.Select(p => p.label + " " + p.mode + " " + p.value)) + " × " + input.count, intro.name));
            await db.SaveChangesAsync();
            return new CreateResult(batch, false);
        });
    }

    /// <summary>这位分销商所有没删除的批次（新的在前），带领取进度和领取人（头像、昵称、券码、有没有用）。</summary>
    public async Task<List<object>> ListAsync(Coach coach)
    {
        List<CoachCouponBatch> batches = await db.coachCouponBatch.AsNoTracking().Where(b => b.coach_id == coach.id && b.valid == 1)
            .OrderByDescending(b => b.create_date).ThenByDescending(b => b.id).ToListAsync();
        if (batches.Count == 0) return new();
        List<int> templateIds = batches.Select(b => b.template_id).Distinct().ToList();
        Dictionary<int, TicketTemplate> templates = await db.ticketTemplate.AsNoTracking().Where(t => templateIds.Contains(t.id)).ToDictionaryAsync(t => t.id);
        List<int> batchIds = batches.Select(b => b.id).ToList();
        List<CoachCouponClaim> claims = await db.coachCouponClaim.AsNoTracking().Where(c => batchIds.Contains(c.batch_id)).OrderBy(c => c.id).ToListAsync();
        List<int> memberIds = claims.Select(c => c.member_id).Distinct().ToList();
        List<string> codes = claims.Select(c => c.ticket_code).ToList();
        Dictionary<string, int> usedByCode = codes.Count == 0 ? new() : (await db.ticket.AsNoTracking().Where(t => codes.Contains(t.code)).Select(t => new { t.code, t.used }).ToListAsync())
            .ToDictionary(t => t.code, t => t.used);
        var people = await new CoachQueryService(db).LoadPeopleAsync(memberIds);
        var profiles = (await db.memberProfile.AsNoTracking().Where(p => memberIds.Contains(p.member_id)).Select(p => new { p.member_id, p.nick_name, p.avatar }).ToListAsync())
            .ToDictionary(p => p.member_id);
        string baseUrl = publicBaseUrl ?? new FileStorageOptions().PublicBaseUrl;
        return batches.Select(b =>
        {
            templates.TryGetValue(b.template_id, out TicketTemplate? t);
            var mine = claims.Where(c => c.batch_id == b.id).Select(c =>
            {
                profiles.TryGetValue(c.member_id, out var pr);
                people.TryGetValue(c.member_id, out var p);
                return new
                {
                    memberId = c.member_id, ticketCode = c.ticket_code, claimedAt = c.create_date, used = usedByCode.TryGetValue(c.ticket_code, out int u) && u == 1,
                    nickName = string.IsNullOrWhiteSpace(pr?.nick_name) ? null : pr!.nick_name!.Trim(),
                    avatarUrl = string.IsNullOrWhiteSpace(pr?.avatar) ? null : UploadPaths.PublicUrl(baseUrl, pr!.avatar!),
                    name = p.name, phoneTail = p.tail,
                };
            }).OrderByDescending(x => x.claimedAt).ToList();
            return (object)new
            {
                id = b.id, templateId = b.template_id, name = t?.name ?? "", memo = t?.memo ?? "", total = b.total_count, claimed = b.claimed_count,
                remaining = b.total_count - b.claimed_count, soldOut = b.claimed_count >= b.total_count, canDelete = b.claimed_count == 0 && mine.Count == 0,
                sharePath = SharePath(b.token), createDate = b.create_date, claimers = mine,
            };
        }).ToList();
    }

    /// <summary>分销商本人的批次（取二维码用）。</summary>
    public Task<CoachCouponBatch?> OwnBatchAsync(int coachId, int batchId) =>
        db.coachCouponBatch.AsNoTracking().FirstOrDefaultAsync(b => b.id == batchId && b.coach_id == coachId && b.valid == 1);

    /// <summary>
    /// 删除批次：只有没人领取过的能删；删除 = valid=0（软删除，留审计），二维码和小程序卡片立即失效。
    /// 和领取共用同一把发券锁，不会出现「刚删完又被人领走」。
    /// </summary>
    public Task<object> DeleteAsync(Coach coach, Introducer intro, int batchId, DateTime now) => MiniTicketTransaction.Run(db, async () =>
    {
        CoachCouponBatch batch = await db.coachCouponBatch.AsTracking().FirstOrDefaultAsync(b => b.id == batchId && b.coach_id == coach.id && b.valid == 1)
            ?? throw new CoachException("没有找到这个批次");
        int claimed = Math.Max(batch.claimed_count, await db.coachCouponClaim.AsNoTracking().CountAsync(c => c.batch_id == batchId));
        if (claimed > 0) throw new CoachException("这个批次已经有 " + claimed + " 位顾客领取过，不能删除");
        batch.valid = 0; batch.deleted_at = now; batch.update_date = now;
        await db.SaveChangesAsync();
        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("coach_coupon_batch", "batch", batch.id, "机构删除优惠券批次", coach.member_id, null, "valid=1", "valid=0（没人领取过）", intro.name));
        await db.SaveChangesAsync();
        return (object)new { id = batchId };
    });

    // ---------- 顾客：打开领取页 / 领取 ----------

    private static CouponShareView Fail(string state, string message) => new() { state = state, message = message };

    private sealed record Context(CouponShareView View, CoachCouponBatch? Batch, Coach? Coach, TicketTemplate? Template);

    private async Task<Context> EvaluateAsync(string? token, int? memberId, string openId, DateTime now)
    {
        string t = (token ?? "").Trim();
        if (!TokenPattern.IsMatch(t)) return new(Fail(CouponBatchState.Invalid, "这个领取链接无效"), null, null, null);
        CoachCouponBatch? batch = await db.coachCouponBatch.AsNoTracking().FirstOrDefaultAsync(b => b.token == t);
        if (batch == null || batch.valid != 1) return new(Fail(CouponBatchState.Invalid, "这批优惠券已经不能领取了"), null, null, null);
        Coach? coach = await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == batch.coach_id);
        TicketTemplate? tpl = await db.ticketTemplate.AsNoTracking().FirstOrDefaultAsync(x => x.id == batch.template_id && x.valid == 1);
        if (coach == null || coach.valid != 1 || tpl == null || !await DistributorAccess.CanIssueAsync(db, coach))
            return new(Fail(CouponBatchState.Invalid, "这批优惠券已经不能领取了"), batch, coach, tpl);
        string distributor = (await db.introducer.AsNoTracking().Where(i => i.coach_id == coach.id && i.valid == 1).Select(i => i.name).FirstOrDefaultAsync())?.Trim() ?? coach.display_name;
        var view = new CouponShareView
        {
            name = (tpl.name ?? "").Trim(), memo = (tpl.memo ?? "").Trim(), distributorName = distributor,
            total = batch.total_count, claimed = batch.claimed_count, remaining = Math.Max(0, batch.total_count - batch.claimed_count), validUntil = tpl.expire_date,
        };
        Apply(view, CouponBatchState.Claimable, "");
        Context Done(string state, string message) { Apply(view, state, message); return new(view, batch, coach, tpl); }

        if (tpl.expire_date != null && tpl.expire_date.Value.Date < now.Date) return Done(CouponBatchState.Expired, "这批优惠券已经过期了");
        if (memberId != null && memberId == coach.member_id) return Done(CouponBatchState.Self, "这是你自己发出的优惠券，转发给顾客即可，本人不能领取");
        if (memberId != null)
        {
            string? code = await db.coachCouponClaim.AsNoTracking()
                .Where(c => c.batch_id == batch.id && (c.member_id == memberId || (openId != "" && c.open_id == openId))).Select(c => c.ticket_code).FirstOrDefaultAsync();
            if (code != null) { view.code = code; return Done(CouponBatchState.ClaimedByMe, "你已经领过这批优惠券了"); }
        }
        if (batch.claimed_count >= batch.total_count) return Done(CouponBatchState.SoldOut, "这批优惠券已经被领完了");
        if (memberId != null && await db.coachCouponClaim.AsNoTracking()
                .AnyAsync(c => (c.member_id == memberId || (openId != "" && c.open_id == openId)) && c.claim_date == now.Date))
            return Done(CouponBatchState.DailyLimit, "你今天已经领过一张机构优惠券了，明天再来吧");
        return new(view, batch, coach, tpl);

        static void Apply(CouponShareView v, string state, string message) { v.state = state; v.message = message; }
    }

    /// <summary>领取页打开时的状态（不改数据）。memberId 为空 = 还没有会员（没授权过手机号）。</summary>
    public async Task<CouponShareView> GetShareAsync(string? token, int? memberId, string openId, DateTime now) =>
        (await EvaluateAsync(token, memberId, openId ?? "", now)).View;

    /// <summary>
    /// 领取：在发券锁 + Serializable 事务里，先按规则判断（领过 / 今天领过 / 领完 / 过期 / 停用），再原子占一个名额，
    /// 然后才生成券（券码、券记录、发放记录、领取记录、顾客归属）。创建批次时不生成券。
    /// </summary>
    public Task<CouponShareView> ClaimAsync(string? token, Member member, string openId, DateTime now) => MiniTicketTransaction.Run(db, async () =>
    {
        openId = (openId ?? "").Trim();
        Context ctx = await EvaluateAsync(token, member.id, openId, now);
        if (ctx.View.state != CouponBatchState.Claimable) return ctx.View;
        CoachCouponBatch batch = ctx.Batch!; Coach coach = ctx.Coach!; TicketTemplate tpl = ctx.Template!;

        // 原子占名额：同时满足「没删除」「没领满」才加一，加不上就是被别人抢先领完了
        int rows = await db.coachCouponBatch.Where(b => b.id == batch.id && b.valid == 1 && b.claimed_count < b.total_count)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.claimed_count, b => b.claimed_count + 1).SetProperty(b => b.update_date, (DateTime?)now));
        if (rows != 1) return Fail(CouponBatchState.SoldOut, "这批优惠券已经被领完了");

        var maker = new CoachCouponMakerService(db, config);
        string code = await maker.NewCodeAsync();
        string memberOpenId = openId != "" ? openId : (await db.memberSocialAccount.AsNoTracking()
            .Where(m => m.member_id == member.id && m.valid == 1 && m.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID).OrderByDescending(m => m.id).Select(m => m.num).FirstOrDefaultAsync()) ?? "";
        db.card.Add(new Card { card_no = code, is_ticket = 1, type = "" });
        db.ticket.Add(new Ticket
        {
            code = code, template_id = tpl.id, name = (tpl.name ?? "").Trim(), memo = (tpl.memo ?? "").Trim(), member_id = member.id, open_id = memberOpenId,
            start_date = now, expire_date = tpl.expire_date ?? CoachCardRules.SeasonEnd(now, config), used = 0, is_active = 1, valid = 1, shared = 0, printed = 0,
            channel = CoachCouponMakerService.Channel, staff_id = null, create_memo = "机构批次领取", miniapp_recept_path = "", create_date = now, accepted_time = now,
        });
        db.coachCouponGrant.Add(new CoachCouponGrant { coach_id = coach.id, ticket_code = code, template_id = tpl.id, grant_key = "batch" + batch.id + "-" + member.id, staff_id = 0, create_date = now });
        db.coachCouponClaim.Add(new CoachCouponClaim { batch_id = batch.id, coach_id = coach.id, member_id = member.id, open_id = memberOpenId, ticket_code = code, claim_date = now.Date, create_date = now });
        // 记一条「分销商 → 顾客」的转发日志：教练中心「我的券」据此把这张券显示成「已被领取」，并带上领取人
        string? coachOpenId = await db.memberSocialAccount.AsNoTracking()
            .Where(m => m.member_id == coach.member_id && m.valid == 1 && m.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID).OrderByDescending(m => m.id).Select(m => m.num).FirstOrDefaultAsync();
        if (!string.IsNullOrEmpty(coachOpenId) && !string.IsNullOrEmpty(memberOpenId) && coachOpenId != memberOpenId)
            db.ticketLog.Add(new TicketLog { code = code, sender_open_id = coachOpenId, accepter_open_id = memberOpenId, memo = "领取机构分享的优惠券批次", transact_time = now });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            // 唯一索引兜底（同一批次 / 同一天重复领取）：整个事务回滚，名额也不会被占走
            throw new CoachException("你今天已经领过一张机构优惠券了，明天再来吧");
        }
        // 领券也建立顾客归属（先到先得，已有归属不变）；失败不影响领券
        await new DistributorCustomerService(db).TryOnTicketAcceptedAsync(code, coach.member_id, member.id, now);

        CouponShareView view = ctx.View;
        view.state = CouponBatchState.ClaimedByMe; view.message = ""; view.code = code;
        view.claimed = batch.claimed_count + 1; view.remaining = Math.Max(0, batch.total_count - view.claimed);
        return view;
    });
}
