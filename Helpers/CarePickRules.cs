using System;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 养护扫码取板：店员在养护订单页出一次性码（scan_qrcode 一行，码里带 careId），
    // 顾客用微信扫码直接进小程序（pages/order/care_pick_verify），扫码人就是下单会员即验证通过，店员页随即完成发板。
    // 取代原公众号带参码 care_veri_{careId}_{id}。这里只放不碰数据库的判定，便于单测。
    public static class CarePickRules
    {
        public const string CODE_PREFIX = "cp";
        public const string QR_URL_PREFIX = "https://mini.snowmeet.top/mapp/care_pick?code=";
        public const int QR_EXPIRE_SECONDS = 120;

        // 店员侧 / 顾客侧共用的阶段
        public const string WAITING_SCAN = "waiting_scan";
        public const string EXPIRED = "expired";
        public const string STOPPED = "stopped";
        // 扫码人还没有会员身份（新微信号），顾客页引导授权手机号后重试
        public const string NEED_MEMBER = "need_member";
        public const string MATCHED = "matched";
        public const string MISMATCH = "mismatch";

        // cp_{随机串}_{careId}_{id}：随机串防猜码，careId 让顾客扫码时能找到对应订单
        public static string BuildCode(string token, int careId, int id)
        {
            return CODE_PREFIX + "_" + token + "_" + careId.ToString() + "_" + id.ToString();
        }

        public static bool TryParseCareId(string? code, out int careId)
        {
            careId = 0;
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }
            string[] parts = code.Trim().Split('_');
            return parts.Length == 4 && parts[0] == CODE_PREFIX && parts[1].Length == ReceptCheckinRules.TOKEN_LENGTH
                && int.TryParse(parts[2], out careId) && careId > 0
                && int.TryParse(parts[3], out int id) && id > 0;
        }

        public static string BuildQrText(string code)
        {
            return QR_URL_PREFIX + code;
        }

        // 验证规则同原公众号取板：扫码人就是订单会员。订单没有会员（散客单）一律不通过
        public static string ScannedStage(int? scannerMemberId, int? orderMemberId)
        {
            if (scannerMemberId == null)
            {
                return NEED_MEMBER;
            }
            return orderMemberId != null && scannerMemberId == orderMemberId ? MATCHED : MISMATCH;
        }

        public static string StaffStage(ScanQrCode sq, int? orderMemberId, DateTime now)
        {
            if (sq.scaned == 1)
            {
                return ScannedStage(sq.scaner_member_id, orderMemberId);
            }
            if (sq.stoped == 1)
            {
                return STOPPED;
            }
            if (sq.expire_time != null && now >= sq.expire_time.Value)
            {
                return EXPIRED;
            }
            return WAITING_SCAN;
        }
    }
}
