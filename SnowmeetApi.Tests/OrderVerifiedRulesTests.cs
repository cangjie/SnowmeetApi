using System;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    public class OrderVerifiedRulesTests
    {
        static readonly DateTime Now = new DateTime(2026, 10, 23, 14, 0, 0);
        const int Staff = 28;
        const int Member = 7;

        // 店员 28 出的码，会员 7 用微信扫码并验证了手机号，5 分钟前
        static ScanQrCode VerifiedCheckin()
        {
            return new ScanQrCode
            {
                id = 1,
                code = ReceptCheckinRules.BuildCode("abcdefghij", 1),
                staff_id = Staff,
                scaned = 1,
                scan_time = Now.AddMinutes(-5),
                scaner_openid = "oid-a",
                scaner_member_id = Member,
                cell = "13800138000"
            };
        }

        [Fact]
        public void 本人扫码验证过的报到能担保同一会员的订单()
        {
            Assert.True(OrderVerifiedRules.CheckinVouches(VerifiedCheckin(), Staff, Member, Now));
        }

        [Fact]
        public void 订单会员不是报到会员不担保()
        {
            Assert.False(OrderVerifiedRules.CheckinVouches(VerifiedCheckin(), Staff, 8, Now));
            Assert.False(OrderVerifiedRules.CheckinVouches(VerifiedCheckin(), Staff, null, Now));
        }

        [Fact]
        public void 店员输手机号进入和没验证手机号的报到不担保()
        {
            // 店员输手机号、店长已授权：没人扫码
            ScanQrCode phoneEntry = VerifiedCheckin();
            phoneEntry.scaned = 0;
            phoneEntry.scan_time = null;
            phoneEntry.authed = 1;
            Assert.False(OrderVerifiedRules.CheckinVouches(phoneEntry, Staff, Member, Now));

            // 扫了码但还没验证手机号
            ScanQrCode noPhone = VerifiedCheckin();
            noPhone.cell = null;
            Assert.False(OrderVerifiedRules.CheckinVouches(noPhone, Staff, Member, Now));
        }

        [Fact]
        public void 别的店员出的码不担保()
        {
            Assert.False(OrderVerifiedRules.CheckinVouches(VerifiedCheckin(), 31, Member, Now));
        }

        [Fact]
        public void 报到超过60分钟不担保()
        {
            ScanQrCode sq = VerifiedCheckin();
            sq.scan_time = Now.AddMinutes(-60);
            Assert.False(OrderVerifiedRules.CheckinVouches(sq, Staff, Member, Now));
            sq.scan_time = Now.AddMinutes(-59);
            Assert.True(OrderVerifiedRules.CheckinVouches(sq, Staff, Member, Now));
        }

        [Fact]
        public void 不是接待报到码不担保()
        {
            ScanQrCode sq = VerifiedCheckin();
            sq.code = "cp_abcdefghij_9_1";
            Assert.False(OrderVerifiedRules.CheckinVouches(sq, Staff, Member, Now));
            Assert.False(OrderVerifiedRules.CheckinVouches(null, Staff, Member, Now));
        }

        [Fact]
        public void 草稿保存_报到担保就是1()
        {
            Assert.True(OrderVerifiedRules.ResolveOnSave(true, false, null, Member));
        }

        [Fact]
        public void 草稿保存_会员没换保留已有的1_换了人变0()
        {
            Assert.True(OrderVerifiedRules.ResolveOnSave(false, true, Member, Member));
            Assert.False(OrderVerifiedRules.ResolveOnSave(false, true, Member, 8));
            Assert.False(OrderVerifiedRules.ResolveOnSave(false, true, Member, null));
        }

        [Fact]
        public void 草稿保存_库里是0又没有担保就是0()
        {
            Assert.False(OrderVerifiedRules.ResolveOnSave(false, false, Member, Member));
        }

        [Fact]
        public void 支付成功_本人微信付款是1()
        {
            Assert.True(OrderVerifiedRules.ResolveOnPaid(false, Member, Member, "微信支付", false, Member));
            // 订单转归我：付款方成了订单会员，本人微信付款
            Assert.True(OrderVerifiedRules.ResolveOnPaid(false, null, 8, "微信支付", false, 8));
        }

        [Fact]
        public void 支付成功_已核验的单本人支付宝付款或他人代付保持1()
        {
            Assert.True(OrderVerifiedRules.ResolveOnPaid(true, Member, Member, "支付宝", false, Member));
            Assert.True(OrderVerifiedRules.ResolveOnPaid(true, Member, Member, "微信支付", true, 8));
        }

        [Fact]
        public void 支付成功_付款改了订单会员不保留原来的1()
        {
            // 已核验会员 7 的单，被会员 8 用支付宝「订单转归我」付了款
            Assert.False(OrderVerifiedRules.ResolveOnPaid(true, Member, 8, "支付宝", false, 8));
        }

        [Fact]
        public void 支付成功_没核验过的单非本人微信付款是0()
        {
            Assert.False(OrderVerifiedRules.ResolveOnPaid(false, Member, Member, "支付宝", false, Member));
            Assert.False(OrderVerifiedRules.ResolveOnPaid(false, Member, Member, "微信支付", true, 8));
        }
    }
}
