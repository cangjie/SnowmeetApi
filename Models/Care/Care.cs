using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Drawing;

namespace SnowmeetApi.Models
{
    [Table("care")]
    public class Care
    {
        public static List<CoreDataModLog> GetUpdateDifferenceLog(Care oriCare, Care newCare, int? memberId, int? staffId, string scene)
        {
            if (oriCare.id != newCare.id)
            {
                return null;
            }
            List<CoreDataModLog> logs = new List<CoreDataModLog>();
            TimeSpan ts = DateTime.Now - DateTime.Parse("1970-1-1");
            if (oriCare.order_id != newCare.order_id)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "order_id", oriCare.id, oriCare.order_id, newCare.order_id, memberId, staffId, scene, ts.Ticks));
                oriCare.order_id = newCare.order_id;
            }

            if (oriCare.biz_type != newCare.biz_type)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "biz_type", oriCare.id, oriCare.biz_type, newCare.biz_type, memberId, staffId, scene, ts.Ticks));
                oriCare.biz_type = newCare.biz_type;
            }

            if (oriCare.equipment != newCare.equipment)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "equipment", oriCare.id, oriCare.equipment, newCare.equipment, memberId, staffId, scene, ts.Ticks));
                oriCare.equipment = newCare.equipment;
            }

            if (oriCare.brand != newCare.brand)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "brand", oriCare.id, oriCare.brand, newCare.brand, memberId, staffId, scene, ts.Ticks));
                oriCare.brand = newCare.brand;
            }
            if (oriCare.series != newCare.series)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "series", oriCare.id, oriCare.series, newCare.series, memberId, staffId, scene, ts.Ticks));
                oriCare.series = newCare.series;
            }

            if (oriCare.scale != newCare.scale)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "scale", oriCare.id, oriCare.scale, newCare.scale, memberId, staffId, scene, ts.Ticks));
                oriCare.scale = newCare.scale;
            }

            if (oriCare.year != newCare.year)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "year", oriCare.id, oriCare.year, newCare.year, memberId, staffId, scene, ts.Ticks));
                oriCare.year = newCare.year;
            }

            if (oriCare.images != newCare.images)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "images", oriCare.id, oriCare.images, newCare.images, memberId, staffId, scene, ts.Ticks));
                oriCare.images = newCare.images;
            }

            if (oriCare.urgent != newCare.urgent)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "urgent", oriCare.id, oriCare.urgent, newCare.urgent, memberId, staffId, scene, ts.Ticks));
                oriCare.urgent = newCare.urgent;
            }

            if (oriCare.boot_length != newCare.boot_length)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "boot_length", oriCare.id, oriCare.boot_length, newCare.boot_length, memberId, staffId, scene, ts.Ticks));
                oriCare.boot_length = newCare.boot_length;
            }

            if (oriCare.height != newCare.height)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "height", oriCare.id, oriCare.height, newCare.height, memberId, staffId, scene, ts.Ticks));
                oriCare.height = newCare.height;
            }

            if (oriCare.weight != newCare.weight)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "weight", oriCare.id, oriCare.weight, newCare.weight, memberId, staffId, scene, ts.Ticks));
                oriCare.weight = newCare.weight;
            }

            if (oriCare.gap != newCare.gap)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "gap", oriCare.id, oriCare.gap, newCare.gap, memberId, staffId, scene, ts.Ticks));
                oriCare.gap = newCare.gap;
            }

            if (oriCare.board_front != newCare.board_front)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "board_front", oriCare.id, oriCare.board_front, newCare.board_front, memberId, staffId, scene, ts.Ticks));
                oriCare.board_front = newCare.board_front;
            }

            if (oriCare.front_din != newCare.front_din)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "front_din", oriCare.id, oriCare.front_din, newCare.front_din, memberId, staffId, scene, ts.Ticks));
                oriCare.front_din = newCare.front_din;
            }

            if (oriCare.rear_din != newCare.rear_din)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "rear_din", oriCare.id, oriCare.rear_din, newCare.rear_din, memberId, staffId, scene, ts.Ticks));
                oriCare.rear_din = newCare.rear_din;
            }

            if (oriCare.left_angle != newCare.left_angle)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "left_angle", oriCare.id, oriCare.left_angle, newCare.left_angle, memberId, staffId, scene, ts.Ticks));
                oriCare.left_angle = newCare.left_angle;
            }

            if (oriCare.right_angle != newCare.right_angle)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "right_angle", oriCare.id, oriCare.right_angle, newCare.right_angle, memberId, staffId, scene, ts.Ticks));
                oriCare.right_angle = newCare.right_angle;
            }

            if (oriCare.serials != newCare.serials)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "serials", oriCare.id, oriCare.serials, newCare.serials, memberId, staffId, scene, ts.Ticks));
                oriCare.serials = newCare.serials;
            }

            if (oriCare.edge_degree != newCare.edge_degree)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "edge_degree", oriCare.id, oriCare.edge_degree, newCare.edge_degree, memberId, staffId, scene, ts.Ticks));
                oriCare.edge_degree = newCare.edge_degree;
            }

            if (oriCare.need_edge != newCare.need_edge)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "need_edge", oriCare.id, oriCare.need_edge, newCare.need_edge, memberId, staffId, scene, ts.Ticks));
                oriCare.need_edge = newCare.need_edge;
            }

            if (oriCare.need_vax != newCare.need_vax)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "need_vax", oriCare.id, oriCare.need_vax, newCare.need_vax, memberId, staffId, scene, ts.Ticks));
                oriCare.need_vax = newCare.need_vax;
            }

            if (oriCare.need_unvax != newCare.need_unvax)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "need_unvax", oriCare.id, oriCare.need_unvax, newCare.need_unvax, memberId, staffId, scene, ts.Ticks));
                oriCare.need_unvax = newCare.need_unvax;
            }

            if (oriCare.need_repair != newCare.need_repair)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "need_repair", oriCare.id, oriCare.need_repair, newCare.need_repair, memberId, staffId, scene, ts.Ticks));
                oriCare.need_repair = newCare.need_repair;
            }
            if (oriCare.repair_memo != newCare.repair_memo)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "repair_memo", oriCare.id, oriCare.repair_memo, newCare.repair_memo, memberId, staffId, scene, ts.Ticks));
                oriCare.repair_memo = newCare.repair_memo;
            }

            if (oriCare.repair_charge != newCare.repair_charge)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "repair_charge", oriCare.id, oriCare.repair_charge, newCare.repair_charge, memberId, staffId, scene, ts.Ticks));
                oriCare.repair_charge = newCare.repair_charge;
            }

            if (oriCare.common_charge != newCare.common_charge)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "common_charge", oriCare.id, oriCare.common_charge, newCare.common_charge, memberId, staffId, scene, ts.Ticks));
                oriCare.common_charge = newCare.common_charge;
            }

            if (oriCare.ticket_code != newCare.ticket_code)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "ticket_code", oriCare.id, oriCare.ticket_code, newCare.ticket_code, memberId, staffId, scene, ts.Ticks));
                oriCare.ticket_code = newCare.ticket_code;
            }

            if (oriCare.ticket_discount != newCare.ticket_discount)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "ticket_discount", oriCare.id, oriCare.ticket_discount, newCare.ticket_discount, memberId, staffId, scene, ts.Ticks));
                oriCare.ticket_discount = newCare.ticket_discount;
            }

            if (oriCare.discount != newCare.discount)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "discount", oriCare.id, oriCare.discount, newCare.discount, memberId, staffId, scene, ts.Ticks));
                oriCare.discount = newCare.discount;
            }

            if (oriCare.finish != newCare.finish)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "finish", oriCare.id, oriCare.finish, newCare.finish, memberId, staffId, scene, ts.Ticks));
                oriCare.finish = newCare.finish;
            }

            if (oriCare.member_pick_date != newCare.member_pick_date)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "member_pick_date", oriCare.id, oriCare.member_pick_date, newCare.member_pick_date, memberId, staffId, scene, ts.Ticks));
                oriCare.member_pick_date = newCare.member_pick_date;
            }

            if (oriCare.veri_code != newCare.veri_code)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "veri_code", oriCare.id, oriCare.veri_code, newCare.veri_code, memberId, staffId, scene, ts.Ticks));
                oriCare.veri_code = newCare.veri_code;
            }

            if (oriCare.veri_code_time != newCare.veri_code_time)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "veri_code_time", oriCare.id, oriCare.veri_code_time, newCare.veri_code_time, memberId, staffId, scene, ts.Ticks));
                oriCare.veri_code_time = newCare.veri_code_time;
            }
            if (oriCare.memo != newCare.memo)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "memo", oriCare.id, oriCare.memo, newCare.memo, memberId, staffId, scene, ts.Ticks));
                oriCare.memo = newCare.memo;
            }
            if (oriCare.task_flow_code != newCare.task_flow_code)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "task_flow_code", oriCare.id, oriCare.task_flow_code, newCare.task_flow_code, memberId, staffId, scene, ts.Ticks));
                oriCare.task_flow_code = newCare.task_flow_code;
            }
            if (oriCare.valid != newCare.valid)
            {
                logs.Add(Util.CreateCoreDataModLog("care", "valid", oriCare.id, oriCare.valid, newCare.valid, memberId, staffId, scene, ts.Ticks));
                oriCare.valid = newCare.valid;
            }
            return logs;
        }
        [Key]
        public int id { get; set; }
        public int? order_id { get; set; }
        public string? biz_type { get; set; }
        public string equipment { get; set; }
        public string brand { get; set; }
        public string? series { get; set; }
        public string scale { get; set; }
        public string? year { get; set; }
        public string? images { get; set; }
        public int urgent { get; set; }
        public string? boot_length { get; set; }
        public string? height { get; set; }
        public string? weight { get; set; }
        public string? gap { get; set; }
        public string? board_front { get; set; }
        public string? front_din { get; set; }
        public string? rear_din { get; set; }
        public string? left_angle { get; set; }
        public string? right_angle { get; set; }
        public string? serials { get; set; }
        public string? edge_degree { get; set; }
        public int need_edge { get; set; }
        public int need_vax { get; set; }
        public int need_unvax { get; set; }
        public int need_repair { get; set; }
        public string? repair_memo { get; set; }
        public double repair_charge { get; set; }
        public double common_charge { get; set; }
        public string? ticket_code { get; set; }
        public double ticket_discount { get; set; }
        public double discount { get; set; }
        public int finish { get; set; }
        public DateTime? member_pick_date { get; set; }
        public string? veri_code { get; set; }
        public DateTime? veri_code_time { get; set; }
        public string? memo { get; set; }
        public string? task_flow_code { get; set; }
        public int valid { get; set; }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; }
        [ForeignKey("order_id")]
        public Order? order {get; set;}
        [NotMapped]
        public string description
        {
            get
            {
                string desc = "";
                if (edge_degree != null && need_vax == 1)
                {
                    desc += "双项";
                }
                else if (edge_degree != null || need_vax == 1)
                {
                    desc += "单项";
                }
                if (repair_memo != null)
                {
                    desc += repair_memo.Trim();
                }
                if (desc.Trim().Equals(""))
                {
                    return "无";
                }
                else
                {
                    return desc.Trim();
                }
            }
        }
        
        public List<CareTask> tasks { get; set; } = new List<CareTask>();
        [NotMapped]
        public string? currentStep
        {
            get
            {
                if (tasks == null || tasks.Count == 0)
                {
                    return null;
                }
                return tasks[tasks.Count - 1].task_name.Trim();
            }
        }
        [NotMapped]
        public string? status
        {
            get 
            {
                if (currentStep == null)
                {
                    return "未开始";
                }
                if (currentStep.Trim().Equals("发板") || currentStep.Trim().Equals("强行索回"))
                {
                    return "已完成";
                }
                else
                {
                    return "进行中";
                }
            }
        }
    }
}