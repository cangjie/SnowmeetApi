using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 店铺接待扫码：店员手机出一次性码（scan_qrcode 一行），顾客用微信扫码直接进小程序报到、验证手机号，
    // 店员开单后顾客手机等到「待支付」的支付单就跳去付款。这里只放不碰数据库的判定，便于单测。
    public static class ReceptCheckinRules
    {
        public const string CODE_PREFIX = "rc";
        public const string QR_URL_PREFIX = "https://mini.snowmeet.top/mapp/recept_checkin?code=";
        public const int QR_EXPIRE_SECONDS = 120;
        public const int CUSTOMER_WAIT_MINUTES = 60;
        public const int TOKEN_LENGTH = 10;
        const string TOKEN_CHARS = "abcdefghijkmnpqrstuvwxyz23456789";

        // 店员侧阶段
        public const string STAFF_WAITING_SCAN = "waiting_scan";
        public const string STAFF_NEED_PHONE = "need_phone";
        public const string STAFF_VERIFIED = "verified";
        public const string STAFF_EXPIRED = "expired";
        public const string STAFF_STOPPED = "stopped";
        // 店员输手机号进入（QrCode/QueryCell 写 cell）：普通店员要等店长授权，店长及以上直接通过
        public const string STAFF_NEED_AUTH = "need_auth";
        public const string STAFF_PHONE_AUTHED = "phone_authed";

        // 顾客侧阶段
        public const string CUSTOMER_NEED_PHONE = "need_phone";
        public const string CUSTOMER_WAITING = "waiting";
        public const string CUSTOMER_WEPAY = "wepay";
        public const string CUSTOMER_ALIPAY = "alipay";
        public const string CUSTOMER_EXPIRED = "expired";

        // 码里带随机串，防止有人按自增 id 猜码抢先报到
        public static string NewToken()
        {
            char[] chars = new char[TOKEN_LENGTH];
            for (int i = 0; i < TOKEN_LENGTH; i++)
            {
                chars[i] = TOKEN_CHARS[RandomNumberGenerator.GetInt32(TOKEN_CHARS.Length)];
            }
            return new string(chars);
        }

        public static string BuildCode(string token, int id)
        {
            return CODE_PREFIX + "_" + token + "_" + id.ToString();
        }

        public static bool IsCheckinCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }
            string[] parts = code.Trim().Split('_');
            return parts.Length == 3 && parts[0] == CODE_PREFIX && parts[1].Length == TOKEN_LENGTH
                && int.TryParse(parts[2], out int id) && id > 0;
        }

        public static string BuildQrText(string code)
        {
            return QR_URL_PREFIX + code;
        }

        // 已验证 = 已扫码 + 已有会员 + 手机号已确认（会员原有手机号或本次微信授权的手机号）
        public static bool IsVerified(ScanQrCode sq)
        {
            return sq.scaned == 1 && sq.scaner_member_id != null && !string.IsNullOrWhiteSpace(sq.cell);
        }

        // 店员在出码页输了手机号（没人扫码、cell 已写入）
        public static bool IsPhoneEntry(ScanQrCode sq)
        {
            return sq.scaned == 0 && !string.IsNullOrWhiteSpace(sq.cell);
        }

        public static string StaffStage(ScanQrCode sq, DateTime now)
        {
            if (sq.scaned == 1)
            {
                return IsVerified(sq) ? STAFF_VERIFIED : STAFF_NEED_PHONE;
            }
            if (sq.stoped == 1)
            {
                return STAFF_STOPPED;
            }
            // 等店长授权可能超过二维码的 2 分钟，不按过期处理
            if (IsPhoneEntry(sq))
            {
                return sq.authed == 1 ? STAFF_PHONE_AUTHED : STAFF_NEED_AUTH;
            }
            if (sq.expire_time != null && now >= sq.expire_time.Value)
            {
                return STAFF_EXPIRED;
            }
            return STAFF_WAITING_SCAN;
        }

        // 顾客扫码报到能否放行：null = 放行，否则是给顾客看的拒绝原因。
        // 同一个人重复打开（扫码后退出再进）照常放行；店员离开出码页会作废码（stoped），但不影响已扫码的人。
        public static string? CheckScanAllowed(ScanQrCode sq, string? openId, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(openId))
            {
                return "登录信息失效，请重新扫码";
            }
            if (sq.scaned == 1)
            {
                return SameOpenId(sq.scaner_openid, openId) ? null : "这个二维码已被其他顾客使用，请让店员换一个码";
            }
            if (sq.stoped == 1 || IsPhoneEntry(sq))
            {
                return "二维码已失效，请让店员换一个码";
            }
            if (sq.expire_time != null && now >= sq.expire_time.Value)
            {
                return "二维码已过期，请让店员换一个码";
            }
            return null;
        }

        public static bool SameOpenId(string? a, string? b)
        {
            return !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && a.Trim() == b.Trim();
        }

        // 扫码之后、这位会员名下有效订单的待支付单里最新的一笔（微信或支付宝）。
        // 扫码前就有的单、已作废的单（店员切换支付方式会作废旧单）、别人的单都不算。
        public static OrderPayment? PickPendingPayment(ScanQrCode sq, IEnumerable<OrderPayment> payments)
        {
            if (!IsVerified(sq) || sq.scan_time == null)
            {
                return null;
            }
            return payments
                .Where(p => p.valid == 1 && p.status == "待支付" && p.create_date >= sq.scan_time.Value
                    && (p.pay_method == "微信支付" || p.pay_method == "支付宝")
                    && p.order != null && p.order.valid == 1 && p.order.member_id == sq.scaner_member_id)
                .OrderByDescending(p => p.create_date).ThenByDescending(p => p.id)
                .FirstOrDefault();
        }

        public static (string stage, int? paymentId) CustomerStage(ScanQrCode sq, IEnumerable<OrderPayment> payments,
            DateTime now)
        {
            if (!IsVerified(sq))
            {
                return (CUSTOMER_NEED_PHONE, null);
            }
            OrderPayment? pending = PickPendingPayment(sq, payments);
            if (pending != null)
            {
                return (pending.pay_method == "支付宝" ? CUSTOMER_ALIPAY : CUSTOMER_WEPAY, pending.id);
            }
            if (sq.scan_time != null && now >= sq.scan_time.Value.AddMinutes(CUSTOMER_WAIT_MINUTES))
            {
                return (CUSTOMER_EXPIRED, null);
            }
            return (CUSTOMER_WAITING, null);
        }

        public static string MaskCell(string? cell)
        {
            if (string.IsNullOrWhiteSpace(cell))
            {
                return "";
            }
            string c = cell.Trim();
            return c.Length == 11 ? c.Substring(0, 3) + "****" + c.Substring(7) : c;
        }
    }
}
