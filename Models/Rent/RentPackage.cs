using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Rent
{
    [Table("rent_package")]
    public class RentPackage
    {
        [Key]
        public int id { get; set; }
        public string name {get; set; }
        public string description {get; set; }
        public double deposit {get; set;}
        public int is_delete {get; set;}
        public DateTime update_date {get; set; }    

        [NotMapped]
        public ICollection<RentPackageCategory> rentPackageCategoryList { get; set; }

        [NotMapped]
        public ICollection<RentPrice> rentPackagePriceList {get; set;}
    }

    [Table("rent_package_category_list")]
    public class RentPackageCategory
    {
        public int package_id {get; set; }
        public string category_code {get; set; }
        public DateTime update_date {get; set;}
    }
}