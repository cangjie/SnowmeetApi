using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.UTV
{
    [Table("utv_vehicle_schedule")]
    public class UTVVehicleSchedule
    {
        [Key]
        public int id { get; set; }
        public int trip_id { get; set; }
        public int reserve_id { get; set; }
        public string car_no { get; set; }
        public string status { get; set; }
        public string start_mile { get; set; }
        public string end_mile { get; set; }
        public string line_type { get; set; }
        public double charge { get; set; }

        public double charge_discount { get; set; }
        public double deposit { get; set; }
        public double deposit_discount { get; set; }
        public string ticket_code { get; set; }
        public double ticket_discount { get; set; }
        public int driver_user_id { get; set; }
        public string driver_insurance { get; set; }
        public int passenger_user_id { get; set; }
        public string passenger_insurance { get; set; }
        public string memo { get; set; }

        [NotMapped]
        public UTVUsers driver { get; set; }
        [NotMapped]
        public UTVUsers passenger { get; set; }

        [NotMapped]
        public bool haveDriverLicense { get; set; }
        [NotMapped]
        public bool havePassengerLicense { get; set; }
        [NotMapped]
        public bool haveDriverInsurance { get; set; }
        [NotMapped]
        public bool havePassengerInsurance { get; set; }
        [NotMapped]
        public bool canGo { get; set; } = false;
        
    }
}
