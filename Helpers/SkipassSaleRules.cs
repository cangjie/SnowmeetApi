using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 万龙雪票（自我游）的售卖规则。只放不碰数据库的判定，便于单测。
    //   1. 只有在职店员能订票，顾客只能看价格（ReserveSkiPass 入口强制，页面隐藏支付栏只是体验）；
    //   2. 成交价 = 自我游门市价 marketPrice，不再加价；门市价缺失（<=0）时回退到「结算价 + 加价」；
    //   3. 价格行变化判定看结算价、建议售价、门市价任一个，不能只看结算价；
    //   4. 非店员看不到进货价（商品 cost、每日结算价、建议售价），也查不了自我游预存款。
    public static class SkipassSaleRules
    {
        public const string STAFF_ONLY_MESSAGE = "仅限店员订票";

        // 自我游的两个账号：万龙自我游是万龙自己的账号，大好河山是另一个渠道账号。
        // 万龙试营业期间的票只在大好河山账号里有售卖权限（2026-10-29 核实），所以两个来源的商品都能卖。
        public const string SOURCE_WANLONG = "万龙自我游";
        public const string SOURCE_DHHS = "大好河山";
        public static readonly string[] SellableSources = { SOURCE_WANLONG, SOURCE_DHHS };

        // 门市价缺失时沿用的加价（与旧同步逻辑的默认值一致）
        public const double DEFAULT_MARGIN = 20;

        const double PRICE_EPSILON = 0.005;

        // staff 来自 Util.GetStaffBySessionKey：会话无效、不是店员或已离职都是 null
        public static bool CanReserve(Staff? staff)
        {
            return staff != null && staff.valid == 1;
        }

        public static bool CanSeeCost(Staff? staff)
        {
            return CanReserve(staff);
        }

        public static double ResolveDealPrice(double? marketPrice, double settlementPrice, double margin)
        {
            if (marketPrice != null && marketPrice.Value > 0)
            {
                return marketPrice.Value;
            }
            return settlementPrice + margin;
        }

        public static bool PriceChanged(SkipassDailyPrice existing, double settlementPrice, double salePrice, double marketPrice)
        {
            return Differs(existing.settlementPrice, settlementPrice)
                || Differs(existing.salePrice, salePrice)
                || Differs(existing.marketPrice, marketPrice);
        }

        static bool Differs(double a, double b)
        {
            return Math.Abs(a - b) > PRICE_EPSILON;
        }

        // 取消已确认（is_cancel=1）、有效、还没发起过退款的万龙雪票，需要自动退款。
        // 取消先提交给自我游，即时确认的当场退，需要审核的等 RefreshCancel 查到已取消再退；
        // 退款行（payment_refund）一旦存在就不再自动退，避免失败重试变成重复退款（由调用方查）。
        public static bool NeedsAutoRefund(int isCancel, int valid, string? resort, int? haveRefund, double? refundAmount)
        {
            if (isCancel != 1 || valid != 1) return false;
            if ((resort ?? "").Trim().Equals("南山")) return false;
            return haveRefund == null && refundAmount == null;
        }

        // 取票二维码图片是供应商（自我游、携程图片库）的域名，小程序真机不允许加载非合法域名的图片，
        // 所以由我们的服务器去取再转给小程序。服务器只替小程序取这几个供应商域名下的 https 图片，不能被当成任意地址的代理。
        static readonly string[] QrImageHostSuffixes = { ".zowoyoo.com", ".c-ctrip.com", ".ctrip.com" };

        public static bool IsAllowedQrImageUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;
            string host = uri.Host.ToLowerInvariant();
            if (host == "zowoyoo.com" || host == "c-ctrip.com" || host == "ctrip.com") return true;
            return QrImageHostSuffixes.Any(suffix => host.EndsWith(suffix));
        }

        // 付款后「出票中」的票怎么处理：刚开始的 90 秒留给第一次出票；之后 30 分钟内每分钟重试
        // （向自我游下单用固定的我方单号，重试不会重复出票）；超过 30 分钟还没出成就确认出票失败、停止出票、自动退款。
        public static readonly TimeSpan IssuingGrace = TimeSpan.FromSeconds(90);
        public static readonly TimeSpan IssuingGiveUpAfter = TimeSpan.FromMinutes(30);

        public enum IssuingAction { Wait, Retry, GiveUp }

        public static IssuingAction DecideIssuing(DateTime startedAt, DateTime now)
        {
            TimeSpan age = now - startedAt;
            if (age <= IssuingGrace) return IssuingAction.Wait;
            if (age <= IssuingGiveUpAfter) return IssuingAction.Retry;
            return IssuingAction.GiveUp;
        }

        // 超过 30 分钟没出成、准备确认出票失败之前，先按我方单号去供应商那里核对这张票有没有订单：
        //   没有订单 / 订单已取消 → 可以确认失败并退款；
        //   有订单、有取票码 → 其实出成了（我们没记上），补记订单号，不退款；
        //   有订单、没有取票码 → 先取消，等供应商确认已取消再退款；
        //   查不到结果（接口异常）→ 不动，下一分钟再核对。
        public enum SupplierOrderKind { NoOrder, Cancelled, LiveWithCode, LiveWithoutCode, Unknown }
        public enum GiveUpAction { ConfirmFailedAndRefund, RecordIssued, CancelThenConfirm, Wait }

        public static GiveUpAction DecideGiveUp(SupplierOrderKind kind)
        {
            switch (kind)
            {
                case SupplierOrderKind.NoOrder:
                case SupplierOrderKind.Cancelled:
                    return GiveUpAction.ConfirmFailedAndRefund;
                case SupplierOrderKind.LiveWithCode:
                    return GiveUpAction.RecordIssued;
                case SupplierOrderKind.LiveWithoutCode:
                    return GiveUpAction.CancelThenConfirm;
                default:
                    return GiveUpAction.Wait;
            }
        }

        // 出票中的票（付款了、还没有供应商订单号、没有失败）不允许退票、退款：只有等出票成功，或确认出票失败。
        public static bool CanRefundBeforeIssued(string? reserveNo, int isCancel)
        {
            return !string.IsNullOrWhiteSpace(reserveNo) || isCancel == -2;
        }

        // 向自我游下单时的 orderSourceId（我方单号）。自我游按它去重：同一个单号再下单，返回「订单已经下单成功」
        // 和第一次的订单号，不会新建订单。购物车订单一单有多张票、共用一个付款单号，所以多张票的订单每张票要带上
        // 票号，各用各的单号；一单一张的沿用原来的付款单号。
        public static string SupplierSourceId(string outTradeNo, int validPassesInOrder, int skipassId)
        {
            return validPassesInOrder > 1 ? outTradeNo + "_P" + skipassId : outTradeNo;
        }

        // 一张票退多少：购物车订单一单多张票，只退这一张的成交价；
        // 一单只有一张票的老订单沿用整笔支付金额（行为不变，成交价和支付额本来就相等）。
        public static double RefundAmountFor(int validPassesInOrder, double dealPrice, double paymentAmount)
        {
            return validPassesInOrder > 1 ? Math.Round(dealPrice, 2) : paymentAmount;
        }

        // 预订失败（供应商没接单、也没有供应商订单号）的票：顾客已经付了款，要自动退这一张的钱
        public static bool NeedsAutoRefundForFailedBooking(int isCancel, int valid, string? resort, string? reserveNo, int? haveRefund, double? refundAmount)
        {
            if (isCancel != -2 || valid != 1) return false;
            if ((resort ?? "").Trim().Equals("南山")) return false;
            if (!string.IsNullOrWhiteSpace(reserveNo)) return false;
            return haveRefund == null && refundAmount == null;
        }

        // 「取消中」的雪票多久查一次自我游：取消申请后 30 分钟内每分钟查（通常几分钟就确认），
        // 之后每 10 分钟查一次，超过 24 小时还没确认就不再等（见 CancelTimedOut）。
        // 后台轮询每分钟调一次，这里按当前分钟数决定这一轮查不查。
        public static readonly TimeSpan CancelFastPollWindow = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan CancelGiveUpAfter = TimeSpan.FromHours(24);

        public static bool ShouldPollCancel(DateTime? requestedAt, DateTime now)
        {
            if (requestedAt == null) return true;
            TimeSpan age = now - requestedAt.Value;
            if (age <= CancelFastPollWindow) return true;
            return now.Minute % 10 == 0;
        }

        public static bool CancelTimedOut(DateTime? requestedAt, DateTime now)
        {
            return requestedAt != null && now - requestedAt.Value > CancelGiveUpAfter;
        }

        // 顾客看到的价格行不带进货价和建议售价；deal_price、marketPrice 保留
        public static void RedactDailyPrices(IEnumerable<SkipassDailyPrice>? prices)
        {
            if (prices == null) return;
            foreach (SkipassDailyPrice p in prices)
            {
                p.settlementPrice = 0;
                p.salePrice = 0;
            }
        }

        // 大好河山账号的商品目录里，搜「万龙」会同时返回雪票、次卡和「万龙&常规」这类酒店套餐。
        // 只有景区是万龙滑雪场、不是酒店/公寓、名字里没有「&」「住」的才是能单独卖的雪票或次卡。
        public static bool IsWanlongLiftProduct(string? name, string? viewName)
        {
            string n = name ?? "";
            string v = viewName ?? "";
            if (!v.Contains("万龙滑雪场")) return false;
            if (v.Contains("酒店") || v.Contains("公寓")) return false;
            if (n.Contains('&') || n.Contains('＆') || n.Contains('住')) return false;
            return true;
        }

        // 自我游查价格是按自然月返回的：从 travelDate 一直返回到当月月底。
        // 要拿到整季价格，就得先查起始日，再按每月 1 号往后查。
        public static List<DateTime> MonthStarts(DateTime from, int months)
        {
            var result = new List<DateTime>();
            if (months <= 0) return result;
            result.Add(from.Date);
            var firstOfMonth = new DateTime(from.Year, from.Month, 1);
            for (int i = 1; i < months; i++)
            {
                result.Add(firstOfMonth.AddMonths(i));
            }
            return result;
        }

        static readonly Regex SeasonRegex = new Regex(@"(?<!\d)(?:20)?(\d{2})\s*[-—~至]\s*(?:20)?(\d{2})(?!\d)");

        // 雪季起止日：商品名里写了「26-27」「2026-27」这类连续两年就按它算（10-1 ～ 次年 6-1，沿用旧商品的惯例），
        // 否则按今天所在雪季（7 月起算新一季）。「10.30-11.4」这类日期区间不是连续年份，不会误判。
        public static (DateTime Start, DateTime End) ResolveSeasonDates(string? productName, DateTime today)
        {
            if (!string.IsNullOrWhiteSpace(productName))
            {
                foreach (Match m in SeasonRegex.Matches(productName))
                {
                    int a = int.Parse(m.Groups[1].Value);
                    int b = int.Parse(m.Groups[2].Value);
                    if (b == (a + 1) % 100)
                    {
                        return (new DateTime(2000 + a, 10, 1), new DateTime(2000 + a + 1, 6, 1));
                    }
                }
            }
            int startYear = today.Month >= 7 ? today.Year : today.Year - 1;
            return (new DateTime(startYear, 10, 1), new DateTime(startYear + 1, 6, 1));
        }
    }
}
