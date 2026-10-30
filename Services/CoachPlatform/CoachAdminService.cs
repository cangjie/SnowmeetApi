using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Services.CoachPlatform;

public sealed record CarePriceInput(string? shop, string? tier, string? mode, double value);
public sealed record PricingInput(List<CarePriceInput>? care, double? rental_rate);

/// <summary>管理员后台「教练列表」背后的业务：添加/停用教练、设置价格、批量给教练发优惠券。</summary>
public sealed class CoachAdminService(ApplicationDBContext db, IConfiguration config)
{
    public const string CouponChannel = "教练发券";

    public async Task<object> LookupMemberAsync(string? cell)
    {
        string c = (cell ?? "").Trim();
        if (c.Length < 6) throw new CoachException("请输入完整的手机号");
        Member? member = await FindMemberByCellAsync(c);
        if (member == null) return new { exists = false, cell = c };
        Models.Coach? coach = await db.coach.AsNoTracking().FirstOrDefaultAsync(x => x.member_id == member.id && x.valid == 1);
        return new
        {
            exists = true, memberId = member.id, name = CoachCardRules.StudentName(member.real_name, null),
            phoneTail = CoachCardRules.PhoneTail(c), isCoach = coach != null, coachId = coach?.id,
        };
    }

    private async Task<Member?> FindMemberByCellAsync(string cell)
    {
        int? memberId = await db.memberSocialAccount.AsNoTracking()
            .Where(m => m.valid == 1 && m.type == MemberSocialAccount.TYPE_CELL && m.num == cell)
            .OrderByDescending(m => m.id).Select(m => (int?)m.member_id).FirstOrDefaultAsync();
        if (memberId == null) return null;
        return await db.member.AsNoTracking().FirstOrDefaultAsync(m => m.id == memberId && m.valid == 1);
    }

    /// <summary>添加教练：按会员 id 或手机号。手机号还没有会员时同时创建会员（与店员「手机号注册会员」同口径）。</summary>
    public async Task<Models.Coach> AddCoachAsync(Staff op, int? memberId, string? cell, string? displayName)
    {
        Member? member = null;
        string c = (cell ?? "").Trim();
        if (memberId != null) member = await db.member.AsNoTracking().FirstOrDefaultAsync(m => m.id == memberId && m.valid == 1);
        else if (c.Length >= 6) member = await FindMemberByCellAsync(c);
        else throw new CoachException("请提供手机号或会员");
        if (member == null)
        {
            if (memberId != null || c.Length < 6) throw new CoachException("没有找到这个会员");
            member = new Member { id = 0, real_name = (displayName ?? "").Trim(), gender = "", source = "后台添加教练", valid = 1, following_wechat = 0 };
            await db.member.AddAsync(member);
            await db.SaveChangesAsync();
            MemberSocialAccount? msa = await new MemberController(db, config).BindMemberMainCellNum(member.id, c, "后台添加教练", op);
            if (msa == null) throw new CoachException("手机号已被其它会员占用");
        }
        string name = (displayName ?? "").Trim();
        if (name == "") name = !string.IsNullOrWhiteSpace(member.real_name) ? member.real_name.Trim() : "教练";
        if (name.Length > 100) throw new CoachException("教练显示名太长");
        DateTime now = DateTime.Now;
        Models.Coach? existing = await db.coach.AsNoTracking().FirstOrDefaultAsync(x => x.member_id == member.id);
        if (existing != null)
        {
            if (existing.valid == 1) throw new CoachException("这个会员已经是教练了");
            // 曾经停用过：恢复原记录，价格和历史学员卡保留
            existing.valid = 1; existing.display_name = name; existing.update_date = now;
            db.Entry(existing).State = EntityState.Modified;
            await db.SaveChangesAsync();
            return existing;
        }
        Models.Coach coach = new() { member_id = member.id, display_name = name, valid = 1, created_by_staff_id = op.id, create_date = now };
        db.coach.Add(coach);
        await db.SaveChangesAsync();
        return coach;
    }

