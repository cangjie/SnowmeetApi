using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 支付宝付款的免验证规则（2026-11-27 用户定）：订单会员有绑定手机号时，支付宝端不再验证手机号，直接支付。
    /// 这类订单不因付款人改归属，也不绑支付宝账号、不建会员，付款账号的 open_id 只记在付款记录上。
    /// 身份判断、下单、支付回调、支付成功四处共用。
    /// </summary>
    public static class AlipayPayRules
    {
        public static async Task<bool> SkipsPhoneVerifyAsync(ApplicationDBContext db, int? orderMemberId)
        {
            if (orderMemberId == null) return false;
            return await db.memberSocialAccount.AnyAsync(m => m.member_id == orderMemberId.Value
                && m.valid == 1 && m.type.Trim() == "cell" && m.num.Trim() != "");
        }
    }
}
