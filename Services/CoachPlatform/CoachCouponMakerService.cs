using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Services.CoachPlatform;

public sealed record MakeCouponInput(string? shop, string? tier, string? mode, double value, int count, string? requestId);

/// <summary>
/// 机构自己生成优惠券（用户 2026-11-15 定：根据管理员设的底价生成）。
/// 一次生成 = 一个只给这批券用的券模板（隐藏、养护券）+ 模板上针对「该店单项 / 双项」养护商品的价格规则（一口价或折扣率，
/// 复用现有 product_ticket_template 计价，开单选券和核销都不用改）+ N 张券放进机构自己的券包，并登记成「分销商发出的券」
/// （coach_coupon_grant）：这样转发、顾客接券建立归属、按券统计消费都沿用现有流程。
/// 券价不能低于管理员给这一格设的底价（和机构自己制作会员卡共用同一份底价，见 CoachFloorRules）。
/// </summary>
public sealed class CoachCouponMakerService(ApplicationDBContext db, IConfiguration config)
{
    public const string Channel = "机构自制券";
    public const int MaxPerRequest = 50;
    /// <summary>名下还没用完、没过期的自制券上限，防止无限生成。</summary>
    public const int MaxOutstanding = 500;

    private static readonly Dictionary<string, string> Short = new() { ["万龙服务中心"] = "万龙", ["崇礼旗舰店"] = "崇礼" };

    private static string? Blocked(Coach coach, Introducer intro)
    {
        if (intro.type != IntroducerRules.TypeOrg) return "只有机构可以自己生成优惠券";
        if (coach.valid != 1) return "你的分销商身份已停用，不能生成优惠券";
        return null;
    }

    private async Task<string?> BlockedAsync(Coach coach, Introducer intro) =>
        Blocked(coach, intro) ?? (await DistributorAccess.CanIssueAsync(db, coach) ? null : "你的分销商身份已停用或存在冲突，不能生成优惠券");

    private async Task<Product?> CareProductAsync(string shop, string name)
    {
        int? shopId = await db.shop.AsNoTracking().Where(s => s.name.Trim() == shop).Select(s => (int?)s.id).FirstOrDefaultAsync();
        if (shopId == null) return null;
        int? careCat = await db.category.AsNoTracking().Where(c => c.code == CareProductRules.CareCategoryCode && c.valid == 1).Select(c => (int?)c.id).FirstOrDefaultAsync();
        return await db.product.AsNoTracking()
            .Where(p => p.shop_id == shopId && (p.category_id == careCat || p.category_code == CareProductRules.CareCategoryCode) && p.valid == 1 && p.name.Trim() == name)
            .OrderBy(p => p.id).FirstOrDefaultAsync();
    }

    private IQueryable<Ticket> OutstandingQuery(int coachId, DateTime now) =>
        from g in db.coachCouponGrant.AsNoTracking()
        join t in db.ticket.AsNoTracking() on g.ticket_code equals t.code
        where g.coach_id == coachId && t.channel == Channel && t.used == 0 && t.valid == 1 && (t.expire_date == null || t.expire_date >= now)
        select t;

    /// <summary>「制作优惠券」页的数据：能做的格子（来自管理员的底价）、额度、最近做过的券。</summary>
    public async Task<object> GetMakerViewAsync(Coach coach, Introducer intro)
    {
        DateTime now = DateTime.Now;
        string? blocked = await BlockedAsync(coach, intro);
        List<CoachPriceFloor> floors = await new CoachFloorService(db, config).GetFloorsAsync(coach.id);
        var cells = new List<object>();
        foreach (string shop in CoachCardRules.CareShops)
            foreach (string tier in new[] { CoachCardRules.TierOne, CoachCardRules.TierTwo })
            {
                CoachPriceFloor? f = floors.FirstOrDefault(x => x.biz_type == CoachCardRules.BizCare && x.shop == shop && x.tier == tier);
                bool open = CoachFloorRules.IsOpen(f);
                bool productReady = open && await CareProductAsync(shop, tier) != null;
                cells.Add(new { shop, tier, open, productReady, minRate = f?.min_rate, minFixed = f?.min_fixed });
            }
        // 最近做过的券：按券模板汇总（数量、已用）
        var rows = await (from g in db.coachCouponGrant.AsNoTracking()
                          join t in db.ticket.AsNoTracking() on g.ticket_code equals t.code
                          where g.coach_id == coach.id && t.channel == Channel
                          select new { g.template_id, t.name, t.used, g.create_date }).ToListAsync();
        var recent = rows.GroupBy(r => r.template_id).Select(g => new
        {
            templateId = g.Key, name = g.First().name, count = g.Count(), used = g.Count(x => x.used == 1), created = g.Min(x => x.create_date),
        }).OrderByDescending(x => x.created).Take(10).ToList();
        return new
        {
            canMake = blocked == null, reason = blocked ?? "", name = coach.display_name,
            validUntil = CoachCardRules.SeasonEnd(now, config), maxPerRequest = MaxPerRequest, maxOutstanding = MaxOutstanding,
            outstanding = await OutstandingQuery(coach.id, now).CountAsync(),
            cells, recent,
        };
    }

