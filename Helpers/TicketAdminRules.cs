using System;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>券在管理后台列表上的状态标签 + 样式类。</summary>
    public class TicketStateView
    {
        public string Label { get; set; } = "";
        public string Cls { get; set; } = "";
    }

    /// <summary>
    /// 优惠券管理后台专用的纯规则。
    /// 与 TicketTransferRules 的分工：那边是「券本身」的业务规则（转赠、过期、领取上限），
    /// 这边是「管理后台怎么展示和分页」的规则。
    /// </summary>
    public static class TicketAdminRules
    {
        public const int DefaultPageSize = 20;
        public const int MaxPageSize = 100;

        /// <summary>
        /// 列表上的主状态。优先级：已核销 &gt; 已过期 &gt; 分享中 &gt; 未使用。
        ///
        /// valid / is_active 不在这里判——它们是**叠加**的徽标（"无效"/"未激活"），
        /// 跟主状态是两个维度，混在一起会让"已核销但 valid=0"这种券丢掉核销信息。
        ///
        /// WXML 不支持方法调用，这套文案只能在服务端（或 js 的 map 里）派生好。
        /// </summary>
        public static TicketStateView DescribeState(Ticket ticket, DateTime now)
        {
            if (ticket.used == 1)
            {
                return new TicketStateView() { Label = "已核销", Cls = "used" };
            }
            if (!TicketTransferRules.IsNotExpired(ticket, now))
            {
                return new TicketStateView() { Label = "已过期", Cls = "expired" };
            }
            if (ticket.shared == 1)
            {
                return new TicketStateView() { Label = "分享中", Cls = "shared" };
            }
            return new TicketStateView() { Label = "未使用", Cls = "unused" };
        }

        /// <summary>
        /// 券详情页的操作流水：把一条 ticket_log 归成人看得懂的类型。
        ///
        /// 与 TicketTransferRules.TransferLogFilter 的分工：那个只挑"真转赠"用来计数，
        /// 这里要把**每一条**都归类——店员排查一张券时，核销和撤回同样是关键线索，
        /// 不能像统计转赠次数那样把它们滤掉。
        ///
        /// ticket_log 的 5 个写入方见 TicketController：
        ///   AcceptTicketCore(转赠) / CancelShare(撤回) / Use(核销) /
        ///   ExperienceController、MaintainLogsController(开单发券)
        /// </summary>
        public static TicketStateView DescribeLogEntry(TicketLog log)
        {
            string memo = (log.memo ?? "").Trim();
            string sender = (log.sender_open_id ?? "").Trim();
            string accepter = (log.accepter_open_id ?? "").Trim();

            if (memo == "核销")
            {
                return new TicketStateView() { Label = "核销", Cls = "use" };
            }
            if (memo == "撤回分享" || (sender != "" && accepter == ""))
            {
                return new TicketStateView() { Label = "撤回分享", Cls = "cancel" };
            }
            if (memo.StartsWith("体验订单获得") || memo.StartsWith("养护订单获得") || memo == "管理员赠送")
            {
                return new TicketStateView() { Label = "发放", Cls = "grant" };
            }
            // 收发同一个人 = 旧系统的"发券给本人"，不是转赠
            if (sender != "" && accepter != "" && sender != accepter && memo != "")
            {
                return new TicketStateView() { Label = "转赠成功", Cls = "transfer" };
            }
            if (sender != "" && sender == accepter)
            {
                return new TicketStateView() { Label = "发放", Cls = "grant" };
            }
            return new TicketStateView() { Label = "其它", Cls = "other" };
        }

        /// <summary>
        /// 分页参数护栏。上限 100 是因为明细视图分页后要按 code 批量捞 ticket_log，
        /// 参数个数不能失控（SQL Server 参数上限 2100）。
        /// </summary>
        public static (int pageIndex, int pageSize) ClampPaging(int pageIndex, int pageSize)
        {
            if (pageIndex < 1)
            {
                pageIndex = 1;
            }
            if (pageSize < 1 || pageSize > MaxPageSize)
            {
                pageSize = DefaultPageSize;
            }
            return (pageIndex, pageSize);
        }
    }
}
