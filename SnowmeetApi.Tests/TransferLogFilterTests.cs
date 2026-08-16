using System;
using System.Linq;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    // 「哪条 ticket_log 才算一次真转赠」。
    //
    // ticket_log 有 5 个写入方，只有 1 个是真转赠：
    //   TicketController.AcceptTicketCore  sender=原持有人  accepter=接受人   memo=分享获得/扫码关注公众号后自动接受  ← 真转赠
    //   TicketController.CancelShare       sender=本人      accepter=""      memo=撤回分享
    //   TicketController.Use（核销）        sender=""        accepter=店员     memo=核销
    //   ExperienceController               sender=店员      accepter=顾客     memo=体验订单获得,ID:x
    //   MaintainLogsController             sender=店员      accepter=顾客     memo=养护订单获得,ID:x
    //
    // 2026-08-15 生产库实测：只判 accepter<>'' && accepter<>sender 会命中 4158 条，
    // 加上 sender<>'' 是 3580 条，再加 memo 黑名单才是真实的 17 条。差 245 倍。
    public class TransferLogFilterTests
    {
        private static bool Hit(string sender, string accepter, string memo)
        {
            var f = TicketTransferRules.TransferLogFilter().Compile();
            return f(new TicketLog()
            {
                code = "x", sender_open_id = sender, accepter_open_id = accepter,
                memo = memo, transact_time = new DateTime(2026, 8, 15)
            });
        }

        [Fact]
        public void 真转赠计入()
        {
            Assert.True(Hit("oSenderAAA", "oAccepterBBB", "分享获得"));
        }

        [Fact]
        public void 扫码关注自动接受也是真转赠()
        {
            Assert.True(Hit("oSenderAAA", "oAccepterBBB", "扫码关注公众号后自动接受"));
        }

        [Fact]
        public void 核销日志不算转赠()
        {
            // Use() 写的是 sender_open_id=""、accepter=店员 openid。
            // 这条如果不排掉，每核销一次就给那张券多算一次转赠（生产库 575 条）。
            Assert.False(Hit("", "oStaffCCC", "核销"));
        }

        [Fact]
        public void 撤回分享不算转赠()
        {
            Assert.False(Hit("oSenderAAA", "", "撤回分享"));
        }

        [Fact]
        public void 收发同一个人不算转赠()
        {
            // 旧系统把"发券给本人"也写进 ticket_log
            Assert.False(Hit("oSameAAA", "oSameAAA", "养护订单获得，ID:3533"));
        }

        [Fact]
        public void 养护订单发券不算转赠()
        {
            // 店员开单发给顾客，收发是两个人，光靠 sender/accepter 判不出来，必须看 memo
            Assert.False(Hit("oStaffCCC", "oCustomerDDD", "养护订单获得，ID:2700"));
        }

        [Fact]
        public void 体验订单发券不算转赠()
        {
            Assert.False(Hit("oStaffCCC", "oCustomerDDD", "体验订单获得，ID:2831"));
        }

        [Fact]
        public void 管理员赠送不算转赠()
        {
            Assert.False(Hit("oStaffCCC", "oCustomerDDD", "管理员赠送"));
        }

        [Fact]
        public void 空memo不算转赠()
        {
            // 生产库有 32 条 memo 为空、收发不同人的记录，来源不明，保守排除
            Assert.False(Hit("oSenderAAA", "oAccepterBBB", ""));
        }

        [Fact]
        public void openid为null不抛异常()
        {
            Assert.False(Hit(null, "oAccepterBBB", "分享获得"));
            Assert.False(Hit("oSenderAAA", null, "分享获得"));
        }

        [Fact]
        public void memo为null不抛异常()
        {
            Assert.False(Hit("oSenderAAA", "oAccepterBBB", null));
        }

        [Fact]
        public void 混合集合只挑出真转赠()
        {
            var f = TicketTransferRules.TransferLogFilter().Compile();
            var logs = new[]
            {
                new TicketLog { code = "t1", sender_open_id = "oA", accepter_open_id = "oB", memo = "分享获得" },
                new TicketLog { code = "t1", sender_open_id = "", accepter_open_id = "oStaff", memo = "核销" },
                new TicketLog { code = "t2", sender_open_id = "oA", accepter_open_id = "", memo = "撤回分享" },
                new TicketLog { code = "t3", sender_open_id = "oStaff", accepter_open_id = "oC", memo = "体验订单获得，ID:1" },
                new TicketLog { code = "t4", sender_open_id = "oB", accepter_open_id = "oC", memo = "扫码关注公众号后自动接受" }
            };
            Assert.Equal(new[] { "t1", "t4" }, logs.Where(f).Select(l => l.code).ToArray());
        }
    }
}
