using System;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 「封板」规则（用户 2026-11-09 提出）。
    ///
    /// 封板 = 热蜡之后不刮蜡，把蜡留在板面上封存过季。它不是养护工序（不生成任务、不计费），
    /// 只是 care.seal_board 上的一个标志：取板（「发板」任务完成）后给顾客发一张「开板刮蜡券」，
    /// 开板时凭券刮蜡。
    ///
    /// 开单口径：有热蜡时，「刮蜡」和「封板」必选其一、只能选一，默认刮蜡。
    /// 没有热蜡 / 非雪季养护 / 机打蜡不能封板。
    /// </summary>
    public static class CareSealRules
    {
        /// <summary>开板刮蜡券的券模板 id（ticket_template.id = 20，生产库已建）。</summary>
        public const int SealTicketTemplateId = 20;

        /// <summary>
        /// 发出去的券的 create_memo。带上 care.id：既是后台看到的发券原因，也是「同一件装备只发一次」的去重依据
        /// （不能用 ticket.biz_id 去重——券被核销时它会被改写成核销那件装备的 id）。
        /// </summary>
        public static string GrantMemo(int careId) => "封板取板赠送（养护#" + careId + "）";

        /// <summary>
        /// 这件 care 有没有「蜡」这一类业务：热蜡或养板（二者互斥，互相替换）。
        /// 刮蜡 / 封板的联动都围绕它转：有它刮蜡与封板才二选一。
        /// </summary>
        public static bool HasWaxService(Care care)
        {
            return care != null && (care.need_wax == 1 || care.maintain_board == 1);
        }

        /// <summary>这件 care 当前的服务组合允许封板：必须选了热蜡或养板，且不是非雪季养护、不是机打蜡。</summary>
        public static bool CanSeal(Care care)
        {
            return HasWaxService(care) && care.free_wax != 1
                && string.IsNullOrWhiteSpace(care.summer);
        }

        /// <summary>
        /// 把服务项收敛到合法组合（服务端是真理之源，前端按响应回填）：
        /// ① 养板与热蜡 / 机打蜡互斥、非雪季没有养板（热蜡和养板同时勾了以养板为准——界面不该发生，服务端兜底）；
        /// ② 能封板时刮蜡和封板恰好二选一（封板 → 不刮蜡；否则刮蜡，即默认值）；不能封板时 seal_board 一律清零。
        /// 不动修刃等其他项。
        /// </summary>
        public static void Normalize(Care care)
        {
            if (care == null)
            {
                return;
            }
            if (care.maintain_board == 1)
            {
                if (care.free_wax == 1 || !string.IsNullOrWhiteSpace(care.summer))
                {
                    care.maintain_board = 0;
                }
                else
                {
                    care.need_wax = 0;
                }
            }
            if (!CanSeal(care))
            {
                care.seal_board = 0;
                return;
            }
            if (care.seal_board == 1)
            {
                care.need_unwax = 0;
            }
            else
            {
                care.seal_board = 0;
                care.need_unwax = 1;
            }
        }

        /// <summary>取板后要不要给这件装备的顾客发开板刮蜡券：封板了，且不是取消发板。</summary>
        public static bool ShouldGrantTicket(Care care)
        {
            return care != null && care.seal_board == 1 && !care.is_cancel;
        }
    }
}
