using System;
using System.Collections.Generic;
using System.Linq;
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
    public sealed record AccountInput(string? wepayAccountNum, string? wepayAccountName, string? aliAccountType, string? aliAccountNum);

    /// <param name="wechat">已填写 / 未填写</param>
    /// <param name="alipay">未填写 / 已绑定 / 未绑定</param>
    /// <param name="alipayMessage">这次绑定失败的原因，没有则为空</param>
    public sealed record IntroducerView(int id, int memberId, string type, string name, string? realName, bool valid, int? coachId,
        string wechat, string alipay, string alipayMessage);

    private static string Clean(string? s) => (s ?? "").Trim();

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
        (string? aliType, string? aliNum) = ValidateAli(account, real);

        var relation = new OrderShareRelation
        {
            type = IntroducerRules.RelationType(type), name = name, valid = true,
            wepay_account_type = wepayType, wepay_account_num = wepayNum, wepay_account_name = wepayName,
            ali_account_type = aliType, ali_account_num = aliNum,
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
            (string? aliType, string? aliNum) = ValidateAli(account, i.real_name ?? "");
            if (aliNum != null && (aliType != r.ali_account_type || aliNum != r.ali_account_num))
            {
                r.ali_account_type = aliType;
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

    private static (string? type, string? num) ValidateAli(AccountInput a, string realName)
    {
        string num = Clean(a.aliAccountNum);
        string type = Clean(a.aliAccountType);
        if (num == "") return (null, null);
        if (type != "loginName" && type != "userId") throw new ShareException("支付宝账号类型只能是登录号或用户号");
        if (type == "loginName" && Clean(realName) == "") throw new ShareException("登录号要同时填真实姓名");
        return (type, num);
    }
}
