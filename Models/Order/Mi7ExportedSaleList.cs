using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;
namespace SnowmeetApi.Models
{
    [Table("sale_list")]
    public class Mi7ExportedSaleList
    {
        public string 业务日期 {get; set;}
        public string 送货方式 {get; set;}
        public string 送货日期 {get; set;}
        public string 客户联系人 {get; set;}
        public string 联系电话 {get; set;}
        public string 送货地址 {get; set;}
        [Key]
        public string 单据编号 {get; set;}
        public string 所属门店 {get; set;}
        public string 关联订单号 {get; set;}
        public string 客户编号 {get; set;}
        public string 客户名称 {get; set;}
        public string 客户分类 {get; set;}
        public string 销售商品 {get; set;}
        public string 销售数量 {get; set;}
        public string 整单折扣额 {get; set;}
        public string 折扣金额 {get; set;}
        public string 其他费用 {get; set;}
        public string 总计金额 {get; set;}
        public string 扣除订金 {get; set;}
        public string 实收金额 {get; set;}
        public string 销售毛利 {get; set;}
        public string 结算方式 {get; set;}
        public string 经手人 {get; set;}
        public string 制单人 {get; set;}
        public string 销售类型 {get; set;}
        public string 仓库 {get; set;}
        public string 备注 {get; set;}
        public string 状态 {get; set;}
        public string 发票状态 {get; set;}
        public string 发票号 {get; set;}
        public string 内部备注 {get; set;}
        public string 配送员 {get; set;}
        public string 编辑状态 {get; set;}
        public string 物流公司 {get; set;}
        public string 物流单号 {get; set;}
        public int? mi7_order_id {get; set;}
        public string cell 
        {
            get
            {
                Match m = reg.Match(客户名称);
                if (m.Success)
                {
                    return m.Value.Trim();
                }
                else
                {
                    return "";
                }
            }
        }
        public string name
        {
            get
            {
                return 客户名称.Replace(cell, "");
            }
        }

        public int[] orderIdArr
        {
            get
            {
                string word = 备注 + " " + 内部备注;
                MatchCollection matches = regOrderId.Matches(word);
                int[] arr = new int[matches.Count];
                for(int i = 0; i < arr.Length; i++)
                {
                    arr[i] = int.Parse(matches[i].Value.Trim());
                }
                return arr;
            }
        }

        public static Regex reg = new Regex("1\\d{10}");
        public static Regex regOrderId = new Regex("[45]\\d{4}");
    }
}