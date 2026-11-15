using System;
using System.Collections.Generic;
using System.Linq;

namespace SnowmeetApi.Helpers;

// 小程序「我的」里分销商菜单区域的纯规则。设计：snowmeet_ai_doc/docs/superpowers/specs/2026-11-15-distributor-backend-design.md
// 菜单条目由服务端按身份类型返回，小程序只认条目 key；以后给机构加「制作会员卡」「制作优惠券」只改这里。
public static class DistributorMenuRules
{
    public const string EntryQrCode = "qrcode";       // 订票二维码
    public const string EntryCustomers = "customers"; // 我的顾客与消费
    public const string EntryMakeCard = "makeCard";   // 制作会员卡（仅机构：自己设折扣，受管理员底价约束）
    public const string EntryMakeCoupon = "makeCoupon"; // 制作优惠券（仅机构：按管理员设的底价生成自己的券）
    public const string EntryCards = "cards";         // 发卡 / 领取情况
    public const string EntryCoupons = "coupons";     // 我的优惠券
    public const string EntryPayee = "payee";         // 收款资料（只读）

    /// <summary>这次做菜单区域的身份：教练、机构。KOL 以后再加。</summary>
    public static bool HasMenu(string? type) => type == IntroducerRules.TypeCoach || type == IntroducerRules.TypeOrg;

    public static IReadOnlyList<string> Entries(string? type)
    {
        if (!HasMenu(type)) return Array.Empty<string>();
        if (type == IntroducerRules.TypeOrg) return new[] { EntryQrCode, EntryCustomers, EntryMakeCard, EntryMakeCoupon, EntryCards, EntryCoupons, EntryPayee };
        return new[] { EntryQrCode, EntryCustomers, EntryCards, EntryCoupons, EntryPayee };
    }

    /// <summary>真实姓名：只露第一个字，其余用 * 代替。</summary>
    public static string MaskName(string? name)
    {
        string n = (name ?? "").Trim();
        if (n.Length == 0) return "";
        return n.Substring(0, 1) + new string('*', n.Length - 1);
    }

    /// <summary>支付宝登录号：手机号露前 3 后 4；邮箱露首字母和域名；其它只露后 4 位。</summary>
    public static string MaskLoginName(string? loginName)
    {
        string s = (loginName ?? "").Trim();
        if (s.Length == 0) return "";
        int at = s.IndexOf('@');
        if (at > 0) return s.Substring(0, 1) + "***" + s.Substring(at);
        if (s.Length == 11 && s.All(char.IsDigit)) return s.Substring(0, 3) + "****" + s.Substring(7);
        return s.Length <= 4 ? new string('*', s.Length) : "***" + s.Substring(s.Length - 4);
    }
}