    public async Task<Models.Coach> UpdateCoachAsync(int coachId, string? displayName, bool? valid)
    {
        Models.Coach coach = await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == coachId) ?? throw new CoachException("没有找到这位教练");
        if (displayName != null)
        {
            string n = displayName.Trim();
            if (n == "" || n.Length > 100) throw new CoachException("教练显示名不能为空，且不超过 100 个字");
            coach.display_name = n;
        }
        if (valid != null) coach.valid = valid.Value ? 1 : 0;
        coach.update_date = DateTime.Now;
        db.Entry(coach).State = EntityState.Modified;
        await db.SaveChangesAsync();
        return coach;
    }

    /// <summary>
    /// 设置价格：养护 4 格（2 店 × 单项/双项）+ 租赁一个折扣率。整体替换；没传的格子记为「无」。
    /// 只影响之后领取的新卡，已领的卡保持领取时的价格。
    /// </summary>
    public async Task<Models.Coach> SetPricingAsync(int coachId, PricingInput input)
    {
        Models.Coach coach = await db.coach.AsNoTracking().FirstOrDefaultAsync(c => c.id == coachId) ?? throw new CoachException("没有找到这位教练");
        if (!CoachCardRules.TryValidateRentalRate(input.rental_rate, out string rentalError)) throw new CoachException(rentalError);
        Dictionary<(string, string), CarePriceInput> wanted = new();
        foreach (CarePriceInput p in input.care ?? new())
        {
            string shop = (p.shop ?? "").Trim(), tier = (p.tier ?? "").Trim();
            if (!CoachCardRules.CareShops.Contains(shop)) throw new CoachException("养护价格只能设置万龙服务中心和崇礼旗舰店");
            if (!CoachCardRules.IsValidTier(tier)) throw new CoachException("养护档位只能是单项或双项");
            if (!CoachCardRules.TryValidateCarePrice(p.mode, p.value, out string error)) throw new CoachException(shop + tier + "：" + error);
            wanted[(shop, tier)] = p;
        }
        List<CoachCarePrice> existing = await db.coachCarePrice.AsNoTracking().Where(x => x.coach_id == coachId).ToListAsync();
        foreach (string shop in CoachCardRules.CareShops)
        {
            foreach (string tier in new[] { CoachCardRules.TierOne, CoachCardRules.TierTwo })
            {
                wanted.TryGetValue((shop, tier), out CarePriceInput? p);
                string mode = p?.mode ?? CoachCardRules.ModeNone;
                double value = mode == CoachCardRules.ModeNone ? 0 : (p?.value ?? 0);
                CoachCarePrice? row = existing.FirstOrDefault(x => x.shop == shop && x.tier == tier);
                if (row == null)
                {
                    db.coachCarePrice.Add(new CoachCarePrice { coach_id = coachId, shop = shop, tier = tier, mode = mode, value = value });
                }
                else if (row.mode != mode || row.value != value)
                {
                    row.mode = mode; row.value = value; db.Entry(row).State = EntityState.Modified;
                }
            }
        }
        coach.rental_rate = input.rental_rate; coach.update_date = DateTime.Now;
        db.Entry(coach).State = EntityState.Modified;
        await db.SaveChangesAsync();
        return coach;
    }

    public async Task<List<CoachCarePrice>> GetCarePricesAsync(int coachId) =>
        await db.coachCarePrice.AsNoTracking().Where(p => p.coach_id == coachId).OrderBy(p => p.shop).ThenBy(p => p.tier).ToListAsync();

    public sealed class GrantResult
    {
        public int granted { get; set; }
        public int skipped { get; set; }
        public List<object> perCoach { get; set; } = new();
    }

    /// <summary>
    /// 批量发优惠券给教练：每位教练 countEach 张，发到教练的会员名下，教练再转发给学员。
    /// 幂等：grant_key = requestId-教练id-序号，同一个 requestId 重试时已发的跳过、没发的补发。
    /// 不改 MemberAdminController.GrantCoupon（单个会员发券），这里单独实现，保留它的 card 行（is_ticket=1）和 staff_id 口径。
    /// </summary>
    public async Task<GrantResult> GrantCouponsAsync(Staff op, List<int>? coachIds, int templateId, int countEach, string? requestId)
    {
        List<int> ids = (coachIds ?? new()).Distinct().ToList();
        string req = (requestId ?? "").Trim();
        if (ids.Count == 0) throw new CoachException("请选择要发券的教练");
        if (ids.Count > CoachCardRules.MaxGrantCoaches) throw new CoachException("一次最多给 " + CoachCardRules.MaxGrantCoaches + " 位教练发券");
        if (countEach < 1 || countEach > CoachCardRules.MaxGrantPerCoach) throw new CoachException("每位教练发 1～" + CoachCardRules.MaxGrantPerCoach + " 张");
        if (ids.Count * countEach > CoachCardRules.MaxGrantTotal) throw new CoachException("一次最多发 " + CoachCardRules.MaxGrantTotal + " 张");
        if (req.Length == 0 || req.Length > 60) throw new CoachException("缺少请求编号，请重新提交");

        TicketTemplate? tpl = await db.ticketTemplate.AsNoTracking().FirstOrDefaultAsync(t => t.id == templateId && t.valid == 1);
        if (tpl == null) throw new CoachException("券模板不存在或已停用");
        List<Models.Coach> coaches = await db.coach.AsNoTracking().Where(c => ids.Contains(c.id) && c.valid == 1).ToListAsync();
        if (coaches.Count != ids.Count) throw new CoachException("所选教练里有已停用或不存在的，请刷新后重试");

        DateTime now = DateTime.Now;
        DateTime expire = TicketTemplateRules.ResolveTicketExpireDate(tpl, now);
        return await MiniTicketTransaction.Run(db, async () =>
        {
            GrantResult result = new();
            HashSet<string> doneKeys = (await db.coachCouponGrant.AsNoTracking()
                .Where(g => g.grant_key.StartsWith(req + "-")).Select(g => g.grant_key).ToListAsync()).ToHashSet();
            foreach (Models.Coach coach in coaches)
            {
                Member? member = await db.member.Where(m => m.id == coach.member_id)
                    .Include(m => m.memberSocialAccounts.Where(a => a.valid == 1)).AsNoTracking().FirstOrDefaultAsync();
                if (member == null) throw new CoachException("教练「" + coach.display_name + "」的会员不存在");
                string openId = member.wechatMiniOpenId ?? "";
                int granted = 0;
                for (int seq = 1; seq <= countEach; seq++)
                {
                    string key = req + "-" + coach.id + "-" + seq;
                    if (doneKeys.Contains(key)) { result.skipped++; continue; }
                    string code = await NewCodeAsync();
                    db.card.Add(new Card { card_no = code, is_ticket = 1, type = "" });
                    db.ticket.Add(new Ticket
                    {
                        code = code, template_id = tpl.id, name = (tpl.name ?? "").Trim(), memo = (tpl.memo ?? "").Trim(),
                        member_id = member.id, open_id = openId, start_date = now, expire_date = expire,
                        used = 0, is_active = 1, valid = 1, shared = 0, printed = 0,
                        channel = CouponChannel, staff_id = op.id, create_memo = "管理员批量发给教练",
                        miniapp_recept_path = (tpl.miniapp_recept_path ?? "").Trim(), create_date = now,
                    });
                    db.coachCouponGrant.Add(new CoachCouponGrant { coach_id = coach.id, ticket_code = code, template_id = tpl.id, grant_key = key, staff_id = op.id, create_date = now });
                    await db.SaveChangesAsync();
                    granted++;
                }
                result.granted += granted;
                result.perCoach.Add(new { coachId = coach.id, name = coach.display_name, granted });
            }
            return result;
        });
    }

    private async Task<string> NewCodeAsync()
    {
        for (int i = 0; i < 1000; i++)
        {
            string code = Util.GetRandomCode(9);
            bool used = await db.card.AsNoTracking().AnyAsync(c => c.card_no == code)
                || await db.ticket.AsNoTracking().AnyAsync(t => t.code == code);
            if (!used) return code;
        }
        throw new CoachException("生成券码失败，请重试");
    }
}
