using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using SnowmeetApi.Models.Rent;

namespace SnowmeetApi.Models
{
    public class SerialTest
    {
        public int id { get; set; }
        public string name { get; set; }
        public string cell { get; set; }
        public DateTime joinDate { get; set; }
    }

    [Table("recept")]
    public class Recept
    {
        [Key]
        public int id { get; set; }
        public string shop { get; set; }
        public string? open_id { get; set; }
        public int? member_id { get; set; } = null;
        public string? cell { get; set; } = null;
        public string? real_name { get; set; } = null;
        public string? gender { get; set; } = null;
        public int current_step { get; set; }
        public string recept_type { get; set; }
        public string? submit_data { get; set; } = null;
        public string? recept_staff { get; set; } = null;
        public int valid { get; set; } = 1;
        [NotMapped]
        public string recept_staff_name { get; set; } = "";
        public string update_staff { get; set; }
        [NotMapped]
        public string update_staff_name { get; set; } = "";
        //[ForeignKey(nameof(RentOrder))]
        public int? submit_return_id { get; set; } = null;
        public int? order_id { get; set; } = null;
        public string code { get; set; } = "";
        public DateTime create_date { get; set; } = DateTime.Now;
        public DateTime? update_date { get; set; } = null;
        public DateTime? submit_date { get; set; } = null;
        public int? scan_qrcode_id { get; set; } = null;
        public int? staff_id { get; set; } = null;
        public RentOrder _rentOrder;
        public Maintain.MaintainOrder _maintainOrder;
        [NotMapped]
        public SerialTest entity { get; set; }
        [NotMapped]
        public Rent.RentOrder? rentOrder
        {
            get
            {
                if (_rentOrder == null)
                {
                    if (recept_type.Trim().Equals("租赁下单") || recept_type.Trim().Equals("租赁招待"))
                    {
                        object order = JsonConvert.DeserializeObject(submit_data, typeof(Rent.RentOrder));
                        return (RentOrder)order;

                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return _rentOrder;
                }

            }
            set
            {
                _rentOrder = value;
            }
        }

        [NotMapped]
        public Maintain.MaintainOrder maintainOrder
        {
            get
            {
                if (_maintainOrder == null)
                {
                    if (recept_type.Trim().Equals("养护下单") || recept_type.Trim().Equals("养护招待"))
                    {
                        object order = JsonConvert.DeserializeObject(submit_data, typeof(Maintain.MaintainOrder));
                        return (Maintain.MaintainOrder)order;

                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return _maintainOrder;
                }
            }
            set
            {
                _maintainOrder = value;
            }

        }
        [NotMapped]
        public Member member { get; set; }

    }
}

