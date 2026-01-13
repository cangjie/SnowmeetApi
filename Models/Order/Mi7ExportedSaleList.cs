using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;
namespace SnowmeetApi.Models
{
    [Table("mi7_imported_order")]
    public class Mi7ExportedSaleList
    {
        public DateTime? biz_date {get; set;}
        public string? deliver_method {get; set;}
        public DateTime? deliver_date {get; set;}
        public string? customer_contact {get; set;}
        public string? contact_cell {get; set;}
        public string? deliver_addr {get; set;}
        [Key]
        public string mi7_code {get; set;}
        public string? shop {get; set;}
        public string? associate_order_code {get; set;}
        public string? customer_code {get; set;}
        public string? customer_name {get; set;}
        public string? customer_class {get; set;}
        public string? product_name {get; set;}
        public int? product_count {get; set;}
        public double? order_discount {get; set;}
        public double? discount_amount {get; set;}
        public double? others_charge {get; set;}
        public double? total_amount {get; set;}
        public double? take_off_amount {get; set;}
        public double? real_charge {get; set;}
        public double? gross_profit {get; set;}
        public string? pay_mehtod {get; set;}
        public string? recept_staff {get; set;}
        public string? creater_staff {get; set;}
        public string? charge_type {get; set;}
        public string? store {get; set;}
        public string? memo {get; set;}
        public string? status {get; set;}
        public string? invoice_status {get; set;}
        public string? invoice_no {get; set;}
        public string? inner_memo {get; set;}
        public string? deliver_staff {get; set;}
        public string? edited_status {get; set;}
        public string? express_company {get; set;}
        public string? way_bill_no {get; set;}
        public DateTime create_date {get; set;}
        public DateTime? update_date {get; set;} = null;

        /*
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
        */
    }
}