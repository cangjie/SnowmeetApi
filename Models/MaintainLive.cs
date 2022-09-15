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
        public string brand { get; set; } = "";
        public string scale { get; set; } = "";
        public int edge { get; set; } = 0;
        public int candle { get; set; } = 0;
        public int repair_more { get; set; } = 0;
        public DateTime? pick_date { get; set; } = DateTime.Now;

        public int task_id { get; set; } = 0;
        public int order_id { get; set; } = 0;
        public string service_open_id { get; set; } = "";
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
        public DateTime confirmed_pick_date { get; set; } = DateTime.Now;
        public double confirmed_additional_fee { get; set; }
        public string confirmed_cell { get; set; }
        public string confirmed_name { get; set; }
        public string confirmed_gender { get; set; }
        public int confirmed_product_id { get; set; }
        public string confirmed_images { get; set; } = "";

        public int confirmed_urgent { get; set; } = 0;
        public string confirmed_foot_length { get; set; } = "";
        public string confirmed_front { get; set; } = "";
        public string confirmed_height { get; set; } = "";
        public string confirmed_weight { get; set; } = "";
        public string confirmed_binder_gap { get; set; } = "";
        public string confirmed_front_din { get; set; } = "";
        public string confirmed_rear_din { get; set; } = "";
        public string confirmed_angle { get; set; } = "";
        public string confirmed_relation { get; set; } = "";

        public int batch_id { get; set; } = 0;
        public int label_printed { get; set; } = 0;
        public string? task_flow_num { get; set; } = "";
        public int finish { get; set; } = 0;
        public string ticket_code { get; set; } = "";
        public DateTime create_date { get; set; } = DateTime.Now;

        public string pay_method { get; set; } = "微信支付";

        public string pay_memo { get; set; } = "";


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

