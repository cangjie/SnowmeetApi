﻿using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Ticket
{
    [Table("ticket")]
    public class Ticket
    {
        [Key]
        public string code { get; set; }

        public string name { get; set; }
        public string memo { get; set; }

        public string open_id { get; set; }
    }
}
