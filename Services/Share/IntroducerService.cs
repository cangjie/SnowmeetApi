using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.Share;

/// <summary>
/// 介绍人管理：添加、修改、停用、绑定支付宝分账关系。介绍人 = 会员 + introducer 记录 + 一条收款账号（order_share_relation）。
/// 个人类（教练、KOL）的微信账号取会员在我们小程序里的 openid，不用填；机构填商户号和商户全称。
/// </summary>
public sealed class IntroducerService(ApplicationDBContext db, IAlipayRoyaltyBinder binder)
{
    public sealed record AccountInput(string? wepayAccountNum, string? wepayAccountName, string? aliAccountNum);

    /// <param name="wechat">已填写 / 未填写</param>
    /// <param name="alipay">未填写 / 已绑定 / 未绑定</param>
    /// <param name="alipayMessage">这次绑定失败的原因，没有则为空</param>
    public sealed record IntroducerView(int id, int memberId, string type, string name, string? realName, bool valid, int? coachId,
        string wechat, string alipay, string alipayMessage);

    private static string Clean(string? s) => (s ?? "").Trim();

    /// <param name="cellTail">手机号后 4 位，用来区分同名；没有手机号为空</param>
    public sealed record ChoiceView(int id, string name, string type, string cellTail);

    /// <summary>
    /// 订单上选收款人的候选：只列有效的介绍人。type（教练 / KOL / 机构，空 = 全部）筛类型；
    /// keyword 在介绍人名称、真实姓名（介绍人上填的和会员资料里的）、会员的手机号里找，包含即算。
    /// </summary>
    public async Task<List<ChoiceView>> ChoicesAsync(string? type, string? keyword)
    {
        string kind = Clean(type);
        if (kind != "" && !IntroducerRules.IsValidType(kind)) throw new ShareException("介绍人类型只能是教练、KOL、机构");
        string key = Clean(keyword);

        List<Introducer> introducers = await db.introducer.AsNoTracking()
            .Where(i => i.valid == 1 && (kind == "" || i.type == kind)).OrderBy(i => i.id).ToListAsync();
        List<int> memberIds = introducers.Select(i => i.member_id).Distinct().ToList();
        Dictionary<int, List<string>> cells = (await db.memberSocialAccount.AsNoTracking()
            .Where(a => memberIds.Contains(a.member_id) && a.valid == 1 && a.type == MemberSocialAccount.TYPE_CELL).OrderBy(a => a.id).ToListAsync())
            .GroupBy(a => a.member_id).ToDictionary(g => g.Key, g => g.Select(a => a.num ?? "").Where(n => n != "").ToList());
        Dictionary<int, string> memberNames = (await db.member.AsNoTracking().Where(m => memberIds.Contains(m.id)).ToListAsync())
            .ToDictionary(m => m.id, m => m.real_name ?? "");

        bool Has(string? text) => !string.IsNullOrEmpty(text) && text.Contains(key, StringComparison.OrdinalIgnoreCase);
        return introducers
            .Where(i => key == "" || Has(i.name) || Has(i.real_name) || Has(memberNames.GetValueOrDefault(i.member_id))
                || (cells.TryGetValue(i.member_id, out List<string>? mine) && mine.Any(n => n.Contains(key))))
            .Select(i =>
            {
                string last = cells.TryGetValue(i.member_id, out List<string>? mine) && mine.Count > 0 ? mine[^1] : "";
                return new ChoiceView(i.id, i.name, i.type, last.Length > 4 ? last[^4..] : last);
            })
            .ToList();
    }

    // 进程内只有一个同步在跑：同一位教练不会被两个同时打开的页面各建一次（库里还有 member_id 的唯一索引兜底）
    private static readonly SemaphoreSlim CoachSyncLock = new(1, 1);

