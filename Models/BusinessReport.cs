using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
namespace SnowmeetApi.Models
{
	[Table("business_report")]
	public class BusinessReport
	{
        [Key]
        public int  id { get; set; }
        public DateTime date { get; set; }
        public string time { get; set; }
        public string? type { get; set; }
        public string? oaold_open_id { get; set; }
        public string? month { get; set; }
        public string? season { get; set; }
        public string? shop { get; set; }
        public string? oa_open_id { get; set; }
        public string? mini_open_id { get; set; }
        public string? nick { get; set; }
        public string? cell { get; set; }
        public string? real_name { get; set; }
        public string? gender { get; set; }
        public string? user_info_status { get; set; }
        public string? pay_method { get; set; }
        public string? mch_id { get; set; }
        public string? out_trade_no { get; set; }
        public string? TransactionId { get; set; }
        public string? business_id { get; set; }
        public string? business_type { get; set; }
        public string? business_detail { get; set; }
        public DateTime? ski_pass_book_date { get; set; }
        public string? ski_pass_rent { get; set; }
        public string? ski_pass_type { get; set; }
        public int? ski_pass_count { get; set; }
        public double? ski_pass_price { get; set; }
        public double? ski_pass_deposit { get; set; }
        public double? ski_pass_price_summary { get; set; }
        public double? ski_pass_deposit_summary { get; set; }
        public DateTime? ski_pass_use_date { get; set; }
        public string? ski_pass_use_oper_open_id { get; set; }
        public string? ski_pass_user_oper_name { get; set; }
        public string? memo { get; set; }
        public int is_test { get; set; } = 0;
        public string? order_status { get; set; }
        public string? order_type { get; set; }
        public string? income_type { get; set; }
        public int refund_times { get; set; }
        public double? total_summary { get; set; }

        public double? income { get; set; }
        public double? income_fee { get; set; }
        public double? income_real { get; set; }
        public string? income_oper_open_id { get; set; }
        public string? income_oper_name { get; set; }


        public double? refund_amount { get; set; }
        public double? refund_fee { get; set; }
        public double? refund_real { get; set; }
        public string? refund_oper_open_id { get; set; }
        public string? refund_oper_name { get; set; }

        public double? refund_summary { get; set; }
        public double? refund_fee_summary { get; set; }
        public double? refund_real_summary { get; set; }


        public DateTime? refund1_date { get; set; }
        public string? refund1_time { get; set; }
        public string? refund1_no { get; set; }
        public double? refund1_amount { get; set; }
        public double? refund1_fee { get; set; }
        public double? refund1_summary { get; set; }
        public string? refund1_oper_open_id { get; set; }
        public string? refund1_oper_name { get; set; }
        public string? refund1_type { get; set; }
        public DateTime? refund2_date { get; set; }
        public string? refund2_time { get; set; }
        public string? refund2_no { get; set; }
        public double? refund2_amount { get; set; }
        public double? refund2_fee { get; set; }
        public double? refund2_summary { get; set; }
        public string? refund2_oper_open_id { get; set; }
        public string? refund2_oper_name { get; set; }
        public string? refund2_type { get; set; }
        public DateTime? refund3_date { get; set; }
        public string? refund3_time { get; set; }
        public string? refund3_no { get; set; }
        public double? refund3_amount { get; set; }
        public double? refund3_fee { get; set; }
        public double? refund3_summary { get; set; }
        public string? refund3_oper_open_id { get; set; }
        public string? refund3_oper_name { get; set; }
        public string? refund3_type { get; set; }
        public DateTime? refund4_date { get; set; }
        public string? refund4_time { get; set; }
        public string? refund4_no { get; set; }
        public double? refund4_amount { get; set; }
        public double? refund4_fee { get; set; }
        public double? refund4_summary { get; set; }
        public string? refund4_oper_open_id { get; set; }
        public string? refund4_oper_name { get; set; }
        public string? refund4_type { get; set; }
        public DateTime? refund5_date { get; set; }
        public string? refund5_time { get; set; }
        public string? refund5_no { get; set; }
        public double? refund5_amount { get; set; }
        public double? refund5_fee { get; set; }
        public double? refund5_summary { get; set; }
        public string? refund5_oper_open_id { get; set; }
        public string? refund5_oper_name { get; set; }
        public string? refund5_type { get; set; }
        public DateTime? refund6_date { get; set; }
        public string? refund6_time { get; set; }
        public string? refund6_no { get; set; }
        public double? refund6_amount { get; set; }
        public double? refund6_fee { get; set; }
        public double? refund6_summary { get; set; }
        public string? refund6_oper_open_id { get; set; }
        public string? refund6_oper_name { get; set; }
        public string? refund6_type { get; set; }
    }
}

