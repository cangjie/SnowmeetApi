using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("maintain_in_shop_request")]
    public class MaintainLive
    {
        [Key]
        public int id { get;}
        public string shop { get; set; }
        public string open_id { get; set; }
        
        public string equip_type { get; set; }
        public string brand { get; set; }
        public string scale { get; set; }
        public int edge { get; set; }
        public int candle { get; set; }
        public int repair_more { get; set; }
        public DateTime? pick_date { get; set; }
        
        public int task_id { get; set; }
        public int order_id { get; set; }
        public string service_open_id { get; set; }
        public string confirmed_equip_type { get; set; }
        public string confirmed_brand { get; set; }
        public string confirmed_serial { get; set; }
        public string confirmed_scale { get; set; }
        public string confirmed_year { get; set; }
        
        public int confirmed_edge { get; set; }
        public int confirmed_degree { get; set; }
        public int confirmed_candle { get; set; }
        public string confirmed_more { get; set; }
        public string confirmed_memo { get; set; }
        public DateTime confirmed_pick_date { get; set; }
        public double confirmed_additional_fee { get; set; }
        public string confirmed_cell { get; set; }
        public string confirmed_name { get; set; }
        public string confirmed_gender { get; set; }
        public int confirmed_product_id { get; set; }
        public string confirmed_images { get; set; }
        public int batch_id { get; set; }
        public int label_printed { get; set; }
        public string? task_flow_num { get; set; }
        public int finish { get; set; }
        public string ticket_code { get; set; }
        public DateTime create_date { get; set; }

        public string pay_method { get; set; }

        public string pay_memo { get; set; }


        //附加费用商品编号
        public int AddtionalFeeProductId
        {
            get
            {
                return 146;
            }
        }


    }
}

