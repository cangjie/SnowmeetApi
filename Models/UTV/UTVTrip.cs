using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.UTV
{
    [Table("utv_trip")]
    public class UTVTrip
    {
        [Key]
        public int id { get; set; }
        public DateTime trip_date { get; set; }
        public string trip_name { get; set; }
        public string status { get; set; }
        public int reserve_able { get; set; }

        [NotMapped]
        public IEnumerable<UTVVehicleSchedule> vehicleSchedule { get; set; } 

    }
}
