using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Storage;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Services.CoachPlatform;

public sealed record MakeCouponInput(string? shop, string? tier, string? mode, double value, int count, string? requestId);

/// <summary>
/// 分销商自己生成单张优惠券：教练 / KOL / 机构都可以（用户 2026-11-17 定：只有机构能发整个雪季的会籍卡，
/// 教练和 KOL 只能发单张优惠券）。券价受管理员设的**优惠券底价**（coach_coupon_floor，原来的「价格设置」）约束：
/// 一口价不能低于、折扣不能低于、减免不能超过；底价里没设的方式没有开放，界面不显示。
/// （2026-11-15 的版本只给机构、按会籍卡底价限制；现已改为所有分销商、按优惠券底价。）
/// 一次生成 = 一个只给这批券用的券模板（隐藏、养护券）+ 模板上针对「该店单项 / 双项」养护商品的价格规则（一口价或折扣率，
/// 复用现有 product_ticket_template 计价，开单选券和核销都不用改）+ N 张券放进机构自己的券包，并登记成「分销商发出的券」
/// （coach_coupon_grant）：这样转发、顾客接券建立归属、按券统计消费都沿用现有流程。
/// 券价不能低于管理员给这一格设的底价（和机构自己制作会员卡共用同一份底价，见 CoachFloorRules）。
/// </summary>
public sealed class CoachCouponMakerService(ApplicationDBContext db, IConfiguration config)
{
    /// <summary>券的渠道名沿用「机构自制券」（生产库里已有这个值），教练 / KOL 自己生成的也用它。</summary>
    public const string Channel = "机构自制券";
    public const int MaxPerRequest = 50;
    /// <summary>名下还没用完、没过期的自制券上限，防止无限生成。</summary>
    public const int MaxOutstanding = 500;

    private static readonly Dictionary<string, string> Short = new() { ["万龙服务中心"] = "万龙", ["崇礼旗舰店"] = "崇礼" };

    internal static string? Blocked(Coach coach, Introducer intro)
    {
        if (!DistributorMenuRules.HasMenu(intro.type)) return "只有教练、KOL 和机构可以生成优惠券";
        if (coach.valid != 1) return "你的分销商身份已停用，不能生成优惠券";
        return null;
    }

    internal async Task<string?> BlockedAsync(Coach coach, Introducer intro) =>
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
              && !db.coachCouponClaim.Any(c => c.ticket_code == t.code)   // 批次里顾客领走的券已经是顾客的了，不算分销商自己手里的
        select t;

    /// <summary>「制作优惠券」页的数据：能做的格子和每格开放的方式（来自管理员的优惠券底价，没开放的不返回方式）、额度、最近做过的券。</summary>
    public async Task<object> GetMakerViewAsync(Coach coach, Introducer intro)
    {
        DateTime now = DateTime.Now;
        string? blocked = await BlockedAsync(coach, intro);
        List<CoachCouponFloor> floors = await new CoachFloorService(db, config).GetCouponFloorsAsync(coach.id);
        var cells = new List<object>();
        foreach (string shop in CoachCardRules.CareShops)
            foreach (string tier in new[] { CoachCardRules.TierOne, CoachCardRules.TierTwo })
            {
                CoachCouponFloor? f = floors.FirstOrDefault(x => x.shop == shop && x.tier == tier);
                List<string> modes = CoachFloorRules.CouponModes(f);
                bool open = modes.Count > 0;
                bool productReady = open && await CareProductAsync(shop, tier) != null;
                cells.Add(new { shop, tier, open, productReady, modes, minFixed = f?.min_fixed, minRate = f?.min_rate, maxOff = f?.max_off });
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
            // 分享批次（2026-11-18）：每做一次是一个批次，领取时才生成券
            batches = await new CoachCouponBatchService(db, config, FileStorageOptions.From(config).PublicBaseUrl).ListAsync(coach),
            maxBatchSize = CoachCouponBatchService.MaxBatchSize, maxOpenCapacity = CoachCouponBatchService.MaxOpenCapacity,
        };
    }

    public sealed record MakeResult(int templateId, string name, string memo, int created, bool repeated);

    /// <summary>校验后的券参数（门店 / 档位 / 方式 / 数值）和这一格的养护商品，批次创建和直接生成共用。</summary>
    public sealed record Prepared(string shop, string tier, string mode, double value, string label, Product baseProduct, Product? urgentProduct);

    /// <summary>门店、档位、方式、数值的基本校验（不查库）。</summary>
    public static (string shop, string tier, string mode) ValidateInput(string? shopIn, string? tierIn, string? modeIn, double value)
    {
        string shop = (shopIn ?? "").Trim(), tier = (tierIn ?? "").Trim(), mode = (modeIn ?? "").Trim();
        if (!CoachCardRules.CareShops.Contains(shop)) throw new CoachException("优惠券只能用于万龙服务中心和崇礼旗舰店的养护");
        if (!CoachCardRules.IsValidTier(tier)) throw new CoachException("优惠券档位只能是单项或双项");
        if (!CoachFloorRules.TryValidateCouponValue(mode, value, out string valueError)) throw new CoachException(valueError);
        return (shop, tier, mode);
    }

