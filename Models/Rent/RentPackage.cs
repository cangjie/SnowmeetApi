using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
namespace SnowmeetApi.Models
{
    public class ShopRentPackage
    {
        public Shop shop  {get; set;} = null;
        public List<RentPackage> rentPackages { get; set; } = new List<RentPackage>();
    }
    [Table("rent_package")]
    public class RentPackage
    {
        [Key]
        public int id { get; set; }
        public string? shop {get; set;}
        public int? item_count {get; set;}
        public string name { get; set; }
        public string description { get; set; }
        public string? package_type { get; set; }
        public double deposit { get; set; }
        public int valid { get; set; }
        public int? staff_id { get; set; } = null;
        public DateTime update_date { get; set; }
        public List<RentPackageCategory>? rentPackageCategoryList { get; set; } = null;
        [NotMapped]
        public List<RentPackageItemCategories>? rentPackageItemCategories
        {
            get
            {
                List<RentPackageItemCategories> list = new List<RentPackageItemCategories>();
                for(int i = 0; i < item_count; i++)
                {
                    if (rentPackageCategoryList == null || i >= rentPackageCategoryList.Count)
                    {
                        continue;
                    }
                    RentPackageItemCategories itemC = new RentPackageItemCategories();
                    itemC.itemIndex = i;
                    List<RentPackageCategory> packageCategories = rentPackageCategoryList.Where(c => c.item_index == i).ToList();
                    itemC.categories = new List<RentCategory>();
                    for(int j = 0; j < packageCategories.Count; j++)
                    {
                        itemC.categories.Add(packageCategories[j].rentCategory);
                    }
                    list.Add(itemC);
                }
                return list.OrderBy(l => l.itemIndex).ToList();
            }
        }
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
        [Key]
        public int id {get; set;}
        public int package_id { get; set; }
        public int item_index {get; set;}
        public int category_id { get; set; }
        public bool valid  {get; set;}
        public DateTime update_date { get; set; }
        public DateTime create_date { get; set; }
        [ForeignKey("category_id")]
        public RentCategory rentCategory { get; set; }
        [ForeignKey("package_id")]
        public RentPackage rentPackage { get; set; }
    }
    public class RentPackageItemCategories
    {
        public int itemIndex {get; set;}
        public List<RentCategory> categories { get; set; }
    }
}