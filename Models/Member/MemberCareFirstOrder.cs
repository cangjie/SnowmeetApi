using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    /// <summary>
    /// 会员的养护首单：这个会员第一次下「有效」养护订单的那一单，记在 member 表的两个可空列上
    /// （care_first_order_id = 订单 id，care_first_order_date = 当时的开单时间）。
    /// 养护订单列表的「新」标识、以后的报表都只读这一份，不各自从订单表现算（口径见 CareNewCustomerRules、
    /// 维护见 CareFirstOrderService）。记录不依赖订单表一直保留旧单：旧单被归档 / 删除后，这两列仍然保留。
    ///
    /// 这两列只出现在这个实体里，Member 实体不映射它们（两个实体共用 member 表，主键都是 id）。这样：
    ///   · 会员的整行保存（Entry(member).State = Modified）只写 Member 自己的列，不会拿旧副本覆盖首单；
    ///   · 会员的任何查询都不带这两列——代码先于加列脚本上线时，只是「新」暂时不显示，会员相关功能不受影响。
    /// 线上的列由 snowmeet_ai_doc/sql/2026-11-10_member_care_first_order.sql 加（含一次性回填）；
    /// 这里的列必须和脚本一一对应（CareFirstOrderTests.SchemaScript_* 会核对）。
    /// 写入只走 CareFirstOrderService（直接 UPDATE 这两列，不走 SaveChanges）。
    /// </summary>
    [Table("member")]
    public class MemberCareFirstOrder
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int member_id { get; set; }
        [Column("care_first_order_id")]
        public int? order_id { get; set; }
        [Column("care_first_order_date")]
        public DateTime? biz_date { get; set; }
    }
}