    /// <summary>券价不能越过管理员给这一格设的优惠券底价；这家店要有养护价目。</summary>
    public async Task<Prepared> PrepareAsync(Coach coach, string shop, string tier, string mode, double value)
    {
        string label = shop + " " + tier;
        CoachCouponFloor? floor = (await new CoachFloorService(db, config).GetCouponFloorsAsync(coach.id))
            .FirstOrDefault(x => x.shop == shop && x.tier == tier);
        string? floorError = CoachFloorRules.CheckCoupon(floor, mode, value, label);
        if (floorError != null) throw new CoachException(floorError);

        Product? baseProduct = await CareProductAsync(shop, tier);
        if (baseProduct == null) throw new CoachException(label + "：这家店还没有配置养护价目，请联系管理员");
        Product? urgentProduct = await CareProductAsync(shop, tier + "加急");
        return new Prepared(shop, tier, mode, value, label, baseProduct, urgentProduct);
    }

    /// <summary>券模板的名字、说明和有效期。生产库 ticket_template.name 是 varchar(50)、Chinese_PRC_CI_AS（汉字 2 字节）：
    /// 名字按 48 字节截，别用 ¥ 这类 GBK 里没有的符号（会变成 ?），价格写「68元」。机构名放在前面，太长就截机构名。</summary>
    public (string name, string memo, DateTime end) Describe(Introducer intro, Prepared p, DateTime now)
    {
        DateTime end = CoachCardRules.SeasonEnd(now, config);
        string value = Math.Round(p.value, 2).ToString("0.##"), rateText = Math.Round(p.value * 10, 2).ToString("0.##");
        string priceText = p.mode == CoachCardRules.ModeFixed ? value + "元一口价" : p.mode == CoachCardRules.ModeRate ? rateText + "折" : "减" + value + "元";
        string suffix = " " + (Short.TryGetValue(p.shop, out string? s) ? s : p.shop) + p.tier + priceText + "券";
        string name = FitBytes(intro.name.Trim(), 48 - ByteLen(suffix)) + suffix;
        string feeText = p.mode == CoachCardRules.ModeFixed ? "按" + value + "元收取" : p.mode == CoachCardRules.ModeRate ? "享" + rateText + "折" : "减" + value + "元";
        string memo = "凭券在" + p.shop + "做" + p.tier + "养护，养护服务费" + feeText
            + "；加急加价、维修费照收；每张券限用一次，有效期至" + end.ToString("yyyy-MM-dd") + "。";
        return (name, memo, end);
    }

    /// <summary>旧的「一次生成一种券」用：单格版本，同样的名字 + 说明 + 有效期共用一个模板。必须在 MiniTicketTransaction 里调用。</summary>
    public Task<TicketTemplate> FindOrCreateTemplateAsync(Coach coach, Prepared p, string name, string memo, DateTime end) =>
        FindOrCreateManyAsync(coach, new List<Prepared> { p }, name, memo, end);

    /// <summary>一张优惠券里的一格：这个服务（门店 × 单项 / 双项）按什么方式、什么价。</summary>
    public sealed record CouponItemInput(string? shop, string? tier, string? mode, double value);

    /// <summary>
    /// 一张优惠券可以适用于不同的服务（用户 2026-11-18，和做会员卡选项目一样）：一批里列出若干格（万龙单项、万龙双项、崇礼单项……），
    /// 每格各自选方式和价格；留空的格子这张券不适用。每一格都要过管理员给这一格设的优惠券底价，有问题一次说清所有问题。
    /// </summary>
    public async Task<List<Prepared>> PrepareManyAsync(Coach coach, List<CouponItemInput>? items)
    {
        if (items == null || items.Count == 0) throw new CoachException("请至少给一个服务设置价格");
        if (items.Count > CoachCardRules.CareShops.Count() * 2) throw new CoachException("服务项目重复或太多");
        var seen = new HashSet<(string, string)>();
        var prepared = new List<Prepared>();
        var problems = new List<string>();
        foreach (CouponItemInput item in items)
        {
            try
            {
                var (shop, tier, mode) = ValidateInput(item.shop, item.tier, item.mode, item.value);
                if (!seen.Add((shop, tier))) throw new CoachException(shop + " " + tier + "：重复了");
                prepared.Add(await PrepareAsync(coach, shop, tier, mode, item.value));
            }
            catch (CoachException e) { problems.Add(e.Message); }
        }
        if (problems.Count > 0) throw new CoachException(string.Join("；", problems));
        return prepared;
    }

