using System;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    // 券详情页的操作流水：把 ticket_log 一条记录归成人看得懂的类型。
    // ticket_log 有 5 个写入方（见 TransferLogFilterTests 的注释），详情页要**全部**显示，
    // 不像转赠次数那样只数真转赠——店员排查一张券时，核销和撤回同样是关键线索。
    public class DescribeLogEntryTests
    {
        private static string Label(string sender, string accepter, string memo)
        {
            return TicketAdminRules.DescribeLogEntry(new TicketLog()
            {
                code = "x", sender_open_id = sender, accepter_open_id = accepter, memo = memo
            }).Label;
        }

        [Fact]
        public void 真转赠()
        {
            Assert.Equal("转赠成功", Label("oA", "oB", "分享获得"));
        }

        [Fact]
        public void 扫码关注后自动接受也是转赠成功()
        {
            Assert.Equal("转赠成功", Label("oA", "oB", "扫码关注公众号后自动接受"));
        }

        [Fact]
        public void 核销()
        {
            Assert.Equal("核销", Label("", "oStaff", "核销"));
        }

        [Fact]
        public void 撤回分享()
        {
            Assert.Equal("撤回分享", Label("oA", "", "撤回分享"));
        }

        [Fact]
        public void 开单发券归为发放()
        {
            Assert.Equal("发放", Label("oStaff", "oCustomer", "养护订单获得，ID:2700"));
            Assert.Equal("发放", Label("oStaff", "oCustomer", "体验订单获得，ID:2831"));
            Assert.Equal("发放", Label("oStaff", "oCustomer", "管理员赠送"));
        }

        [Fact]
        public void 收发同一人的历史自发券也归为发放()
        {
            // 旧系统把"发券给本人"也写进 ticket_log，收发是同一个 openid
            Assert.Equal("发放", Label("oSame", "oSame", "养护订单获得，ID:3533"));
        }

        [Fact]
        public void 认不出来的不瞎猜()
        {
            Assert.Equal("其它", Label("oA", "oB", ""));
        }

        [Fact]
        public void 空值不抛异常()
        {
            Assert.Equal("其它", Label(null, null, null));
        }

        [Fact]
        public void 每种类型有独立样式类()
        {
            var seen = new System.Collections.Generic.List<string>();
            foreach (var l in new[]
            {
                new TicketLog { sender_open_id = "oA", accepter_open_id = "oB", memo = "分享获得" },
                new TicketLog { sender_open_id = "", accepter_open_id = "oS", memo = "核销" },
                new TicketLog { sender_open_id = "oA", accepter_open_id = "", memo = "撤回分享" },
                new TicketLog { sender_open_id = "oS", accepter_open_id = "oC", memo = "管理员赠送" },
                new TicketLog { sender_open_id = "oA", accepter_open_id = "oB", memo = "" }
            })
            {
                seen.Add(TicketAdminRules.DescribeLogEntry(l).Cls);
            }
            Assert.Equal(5, new System.Collections.Generic.HashSet<string>(seen).Count);
        }
    }
}
