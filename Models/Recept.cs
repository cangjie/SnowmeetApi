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
        public string open_id { get; set; }
        public string cell { get; set; }
        public string real_name { get; set; }
        public int current_step { get; set; }
        public string gender { get; set; }
        public string recept_type { get; set; }
        public string submit_data { get; set; } = "";
        public string recept_staff { get; set; }

        [NotMapped]
        public string recept_staff_name { get; set; } = "";

        public string update_staff { get; set; }

        [NotMapped]
        public string update_staff_name { get; set; } = "";
        public int submit_return_id { get; set; } = 0;
        public string code { get; set; } = "";
        public DateTime create_date { get; set; }
        public DateTime update_date { get; set; }
        public DateTime? submit_date { get; set; }

        public RentOrder _rentOrder;

        [NotMapped]
        public SerialTest entity { get; set; }

        [NotMapped]
        public Rent.RentOrder? rentOrder
        {
            get
            {
                if (_rentOrder == null)
                {
                    if (recept_type.Trim().Equals("租赁下单"))
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
                if (recept_type.Trim().Equals("养护下单"))
                {
                    object order = JsonConvert.DeserializeObject(submit_data, typeof(Maintain.MaintainOrder));
                    return (Maintain.MaintainOrder)order;

                }
                else
                {
                    return null;
                }
            }

        }

       


	}
}