    /// <summary>券名（模板名）：用户自己起，生产库 ticket_template.name 是 varchar(50)、Chinese_PRC_CI_AS（汉字 2 字节），最多 48 字节；
    /// 不能带表情、¥ 这类 GBK 里没有的符号（入库会变成 ?）。</summary>
    public static string ValidateName(string? raw)
    {
        string name = (raw ?? "").Trim();
        if (name.Length == 0) throw new CoachException("请填写券名");
        if (name.Any(c => char.IsControl(c) || char.IsSurrogate(c) || c == '¥' || c == '￥'))
            throw new CoachException("券名里不要用表情、¥ 等特殊符号，写成「68元」这样");
        if (ByteLen(name) > 48) throw new CoachException("券名太长了，最多 24 个汉字");
        return name;
    }

    /// <summary>多个服务的说明：每格一句，凭券任选其中一项服务使用。</summary>
    public (string memo, DateTime end) DescribeMany(List<Prepared> items, DateTime now)
    {
        DateTime end = CoachCardRules.SeasonEnd(now, config);
        string Piece(Prepared p)
        {
            string value = Math.Round(p.value, 2).ToString("0.##"), rateText = Math.Round(p.value * 10, 2).ToString("0.##");
            string fee = p.mode == CoachCardRules.ModeFixed ? "养护服务费按" + value + "元收取" : p.mode == CoachCardRules.ModeRate ? "享" + rateText + "折" : "减" + value + "元";
            return p.shop + p.tier + "养护：" + fee;
        }
        string memo = "凭券可用于：" + string.Join("；", items.Select(Piece)) + "。每张券限用一次，在以上服务中任选其一使用；加急加价、维修费照收；有效期至" + end.ToString("yyyy-MM-dd") + "。";
        return (memo, end);
    }

    /// <summary>
    /// 券模板 + 每个服务的价格规则（复用现有 product_ticket_template，开单选券和核销计价都不用改）：每一格给该店单项 / 双项基础商品一条规则，
    /// 有加急商品的店，加急商品同价：折扣率一样，一口价 = 券价 + 加急加价（生成时的价目差）。必须在 MiniTicketTransaction 里调用。
    /// 同一位分销商名字、说明（说明里写了每个服务的价格）、有效期都一样的，共用一个模板，多个批次不会让模板越积越多。
    /// </summary>
    public async Task<TicketTemplate> FindOrCreateManyAsync(Coach coach, List<Prepared> items, string name, string memo, DateTime end)
    {
        List<int> mine = await db.coachCouponGrant.AsNoTracking().Where(g => g.coach_id == coach.id).Select(g => g.template_id).Distinct().ToListAsync();
        mine.AddRange(await db.coachCouponBatch.AsNoTracking().Where(b => b.coach_id == coach.id).Select(b => b.template_id).Distinct().ToListAsync());
        TicketTemplate? template = await db.ticketTemplate.AsNoTracking()
            .Where(t => mine.Contains(t.id) && t.valid == 1 && t.name == name && t.memo == memo && t.expire_date == end).OrderBy(t => t.id).FirstOrDefaultAsync();
        if (template != null) return template;
        template = new TicketTemplate
        {
            type = "养护券", biz_type = "养护", name = name, memo = memo, hide = 1, sharable = 0, miniapp_recept_path = "",
            expire_date = end, valid = 1,
        };
        db.ticketTemplate.Add(template);
        await db.SaveChangesAsync();
        foreach (Prepared p in items)
        {
            AddRule(template.id, p.baseProduct.id, p.mode, p.value);
            if (p.urgentProduct != null)
            {
                double surcharge = Math.Max(0, Math.Round(p.urgentProduct.sale_price - p.baseProduct.sale_price, 2));
                AddRule(template.id, p.urgentProduct.id, p.mode, p.mode == CoachCardRules.ModeFixed ? Math.Round(p.value + surcharge, 2) : p.value);
            }
        }
        await db.SaveChangesAsync();
        return template;
    }

    public async Task<MakeResult> MakeAsync(Coach coach, Introducer intro, MakeCouponInput input, DateTime now)
    {
        string? blocked = await BlockedAsync(coach, intro);
        if (blocked != null) throw new CoachException(blocked, 3);
        var (shop, tier, mode) = ValidateInput(input.shop, input.tier, input.mode, input.value);
        string req = (input.requestId ?? "").Trim();
        if (input.count < 1 || input.count > MaxPerRequest) throw new CoachException("一次生成 1～" + MaxPerRequest + " 张");
        if (req.Length == 0 || req.Length > 60) throw new CoachException("缺少请求编号，请重新提交");
        Prepared prepared = await PrepareAsync(coach, shop, tier, mode, input.value);

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
        var (name, memo, end) = Describe(intro, prepared, now);

        return await MiniTicketTransaction.Run(db, async () =>
        {
            TicketTemplate template = await FindOrCreateTemplateAsync(coach, prepared, name, memo, end);
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
                prepared.label + " " + mode + " " + input.value + " × " + input.count, intro.name));
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
        discount_amount = mode == CoachFloorRules.ModeOff ? value : null,   // 减免：立减金额，加急商品同额
    });

    internal async Task<string> NewCodeAsync()
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
