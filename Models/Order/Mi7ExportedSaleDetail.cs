using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("sale_detail")]
    public class Mi7ExportedSaleDetail
    {
        public DateTime? 业务日期 { get; set; }
        public string? 送货方式 { get; set; }
        public string? 送货日期 { get; set; }
        public string? 客户联系人 { get; set; }
        public string? 联系电话 { get; set; }
        public string? 送货地址 { get; set; }
        public string? 单据编号 { get; set; }
        public string? 所属门店 { get; set; }
        public string? 关联订单号 { get; set; }
        public string? 客户编号 { get; set; }
        public string? 客户名称 { get; set; }
        public string? 客户分类 { get; set; }
        public string? 商品编号 { get; set; }
        public string? 商品名称 { get; set; }
        public string? 商品分类 { get; set; }
        public string? 规格 { get; set; }
        public string? 属性 { get; set; }
        public string? 单位 { get; set; }
        public string? 商品条码 { get; set; }
        public string? 出库仓库 { get; set; }
        public string? 数量 { get; set; }
        public string? 单价 { get; set; }
        public string? 折扣 { get; set; }
        public string? 折后单价 { get; set; }
        public string? 总额 { get; set; }
        public string? 成本额 { get; set; }
        public string? 重量 { get; set; }
        public string? 体积 { get; set; }
        public string? 备注 { get; set; }
        public string? 经手人 { get; set; }
        public string? 制单人 { get; set; }
        public string? 内部备注 { get; set; }
        public string? 物流公司 { get; set; }
        public string? 物流单号 { get; set; }
    }
}