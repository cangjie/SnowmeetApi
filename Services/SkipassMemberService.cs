using System;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services;

/// <summary>为雪票结算的有效微信会话安全关联会员，手机号仍由独立的验证流程处理。</summary>
public sealed class SkipassMemberService(ApplicationDBContext db)
{
    public async Task<int?> EnsureAsync(string sessionKey, string sessionType)
    {
        if (string.IsNullOrWhiteSpace(sessionKey) || sessionType != "wechat_mini_openid") return null;
        string key = Util.UrlDecode(sessionKey).Trim();
        var snapshot = await db.miniSession.AsNoTracking().FirstOrDefaultAsync(s => s.session_key == key
            && s.session_type == sessionType && s.valid == 1 && s.expire_date >= DateTime.Now);
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.wechat_openid)) return null;
        string openId = snapshot.wechat_openid.Trim();
        string? unionId = string.IsNullOrWhiteSpace(snapshot.wechat_unionid) ? null : snapshot.wechat_unionid.Trim();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string lockKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(unionId ?? openId)));
        await CareCardSaleService.LockAsync(db, "recept-member:" + lockKey);
        var session = await db.miniSession.AsTracking().FirstOrDefaultAsync(s => s.session_key == key
            && s.session_type == sessionType && s.valid == 1 && s.expire_date >= DateTime.Now);
        if (session == null || !string.Equals(session.wechat_openid?.Trim(), openId, StringComparison.Ordinal)
            || !string.Equals(string.IsNullOrWhiteSpace(session.wechat_unionid) ? null : session.wechat_unionid.Trim(), unionId, StringComparison.Ordinal))
            throw new InvalidOperationException("登录信息已变化，请重新进入小程序");
        var matches = await db.memberSocialAccount.AsNoTracking().Where(a =>
            (a.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID && a.num == openId)
            || (unionId != null && a.type == MemberSocialAccount.TYPE_WECHAT_UNIONID && a.num == unionId)).ToListAsync();
        matches = matches.Where(a => (a.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID && string.Equals(a.num.Trim(), openId, StringComparison.Ordinal))
            || (a.type == MemberSocialAccount.TYPE_WECHAT_UNIONID && string.Equals(a.num.Trim(), unionId, StringComparison.Ordinal))).ToList();
        var jobAccounts = await db.socialAccountForJob.AsNoTracking().Where(a => a.wechat_mini_openid == openId).ToListAsync();
        var otherSessions = await db.miniSession.AsNoTracking().Where(s => s.session_type == "wechat_mini_openid"
            && s.wechat_openid == openId && s.valid == 1 && s.expire_date >= DateTime.Now && s.member_id != null).ToListAsync();
        var jobIds = jobAccounts.Where(a => string.Equals(a.wechat_mini_openid?.Trim(), openId, StringComparison.Ordinal)
            && a.member_id > 0).Select(a => a.member_id).ToList();
        var sessionIds = otherSessions.Where(s => string.Equals(s.wechat_openid?.Trim(), openId, StringComparison.Ordinal))
            .Select(s => s.member_id!.Value).ToList();
        var candidateIds = matches.Select(a => a.member_id).Concat(jobIds).Concat(sessionIds)
            .Concat(session.member_id.HasValue ? new[] { session.member_id.Value } : Array.Empty<int>()).Distinct().ToList();
        var members = await db.member.AsNoTracking().Where(m => candidateIds.Contains(m.id)).ToListAsync();
        bool IsMerged(int id) => members.Any(m => m.id == id && (m.is_merge == 1 || m.merge_id != null));
        bool IsUsable(int id) => members.Any(m => m.id == id && m.valid == 1 && m.is_merge != 1 && m.merge_id == null);
        // 合并来源的旧身份不代表当前归属；员工历史表指向不存在的会员时也不能拿来建重复关系。
        var relevant = matches.Where(a => !IsMerged(a.member_id)).ToList();
        var ids = relevant.Where(a => a.valid == 1).Select(a => a.member_id)
            .Concat(jobIds.Where(IsUsable)).Concat(sessionIds.Where(IsUsable)).Distinct().ToList();
        if (ids.Count > 1 || (ids.Count == 0 && relevant.Count > 0)
            || relevant.Any(a => a.valid != 1 && !relevant.Any(b => b.valid == 1 && b.type == a.type)))
            throw new InvalidOperationException("微信账号存在历史会员关联，请联系工作人员核对");
        int? sessionMemberId = session.member_id != null && !IsMerged(session.member_id.Value) ? session.member_id : null;
        if (sessionMemberId != null && ids.Count == 1 && sessionMemberId != ids[0])
            throw new InvalidOperationException("微信账号与会员信息不一致，请联系工作人员核对");
        int? memberId = sessionMemberId ?? ids.SingleOrDefault();
        if (memberId == 0) memberId = null;
        if (memberId != null && !await db.member.AsNoTracking().AnyAsync(m => m.id == memberId && m.valid == 1 && m.merge_id == null))
            throw new InvalidOperationException("微信关联的会员已失效，请联系工作人员核对");
        Member? created = null;
        if (memberId == null)
        {
            var member = new Member { real_name = "", gender = "", source = "雪票下单", valid = 1, is_staff = 0, is_manager = 0, is_admin = 0 };
            db.member.Add(member);
            await db.SaveChangesAsync();
            created = member;
            memberId = member.id;
            db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("Member", "", memberId.Value, "雪票下单创建会员",
                memberId, null, null, null, "微信会话关联"));
        }
        if (!relevant.Any(a => a.valid == 1 && a.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID))
            db.memberSocialAccount.Add(new MemberSocialAccount { member_id = memberId.Value, type = MemberSocialAccount.TYPE_WECHAT_MINI_OPENID, num = openId, valid = 1, memo = "雪票下单" });
        if (unionId != null && !relevant.Any(a => a.valid == 1 && a.type == MemberSocialAccount.TYPE_WECHAT_UNIONID))
            db.memberSocialAccount.Add(new MemberSocialAccount { member_id = memberId.Value, type = MemberSocialAccount.TYPE_WECHAT_UNIONID, num = unionId, valid = 1, memo = "雪票下单" });
        session.member_id = memberId;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        if (created != null) db.Entry(created).State = EntityState.Detached;
        return memberId;
    }
}
