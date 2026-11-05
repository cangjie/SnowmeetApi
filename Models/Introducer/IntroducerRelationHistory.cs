using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models;

[Table("introducer_relation_history")]
public sealed class IntroducerRelationHistory
{
    [Key] public int id { get; set; }
    public int introducer_id { get; set; }
    public int relation_id { get; set; }
    public string? real_name { get; set; }
    public DateTime create_date { get; set; } = DateTime.Now;
}
