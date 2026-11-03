using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 介绍人分账的纯规则。设计：snowmeet_ai_doc/docs/superpowers/specs/2026-11-02-introducer-commission-design.md

    public static class IntroducerRules
    {
        public const string TypeCoach = "教练";
        public const string TypeKol = "KOL";
        public const string TypeOrg = "机构";

        /// <summary>万龙租赁：介绍人拿租金（扣减免后，和万龙那份同一个基数）的 10%。</summary>
        public const double RentShareRate = 0.10;

        public static bool IsValidType(string? type) => type == TypeCoach || type == TypeKol || type == TypeOrg;

        /// <summary>收款账号的类型：机构是公司，其余是个人。</summary>
        public static string RelationType(string type) => type == TypeOrg ? "公司" : "个人";

        public static double SuggestedRentShare(double rentAfterDiscounts) => Math.Round(rentAfterDiscounts * RentShareRate, 2);
    }

    // 自动分账的 30% 门槛。基数两个平台不同，见 WanlongShareRules.ShareBase。
    public static class ShareCapRules
    {
        public const double AutoCap = 0.3;
        public const double PerPaymentSplit = 0.29;

        /// <summary>这一单所有分账的合计达到各笔付款基数之和的 30%，就整单都不自动分。</summary>
        public static bool IsOverCap(double totalShare, IEnumerable<OrderPayment> payments)
            => totalShare >= payments.Sum(p => WanlongShareRules.ShareBase(p)) * AutoCap;

        /// <summary>这笔付款还能再分多少（基数的 29% 扣掉已经分出去的）。</summary>
        public static double Capacity(OrderPayment payment, double alreadyShared)
            => Math.Max(0, Math.Round(WanlongShareRules.ShareBase(payment) * PerPaymentSplit - alreadyShared, 2));

        public static string OverCapMemo(OrderPayment payment)
            => payment.pay_method != null && payment.pay_method.Trim() == "微信支付" && WanlongShareRules.WechatCapOnNet
                ? "分账金额超过实收（扣除退款）的30%，不自动分账" : "分账金额超过付款总额的30%，不自动分账";
    }

    // 收款方在某个付款渠道上能不能收款。只用于介绍人的收款方；万龙那条在支付宝的绑定不记在 share_relation_bind 里。
    public static class ShareReceiverRules
    {
        public static string? Problem(OrderShareRelation relation, IEnumerable<ShareRelationBind> binds, string payMethod)
        {
            switch ((payMethod ?? "").Trim())
            {
                case "微信支付":
                    return string.IsNullOrWhiteSpace(relation.wepay_account_type) || string.IsNullOrWhiteSpace(relation.wepay_account_num)
                        ? "介绍人没有微信收款账号" : null;
                case "支付宝":
                    if (string.IsNullOrWhiteSpace(relation.ali_account_type) || string.IsNullOrWhiteSpace(relation.ali_account_num))
                    {
                        return "介绍人没有支付宝收款账号";
                    }
                    return binds.Any(b => b.valid && (b.pay_method ?? "").Trim() == "支付宝") ? null : "未绑定分账关系";
                default:
                    return null;
            }
        }
    }

    // payment_share.memo 里系统自己写的备注。重算时先去掉这几句，再写当前该有的，人工备注原样保留。
    public static class ShareMemoRules
    {
        public const string TicketMemo = "租赁用了优惠券，不自动分账";

        public static readonly string[] KnownAutoMemos =
        {
            TicketMemo,
            "分账金额超过实收（扣除退款）的30%，不自动分账",
            "分账金额超过付款总额的30%，不自动分账",
            "介绍人没有微信收款账号",
            "介绍人没有支付宝收款账号",
            "未绑定分账关系",
        };

        public static string? WithAuto(string? memo, string? autoMemo)
        {
            List<string> parts = (memo ?? "").Split('；', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => !KnownAutoMemos.Contains(p)).ToList();
            if (!string.IsNullOrWhiteSpace(autoMemo))
            {
                parts.Add(autoMemo);
            }
            return parts.Count == 0 ? null : string.Join("；", parts);
        }
    }
}
