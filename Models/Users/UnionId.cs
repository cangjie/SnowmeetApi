using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SnowmeetApi.Models.Users
{
    [Table("unionids")]
    [Keyless]
    public class UnionId
    {
        public UnionId()
        {
        }
       
        public string union_id { get; set; }
        public string open_id { get; set; }
        public string source { get; set; }
    }
}
