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
    public const string EntryMakeCoupon = "makeCoupon"; // 制作优惠券（教练 / KOL / 机构：按管理员设的优惠券底价生成单张券）
    public const string EntryCards = "cards";         // 发卡 / 领取情况
    public const string EntryCoupons = "coupons";     // 我的优惠券
    public const string EntryPayee = "payee";         // 收款资料（只读）

    /// <summary>有菜单区域的身份：教练、KOL、机构（2026-11-17 起 KOL 也有：教练和 KOL 能发单张优惠券）。</summary>
    public static bool HasMenu(string? type) => type == IntroducerRules.TypeCoach || type == IntroducerRules.TypeKol || type == IntroducerRules.TypeOrg;

    public static IReadOnlyList<string> Entries(string? type)
    {
        if (!HasMenu(type)) return Array.Empty<string>();
        // 只有机构能发整个雪季有效的会籍卡；教练和 KOL 只能发单张优惠券（用户 2026-11-17 定）
        if (type == IntroducerRules.TypeOrg) return new[] { EntryQrCode, EntryCustomers, EntryMakeCard, EntryMakeCoupon, EntryCards, EntryCoupons, EntryPayee };
        return new[] { EntryQrCode, EntryCustomers, EntryMakeCoupon, EntryCoupons, EntryPayee };
    }

    /// <summary>真实姓名完整显示。</summary>
    public static string MaskName(string? name)
    {
        string n = (name ?? "").Trim();
        return n;
    }

    /// <summary>支付宝登录号完整显示。</summary>
    public static string MaskLoginName(string? loginName)
    {
        string s = (loginName ?? "").Trim();
        return s;
    }
}
