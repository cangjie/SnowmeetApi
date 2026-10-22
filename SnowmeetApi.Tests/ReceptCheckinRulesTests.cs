using System;
using System.Collections.Generic;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    public class ReceptCheckinRulesTests
    {
        static readonly DateTime Now = new DateTime(2026, 10, 22, 14, 0, 0);

        static ScanQrCode VerifiedScan(int memberId = 7, string openId = "oid-a")
        {
            return new ScanQrCode
            {
                id = 1,
                code = ReceptCheckinRules.BuildCode("abcdefghij", 1),
                scaned = 1,
                scan_time = Now.AddMinutes(-5),
                scaner_openid = openId,
                scaner_member_id = memberId,
                cell = "13800138000",
                expire_time = Now.AddMinutes(-3)
            };
        }

        static OrderPayment Payment(int id, string method, DateTime created, int? memberId = 7,
            int valid = 1, string status = "待支付", int orderValid = 1)
        {
            return new OrderPayment
            {
                id = id,
                pay_method = method,
                create_date = created,
                valid = valid,
                status = status,
                order = new Order { id = id * 10, member_id = memberId, valid = orderValid }
            };
        }

        [Fact]
        public void 接待码带随机串且能识别()
        {
            string token = ReceptCheckinRules.NewToken();
            string code = ReceptCheckinRules.BuildCode(token, 123);

            Assert.Equal(10, token.Length);
            Assert.True(ReceptCheckinRules.IsCheckinCode(code));
            Assert.Equal("https://mini.snowmeet.top/mapp/recept_checkin?code=" + code, ReceptCheckinRules.BuildQrText(code));
            Assert.False(ReceptCheckinRules.IsCheckinCode("recept_interact_id_123"));
            Assert.False(ReceptCheckinRules.IsCheckinCode("rc_short_123"));
            Assert.False(ReceptCheckinRules.IsCheckinCode(null));
        }

        [Fact]
        public void 店员侧阶段按扫码与验证情况判定()
        {
            ScanQrCode fresh = new ScanQrCode { expire_time = Now.AddSeconds(60) };
            ScanQrCode expired = new ScanQrCode { expire_time = Now.AddSeconds(-1) };
            ScanQrCode stopped = new ScanQrCode { stoped = 1, expire_time = Now.AddSeconds(60) };
            ScanQrCode scannedNoPhone = new ScanQrCode { scaned = 1, scaner_member_id = 7, expire_time = Now.AddSeconds(-60) };

            Assert.Equal(ReceptCheckinRules.STAFF_WAITING_SCAN, ReceptCheckinRules.StaffStage(fresh, Now));
            Assert.Equal(ReceptCheckinRules.STAFF_EXPIRED, ReceptCheckinRules.StaffStage(expired, Now));
            Assert.Equal(ReceptCheckinRules.STAFF_STOPPED, ReceptCheckinRules.StaffStage(stopped, Now));
            Assert.Equal(ReceptCheckinRules.STAFF_NEED_PHONE, ReceptCheckinRules.StaffStage(scannedNoPhone, Now));
            // 已扫码的码过期或被作废都不影响已识别的顾客
            Assert.Equal(ReceptCheckinRules.STAFF_VERIFIED, ReceptCheckinRules.StaffStage(VerifiedScan(), Now));
        }

        [Fact]
        public void 店员输手机号后等店长授权不按二维码过期处理()
        {
            ScanQrCode waiting = new ScanQrCode { cell = "13800138000", scaner_member_id = 7, authed = 0, expire_time = Now.AddMinutes(-10) };
            ScanQrCode authed = new ScanQrCode { cell = "13800138000", scaner_member_id = 7, authed = 1, expire_time = Now.AddMinutes(-10) };
            ScanQrCode stopped = new ScanQrCode { cell = "13800138000", authed = 0, stoped = 1, expire_time = Now.AddMinutes(1) };

            Assert.True(ReceptCheckinRules.IsPhoneEntry(waiting));
            Assert.False(ReceptCheckinRules.IsPhoneEntry(VerifiedScan()));
            Assert.Equal(ReceptCheckinRules.STAFF_NEED_AUTH, ReceptCheckinRules.StaffStage(waiting, Now));
            Assert.Equal(ReceptCheckinRules.STAFF_PHONE_AUTHED, ReceptCheckinRules.StaffStage(authed, Now));
            Assert.Equal(ReceptCheckinRules.STAFF_STOPPED, ReceptCheckinRules.StaffStage(stopped, Now));
        }

        [Fact]
        public void 店员已经输了手机号的码顾客不能再扫()
        {
            ScanQrCode phoneEntry = new ScanQrCode { cell = "13800138000", expire_time = Now.AddSeconds(60) };

            Assert.NotNull(ReceptCheckinRules.CheckScanAllowed(phoneEntry, "oid-a", Now));
        }

        [Fact]
        public void 码过期作废或被别人扫过时拒绝报到()
        {
            ScanQrCode fresh = new ScanQrCode { expire_time = Now.AddSeconds(60) };
            ScanQrCode expired = new ScanQrCode { expire_time = Now.AddSeconds(-1) };
            ScanQrCode stopped = new ScanQrCode { stoped = 1, expire_time = Now.AddSeconds(60) };

            Assert.Null(ReceptCheckinRules.CheckScanAllowed(fresh, "oid-a", Now));
            Assert.NotNull(ReceptCheckinRules.CheckScanAllowed(fresh, null, Now));
            Assert.NotNull(ReceptCheckinRules.CheckScanAllowed(expired, "oid-a", Now));
            Assert.NotNull(ReceptCheckinRules.CheckScanAllowed(stopped, "oid-a", Now));
            Assert.NotNull(ReceptCheckinRules.CheckScanAllowed(VerifiedScan(openId: "oid-a"), "oid-b", Now));
        }

        [Fact]
        public void 本人重复打开时放行即使码已过期或被作废()
        {
            ScanQrCode scan = VerifiedScan(openId: "oid-a");
            scan.stoped = 1;

            Assert.Null(ReceptCheckinRules.CheckScanAllowed(scan, " oid-a ", Now));
        }

        [Fact]
        public void 未验证手机号时顾客侧停在验证步骤()
        {
            ScanQrCode scan = VerifiedScan();
            scan.cell = null;
            List<OrderPayment> payments = new() { Payment(1, "微信支付", Now.AddMinutes(-1)) };

            var (stage, paymentId) = ReceptCheckinRules.CustomerStage(scan, payments, Now);

            Assert.Equal(ReceptCheckinRules.CUSTOMER_NEED_PHONE, stage);
            Assert.Null(paymentId);
        }

        [Fact]
        public void 店员选微信支付后顾客侧拿到最新的支付单()
        {
            List<OrderPayment> payments = new()
            {
                Payment(1, "微信支付", Now.AddMinutes(-3)),
                Payment(2, "微信支付", Now.AddMinutes(-1))
            };

            var (stage, paymentId) = ReceptCheckinRules.CustomerStage(VerifiedScan(), payments, Now);

            Assert.Equal(ReceptCheckinRules.CUSTOMER_WEPAY, stage);
            Assert.Equal(2, paymentId);
        }

        [Fact]
        public void 店员改选支付宝后顾客侧只提示扫支付宝码()
        {
            List<OrderPayment> payments = new()
            {
                Payment(1, "微信支付", Now.AddMinutes(-3), valid: 0),
                Payment(2, "支付宝", Now.AddMinutes(-1))
            };

            var (stage, paymentId) = ReceptCheckinRules.CustomerStage(VerifiedScan(), payments, Now);

            Assert.Equal(ReceptCheckinRules.CUSTOMER_ALIPAY, stage);
            Assert.Equal(2, paymentId);
        }

        [Fact]
        public void 扫码前的单作废的单和别人的单都不选()
        {
            List<OrderPayment> payments = new()
            {
                Payment(1, "微信支付", Now.AddMinutes(-10)),
                Payment(2, "微信支付", Now.AddMinutes(-1), valid: 0),
                Payment(3, "微信支付", Now.AddMinutes(-1), memberId: 8),
                Payment(4, "微信支付", Now.AddMinutes(-1), status: "支付成功"),
                Payment(5, "微信支付", Now.AddMinutes(-1), orderValid: 0),
                Payment(6, "储值支付", Now.AddMinutes(-1))
            };

            var (stage, paymentId) = ReceptCheckinRules.CustomerStage(VerifiedScan(), payments, Now);

            Assert.Equal(ReceptCheckinRules.CUSTOMER_WAITING, stage);
            Assert.Null(paymentId);
        }

        [Fact]
        public void 等待超过60分钟没开单时顾客侧超时()
        {
            ScanQrCode scan = VerifiedScan();
            scan.scan_time = Now.AddMinutes(-61);

            var (stage, _) = ReceptCheckinRules.CustomerStage(scan, new List<OrderPayment>(), Now);

            Assert.Equal(ReceptCheckinRules.CUSTOMER_EXPIRED, stage);
        }

        [Fact]
        public void 手机号只给中间四位打码()
        {
            Assert.Equal("138****8000", ReceptCheckinRules.MaskCell("13800138000"));
            Assert.Equal("+85251234567", ReceptCheckinRules.MaskCell("+85251234567"));
            Assert.Equal("", ReceptCheckinRules.MaskCell(null));
        }
    }
}