    /// <summary>
    /// 教练也是介绍人：每位有效的教练如果还没有介绍人记录，就自动建一条（类型教练，名称取教练的显示名，
    /// 微信收款账号取这位会员的小程序 openid，支付宝账号和真实姓名留空，管理员以后补）。
    /// 已经有介绍人记录的（不管有效还是被管理员停用）不再建，所以停用的不会被自动启用；停用的教练也不建。返回新建了几位。
    /// </summary>
    public async Task<int> SyncCoachesAsync()
    {
        await CoachSyncLock.WaitAsync();
        try
        {
            List<Coach> coaches = await db.coach.AsNoTracking().Where(c => c.valid == 1).OrderBy(c => c.id).ToListAsync();
            if (coaches.Count == 0) return 0;
            List<int> knownMembers = await db.introducer.AsNoTracking().Select(i => i.member_id).ToListAsync();
            List<int> knownCoaches = await db.introducer.AsNoTracking().Where(i => i.coach_id != null).Select(i => i.coach_id!.Value).ToListAsync();
            int created = 0;
            foreach (Coach coach in coaches.Where(c => !knownMembers.Contains(c.member_id) && !knownCoaches.Contains(c.id)))
            {
                Member? member = await db.member.AsNoTracking().FirstOrDefaultAsync(m => m.id == coach.member_id);
                string name = !string.IsNullOrWhiteSpace(coach.display_name) ? Clean(coach.display_name)
                    : !string.IsNullOrWhiteSpace(member?.real_name) ? Clean(member!.real_name) : "教练" + coach.id;
                string? openid = await db.memberSocialAccount.AsNoTracking()
                    .Where(a => a.member_id == coach.member_id && a.valid == 1 && a.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID)
                    .OrderByDescending(a => a.id).Select(a => a.num).FirstOrDefaultAsync();
                try
                {
                    await using var tx = await db.Database.BeginTransactionAsync();
                    var relation = new OrderShareRelation
                    {
                        type = IntroducerRules.RelationType(IntroducerRules.TypeCoach), name = name, valid = true,
                        wepay_account_type = string.IsNullOrWhiteSpace(openid) ? null : "PERSONAL_OPENID",
                        wepay_account_num = string.IsNullOrWhiteSpace(openid) ? null : openid,
                    };
                    db.orderShareRelation.Add(relation);
                    await db.SaveChangesAsync();
                    db.introducer.Add(new Introducer
                    {
                        member_id = coach.member_id, type = IntroducerRules.TypeCoach, name = name,
                        relation_id = relation.id, coach_id = coach.id,
                    });
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                    created++;
                }
                catch (DbUpdateException)
                {
                    // 别的进程（比如两台服务）同时建了同一位：唯一索引挡住了第二条，事务已回滚，这位算别人建好了
                }
                finally
                {
                    db.ChangeTracker.Clear();
                }
            }
            return created;
        }
        finally
        {
            CoachSyncLock.Release();
        }
    }

    public async Task<IntroducerView> AddAsync(Staff op, int memberId, string type, string name, string? realName, AccountInput account)
    {
        type = Clean(type);
        name = Clean(name);
        string real = Clean(realName);
        if (!IntroducerRules.IsValidType(type)) throw new ShareException("介绍人类型只能是教练、KOL、机构");
        if (name == "") throw new ShareException("请填写显示名称");
        if (!await db.member.AsNoTracking().AnyAsync(m => m.id == memberId && m.valid == 1)) throw new ShareException("会员不存在");
        if (await db.introducer.AsNoTracking().AnyAsync(i => i.member_id == memberId && i.valid == 1))
        {
            throw new ShareException("这位会员已经是介绍人");
        }

        string? wepayType, wepayNum, wepayName;
        if (type == IntroducerRules.TypeOrg)
        {
            wepayNum = Clean(account.wepayAccountNum);
            wepayName = Clean(account.wepayAccountName);
            if (wepayNum == "" || wepayName == "") throw new ShareException("机构要填商户号和商户全称");
            wepayType = "MERCHANT_ID";
        }
        else
        {
            wepayNum = await db.memberSocialAccount.AsNoTracking()
                .Where(a => a.member_id == memberId && a.valid == 1 && a.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID)
                .OrderByDescending(a => a.id).Select(a => a.num).FirstOrDefaultAsync();
            wepayType = string.IsNullOrWhiteSpace(wepayNum) ? null : "PERSONAL_OPENID";
            wepayName = null;
        }
        string? aliNum = ValidateAli(account, real);

        var relation = new OrderShareRelation
        {
            type = IntroducerRules.RelationType(type), name = name, valid = true,
            wepay_account_type = wepayType, wepay_account_num = wepayNum, wepay_account_name = wepayName,
            ali_account_type = aliNum == null ? null : IntroducerRules.AlipayLoginType, ali_account_num = aliNum,
        };
        db.orderShareRelation.Add(relation);
        await db.SaveChangesAsync();
        var introducer = new Introducer
        {
            member_id = memberId, type = type, name = name, real_name = real == "" ? null : real,
            relation_id = relation.id, created_by_staff_id = op?.id,
        };
        db.introducer.Add(introducer);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return aliNum != null ? await BindAlipayAsync(introducer.id) : await ViewAsync(introducer.id);
    }