    public sealed record MakeResult(int templateId, string name, string memo, int created, bool repeated);

    public async Task<MakeResult> MakeAsync(Coach coach, Introducer intro, MakeCouponInput input, DateTime now)
    {
        string? blocked = await BlockedAsync(coach, intro);
        if (blocked != null) throw new CoachException(blocked, 3);
        string shop = (input.shop ?? "").Trim(), tier = (input.tier ?? "").Trim(), mode = (input.mode ?? "").Trim(), req = (input.requestId ?? "").Trim();
        if (!CoachCardRules.CareShops.Contains(shop)) throw new CoachException("优惠券只能用于万龙服务中心和崇礼旗舰店的养护");
        if (!CoachCardRules.IsValidTier(tier)) throw new CoachException("优惠券档位只能是单项或双项");
        if (mode != CoachCardRules.ModeRate && mode != CoachCardRules.ModeFixed) throw new CoachException("请选择折扣或一口价");
        if (!CoachCardRules.TryValidateCarePrice(mode, input.value, out string valueError)) throw new CoachException(valueError);
        if (input.count < 1 || input.count > MaxPerRequest) throw new CoachException("一次生成 1～" + MaxPerRequest + " 张");
        if (req.Length == 0 || req.Length > 60) throw new CoachException("缺少请求编号，请重新提交");

        string label = shop + " " + tier;
        CoachPriceFloor? floor = (await new CoachFloorService(db, config).GetFloorsAsync(coach.id))
            .FirstOrDefault(x => x.biz_type == CoachCardRules.BizCare && x.shop == shop && x.tier == tier);
        string? floorError = CoachFloorRules.CheckCare(floor, mode, input.value, label);
        if (floorError != null) throw new CoachException(floorError);

        Product? baseProduct = await CareProductAsync(shop, tier);
        if (baseProduct == null) throw new CoachException(label + "：这家店还没有配置养护价目，请联系管理员");
        Product? urgentProduct = await CareProductAsync(shop, tier + "加急");

        // 重复提交（同一个请求编号）：不再生成，返回上次的结果
        var existing = await db.coachCouponGrant.AsNoTracking().Where(g => g.coach_id == coach.id && g.grant_key.StartsWith(req + "-")).FirstOrDefaultAsync();
        if (existing != null)
        {
            TicketTemplate old = await db.ticketTemplate.AsNoTracking().FirstAsync(t => t.id == existing.template_id);
            return new MakeResult(old.id, old.name, old.memo, 0, true);
        }
        if (await OutstandingQuery(coach.id, now).CountAsync() + input.count > MaxOutstanding)
            throw new CoachException("名下还没用完的自制优惠券已有很多，用掉或过期一些再生成（上限 " + MaxOutstanding + " 张）");

        Member member = await db.member.Where(m => m.id == coach.member_id)
            .Include(m => m.memberSocialAccounts.Where(a => a.valid == 1)).AsNoTracking().FirstOrDefaultAsync()
            ?? throw new CoachException("分销商的会员不存在");
        string openId = member.wechatMiniOpenId ?? "";
        DateTime end = CoachCardRules.SeasonEnd(now, config);
        // 生产库 ticket_template.name 是 varchar(50)、Chinese_PRC_CI_AS（汉字 2 字节）：名字按 48 字节截，别用 ¥ 这类
        // GBK 里没有的符号（会变成 ?），价格写「68元」。机构名放在前面，太长就截机构名。
        string priceText = mode == CoachCardRules.ModeFixed ? Math.Round(input.value, 2).ToString("0.##") + "元一口价" : Math.Round(input.value * 10, 2).ToString("0.##") + "折";
        string suffix = " " + (Short.TryGetValue(shop, out string? s) ? s : shop) + tier + priceText + "券";
        string name = FitBytes(intro.name.Trim(), 48 - ByteLen(suffix)) + suffix;
        string memo = "凭券在" + shop + "做" + tier + "养护，养护服务费" + (mode == CoachCardRules.ModeFixed ? "按" + Math.Round(input.value, 2).ToString("0.##") + "元收取" : "享" + Math.Round(input.value * 10, 2).ToString("0.##") + "折")
            + "；加急加价、维修费照收；每张券限用一次，有效期至" + end.ToString("yyyy-MM-dd") + "。";

        return await MiniTicketTransaction.Run(db, async () =>
        {
            // 同一位分销商、同一档位、同一价格、同一雪季的券共用一个模板：多次生成不会让模板和价格规则越积越多
            List<int> mine = await db.coachCouponGrant.AsNoTracking().Where(g => g.coach_id == coach.id).Select(g => g.template_id).Distinct().ToListAsync();
            TicketTemplate? template = await db.ticketTemplate.AsNoTracking()
                .Where(t => mine.Contains(t.id) && t.valid == 1 && t.name == name && t.expire_date == end).OrderBy(t => t.id).FirstOrDefaultAsync();
            if (template == null)
            {
                template = new TicketTemplate
                {
                    type = "养护券", biz_type = "养护", name = name, memo = memo, hide = 1, sharable = 0, miniapp_recept_path = "",
                    expire_date = end, valid = 1,
                };
                db.ticketTemplate.Add(template);
                await db.SaveChangesAsync();
                // 单项 / 双项基础商品的价格规则；有加急商品的店，加急商品同价：折扣率一样，一口价 = 券价 + 加急加价（生成时的价目差）
                AddRule(template.id, baseProduct.id, mode, input.value);
                if (urgentProduct != null)
                {
                    double surcharge = Math.Max(0, Math.Round(urgentProduct.sale_price - baseProduct.sale_price, 2));
                    AddRule(template.id, urgentProduct.id, mode, mode == CoachCardRules.ModeFixed ? Math.Round(input.value + surcharge, 2) : input.value);
                }
                await db.SaveChangesAsync();
            }
            for (int seq = 1; seq <= input.count; seq++)
            {
                string code = await NewCodeAsync();
                db.card.Add(new Card { card_no = code, is_ticket = 1, type = "" });
                db.ticket.Add(new Ticket
                {
                    code = code, template_id = template.id, name = name, memo = memo, member_id = member.id, open_id = openId,
                    start_date = now, expire_date = end, used = 0, is_active = 1, valid = 1, shared = 0, printed = 0,
                    channel = Channel, staff_id = null, create_memo = "机构自己生成", miniapp_recept_path = "", create_date = now,
                });
                db.coachCouponGrant.Add(new CoachCouponGrant { coach_id = coach.id, ticket_code = code, template_id = template.id, grant_key = req + "-" + coach.id + "-" + seq, staff_id = 0, create_date = now });
                await db.SaveChangesAsync();
            }
            db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("ticket_template", "coupon", template.id, "机构自制优惠券", coach.member_id, null, null,
                label + " " + mode + " " + input.value + " × " + input.count, intro.name));
            await db.SaveChangesAsync();
            return new MakeResult(template.id, name, memo, input.count, false);
        });
    }

    /// <summary>按 GBK 近似计字节：非 ASCII 算 2 字节。</summary>
    public static int ByteLen(string text) => text.Sum(c => c > 127 ? 2 : 1);

    public static string FitBytes(string text, int maxBytes)
    {
        if (maxBytes <= 0) return "";
        var sb = new System.Text.StringBuilder();
        int used = 0;
        foreach (char c in text)
        {
            int w = c > 127 ? 2 : 1;
            if (used + w > maxBytes) break;
            sb.Append(c); used += w;
        }
        return sb.ToString();
    }

    private void AddRule(int templateId, int productId, string mode, double value) => db.productTicketTemplate.Add(new ProductTicketTemplate
    {
        ticket_template_id = templateId, product_id = productId, valid = true, create_date = DateTime.Now,
        fixed_price = mode == CoachCardRules.ModeFixed ? value : null,
        discount_rate = mode == CoachCardRules.ModeRate ? value : null,
    });

    private async Task<string> NewCodeAsync()
    {
        for (int i = 0; i < 1000; i++)
        {
            string code = Util.GetRandomCode(9);
            bool used = await db.card.AsNoTracking().AnyAsync(c => c.card_no == code) || await db.ticket.AsNoTracking().AnyAsync(t => t.code == code);
            if (!used) return code;
        }
        throw new CoachException("生成券码失败，请重试");
    }
}
