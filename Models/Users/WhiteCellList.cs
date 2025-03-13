using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.Users
{
    [Table("cell_white_list")]
    public class CellWhiteList
    {
        [Key]
        public string cell {get; set;}
    }
}