    public async Task<IntroducerView> UpdateAsync(int introducerId, string? name, string? realName, bool? valid, AccountInput? account)
    {
        Introducer i = await db.introducer.AsNoTracking().FirstOrDefaultAsync(x => x.id == introducerId)
            ?? throw new ShareException("介绍人不存在");
        OrderShareRelation r = await db.orderShareRelation.AsNoTracking().FirstAsync(x => x.id == i.relation_id);
        if (valid == true && i.valid == 0
            && await db.introducer.AsNoTracking().AnyAsync(x => x.member_id == i.member_id && x.valid == 1 && x.id != i.id))
        {
            throw new ShareException("这位会员已经是介绍人");
        }
        if (name != null)
        {
            string n = Clean(name);
            if (n == "") throw new ShareException("请填写显示名称");
            i.name = n;
            r.name = n;
        }
        if (realName != null) i.real_name = Clean(realName) == "" ? null : Clean(realName);
        if (valid != null) i.valid = valid.Value ? 1 : 0;

        bool aliChanged = false;
        if (account != null)
        {
            if (i.type == IntroducerRules.TypeOrg && Clean(account.wepayAccountNum) != "" && Clean(account.wepayAccountName) != "")
            {
                r.wepay_account_num = Clean(account.wepayAccountNum);
                r.wepay_account_name = Clean(account.wepayAccountName);
            }
            string? aliNum = ValidateAli(account, i.real_name ?? "");
            if (aliNum != null && (r.ali_account_type != IntroducerRules.AlipayLoginType || aliNum != r.ali_account_num))
            {
                r.ali_account_type = IntroducerRules.AlipayLoginType;
                r.ali_account_num = aliNum;
                aliChanged = true;
            }
        }
        i.update_date = DateTime.Now;
        db.introducer.Update(i);
        db.orderShareRelation.Update(r);
        if (aliChanged)
        {
            // 账号换了，旧的绑定作废，下面重新绑
            foreach (ShareRelationBind old in await db.shareRelationBind.AsNoTracking()
                .Where(b => b.share_relation_id == r.id && b.pay_method == "支付宝" && b.valid).ToListAsync())
            {
                old.valid = false;
                old.update_date = DateTime.Now;
                db.shareRelationBind.Update(old);
            }
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return aliChanged ? await BindAlipayAsync(i.id) : await ViewAsync(i.id);
    }

    /// <summary>向支付宝绑定分账关系。绑定失败不抛异常：介绍人保留，失败原因放在返回里，可以重试。</summary>
    public async Task<IntroducerView> BindAlipayAsync(int introducerId)
    {
        Introducer i = await db.introducer.AsNoTracking().FirstOrDefaultAsync(x => x.id == introducerId)
            ?? throw new ShareException("介绍人不存在");
        OrderShareRelation r = await db.orderShareRelation.AsNoTracking().FirstAsync(x => x.id == i.relation_id);
        if (string.IsNullOrWhiteSpace(r.ali_account_type) || string.IsNullOrWhiteSpace(r.ali_account_num))
        {
            throw new ShareException("还没有填支付宝账号");
        }
        var result = await binder.BindAsync(r.ali_account_type!, r.ali_account_num!, i.real_name);
        if (result.ok)
        {
            ShareRelationBind? bind = await db.shareRelationBind.AsNoTracking()
                .FirstOrDefaultAsync(b => b.share_relation_id == r.id && b.pay_method == "支付宝");
            if (bind == null)
            {
                db.shareRelationBind.Add(new ShareRelationBind { share_relation_id = r.id, pay_method = "支付宝", valid = true });
            }
            else if (!bind.valid)
            {
                bind.valid = true;
                bind.update_date = DateTime.Now;
                db.shareRelationBind.Update(bind);
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        return await ViewAsync(introducerId, result.ok ? "" : result.message);
    }

    public async Task<List<IntroducerView>> ListAsync(bool onlyValid)
    {
        List<int> ids = await db.introducer.AsNoTracking().Where(i => !onlyValid || i.valid == 1)
            .OrderByDescending(i => i.valid).ThenBy(i => i.id).Select(i => i.id).ToListAsync();
        List<IntroducerView> views = new();
        foreach (int id in ids) views.Add(await ViewAsync(id));
        return views;
    }

    private async Task<IntroducerView> ViewAsync(int introducerId, string alipayMessage = "")
    {
        Introducer i = await db.introducer.AsNoTracking().FirstAsync(x => x.id == introducerId);
        OrderShareRelation r = await db.orderShareRelation.AsNoTracking().FirstAsync(x => x.id == i.relation_id);
        bool bound = await db.shareRelationBind.AsNoTracking().AnyAsync(b => b.share_relation_id == r.id && b.pay_method == "支付宝" && b.valid);
        string wechat = string.IsNullOrWhiteSpace(r.wepay_account_num) ? "未填写" : "已填写";
        string alipay = string.IsNullOrWhiteSpace(r.ali_account_num) ? "未填写" : (bound ? "已绑定" : "未绑定");
        return new IntroducerView(i.id, i.member_id, i.type, i.name, i.real_name, i.valid == 1, i.coach_id, wechat, alipay, alipayMessage);
    }

    /// <summary>支付宝登录号：空就是没填；填了要是手机号或邮箱，并且要有真实姓名（支付宝按登录号绑定时核对）。</summary>
    private static string? ValidateAli(AccountInput a, string realName)
    {
        string num = Clean(a.aliAccountNum);
        if (num == "") return null;
        if (!IntroducerRules.IsValidAlipayLoginName(num)) throw new ShareException("支付宝登录号要填手机号或邮箱");
        if (Clean(realName) == "") throw new ShareException("登录号要同时填真实姓名");
        return num;
    }
}
