using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services;

/// <summary>只在店员明确指定本次接待记录时，为开单顾客匹配或创建微信会员。调用方须在订单写入的同一事务中调用。</summary>
public sealed class ReceptOrderMemberService(ApplicationDBContext db)
{
    public async Task<bool> PrepareAsync(Models.Order order, int staffId, int? receptCheckinId)
    {
        if (receptCheckinId == null || order.member_id != null) return false;
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("接待会员关联需要数据库事务");

        // 同一接待记录的并发保存先串行化，再读取记录，避免事务同时持有读锁后互相等待。
        await CareCardSaleService.LockAsync(db, "recept-checkin:" + receptCheckinId.Value);
        var scan = await db.scanQrCode.AsTracking().FirstOrDefaultAsync(q => q.id == receptCheckinId);
        if (scan == null || scan.staff_id != staffId || scan.scaned != 1 ||
            !ReceptCheckinRules.IsCheckinCode(scan.code) || string.IsNullOrWhiteSpace(scan.scaner_openid) ||
            scan.platform != "wechat_mini" || scan.scene != "店铺接待" || scan.purpose != "顾客扫码进小程序" ||
            scan.scan_time == null || scan.scan_time > DateTime.Now ||
            (order.id == 0 && scan.scan_time < DateTime.Now.AddMinutes(-ReceptCheckinRules.CUSTOMER_WAIT_MINUTES)))
            throw new InvalidOperationException("接待扫码记录无效，请重新接待顾客");

        // 旧客户端或迟到的草稿保存可能仍传空 member_id；只恢复本次扫码已关联的原会员。
        if (order.id != 0)
        {
            int? savedMember = await db.order.AsNoTracking().Where(o => o.id == order.id)
                .Select(o => o.member_id).FirstOrDefaultAsync();
            if (savedMember != null && savedMember == scan.scaner_member_id) order.member_id = savedMember;
            return false;
        }

        string openId = scan.scaner_openid.Trim();
        string? unionId = string.IsNullOrWhiteSpace(scan.scaner_unionid) ? null : scan.scaner_unionid.Trim();
        // 相同顾客的并发开单使用同一跨进程锁；不把微信标识直接写入锁名或日志。
        string lockKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(unionId ?? openId)));
        await CareCardSaleService.LockAsync(db, "recept-member:" + lockKey);
        // 账号写入时去除首尾空格；生产 num/type 索引需直接比较 num，避免事务内扫描全表。
        var matches = await db.memberSocialAccount.AsNoTracking()
            .Where(a => (a.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID && a.num == openId)
                || (unionId != null && a.type == MemberSocialAccount.TYPE_WECHAT_UNIONID && a.num == unionId))
            .ToListAsync();
        // 数据库可能使用不区分大小写的排序规则；微信标识需要精确区分大小写。
        matches = matches.Where(a => (a.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID && string.Equals(a.num.Trim(), openId, StringComparison.Ordinal))
            || (a.type == MemberSocialAccount.TYPE_WECHAT_UNIONID && string.Equals(a.num.Trim(), unionId, StringComparison.Ordinal))).ToList();
        var activeIds = matches.Where(a => a.valid == 1).Select(a => a.member_id).Distinct().ToList();
        if (activeIds.Count > 1 || (activeIds.Count == 0 && matches.Count > 0))
            throw new InvalidOperationException("顾客微信账号存在历史关联，请先核对会员资料");
        if (matches.Any(a => a.valid != 1 && a.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID)
            && !matches.Any(a => a.valid == 1 && a.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID))
            throw new InvalidOperationException("顾客微信账号存在历史关联，请先核对会员资料");
        if (matches.Any(a => a.valid != 1 && a.type == MemberSocialAccount.TYPE_WECHAT_UNIONID)
            && !matches.Any(a => a.valid == 1 && a.type == MemberSocialAccount.TYPE_WECHAT_UNIONID))
            throw new InvalidOperationException("顾客微信账号存在历史关联，请先核对会员资料");

        int memberId;
        if (activeIds.Count == 1)
        {
            memberId = activeIds[0];
            bool valid = await db.member.AsNoTracking().AnyAsync(m => m.id == memberId && m.valid == 1 && m.merge_id == null);
            if (!valid) throw new InvalidOperationException("顾客微信关联的会员已失效，请先核对会员资料");
        }
        else if (scan.scaner_member_id != null)
        {
            memberId = scan.scaner_member_id.Value;
            bool valid = await db.member.AsNoTracking().AnyAsync(m => m.id == memberId && m.valid == 1 && m.merge_id == null);
            if (!valid) throw new InvalidOperationException("接待记录关联的会员已失效，请先核对会员资料");
        }
        else
        {
            var member = new Member { real_name = "", gender = "", source = "接待扫码开单", valid = 1,
                is_staff = 0, is_manager = 0, is_admin = 0 };
            await db.member.AddAsync(member);
            await db.SaveChangesAsync();
            memberId = member.id;
        }
        if (scan.scaner_member_id != null && scan.scaner_member_id != memberId)
            throw new InvalidOperationException("接待记录与微信会员不一致，请先核对会员资料");
        if (!matches.Any(a => a.valid == 1 && a.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID))
            await db.memberSocialAccount.AddAsync(new MemberSocialAccount { member_id = memberId, type = MemberSocialAccount.TYPE_WECHAT_MINI_OPENID, num = openId, valid = 1, memo = "接待扫码开单" });
        if (unionId != null && !matches.Any(a => a.valid == 1 && a.type == MemberSocialAccount.TYPE_WECHAT_UNIONID))
            await db.memberSocialAccount.AddAsync(new MemberSocialAccount { member_id = memberId, type = MemberSocialAccount.TYPE_WECHAT_UNIONID, num = unionId, valid = 1, memo = "接待扫码开单" });

        scan.scaner_member_id = memberId;
        scan.update_date = DateTime.Now;
        var sessions = await db.miniSession.AsTracking().Where(s => s.session_type == "wechat_mini_openid"
            && s.wechat_openid == openId && s.valid == 1 && s.expire_date >= DateTime.Now).ToListAsync();
        sessions = sessions.Where(s => string.Equals(s.wechat_openid, openId, StringComparison.Ordinal)).ToList();
        if (sessions.Any(s => s.member_id != null && s.member_id != memberId))
            throw new InvalidOperationException("顾客会话与微信会员不一致，请先核对会员资料");
        foreach (var session in sessions.Where(s => s.member_id == null)) session.member_id = memberId;
        order.member_id = memberId;
        await db.SaveChangesAsync();
        return true;
    }

    public static CoreDataModLog ChangeLog(int orderId, int memberId, int staffId, int checkinId) =>
        CoreDataModLog.CreateManualLog("Order", "member_id", orderId, "接待扫码开单关联会员",
            memberId, staffId, null, memberId.ToString(), "接待记录 " + checkinId);
}
