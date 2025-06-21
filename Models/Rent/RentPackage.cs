using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("rent_package")]
    public class RentPackage
    {
        [Key]
        public int id { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public double deposit { get; set; }
        public int valid { get; set; }
        public DateTime update_date { get; set; }
        public List<RentPackageCategory>? rentPackageCategoryList { get; set; }
        public List<RentPrice> rentPackagePriceList { get; set; }
        [NotMapped]
        public List<RentCategory>? categories
        {
            get
            {
                if (rentPackageCategoryList == null)
                {
                    return null;
                }
                List<RentCategory> list = new List<RentCategory>();
                foreach (RentPackageCategory c in rentPackageCategoryList)
                {
                    list.Add(c.rentCategory);
                }
                return list;
            }
        }
    }
    [Table("rent_package_category_list")]
    public class RentPackageCategory
    {
        public int package_id { get; set; }
        public int category_id { get; set; }
        public DateTime update_date { get; set; }

        [ForeignKey("category_id")]
        public RentCategory rentCategory { get; set; }
        [ForeignKey("package_id")]
        public RentPackage rentPackage { get; set; }
    }
}