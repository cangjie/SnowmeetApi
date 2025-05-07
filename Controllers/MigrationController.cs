using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Maintain;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class MigrationController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        public MigrationController(ApplicationDBContext context)
        {
            _db = context;
        }
        [NonAction]
        public async Task<string> CreateTextOrderCode(OrderOnline order)
        {
            string shopCode = "";
            List<Shop> shopList = await _db.shop.Where(s => s.name.Trim().Equals(order.shop.Trim())).AsNoTracking().ToListAsync();
            if (shopList.Count <= 0)
            {
                if (order.shop.Trim().Equals("万龙"))
                {
                    shopCode = "WL";
                }
                //return "";
            }
            else
            {
                shopCode = shopList[0].code;
            }
            if (shopCode == null || shopCode.Trim().Length <= 0)
            {
                return "";
            }
            DateTime orderDate = (DateTime)(order.pay_time == null ? order.create_date : order.pay_time);
            string bizCode = "QT";
            switch (order.type.Trim())
            {
                case "店销":
                case "店销现货":
                case "零售":
                    bizCode = "LS";
                    break;
                case "服务":
                    bizCode = "YH";
                    break;
                case "押金":
                    bizCode = "ZL";
                    break;
                case "雪票":
                    bizCode = "XP";
                    break;
                default:
                    break;
            }
            string orderCode = shopCode.Trim() + "_" + bizCode + "_" + orderDate.ToString("yyMMdd") + "_" + order.id.ToString().PadLeft(5, '0');
            return orderCode;
        }
        [HttpGet]
        public async Task MigrateCareEntrain()
        {
            StaffController _staffHelper = new StaffController(_db);
            List<Care> careList = await _db.care.Where(c => c.order_id == null && c.valid == 1).ToListAsync();
            for (int i = 0; i < careList.Count; i++)
            {
                Care care = careList[i];
                if (care.order_id == null)
                {
                    MaintainLive live = await _db.MaintainLives.FindAsync(careList[i].id);
                    int batchId = live.batch_id;
                    List<MaintainLive> mL = await _db.MaintainLives.Where(m => m.batch_id == batchId).AsNoTracking().ToListAsync();
                    Staff? staff = await _staffHelper.GetStaffBySocialNum(live.service_open_id, "wechat_mini_openid", live.create_date);
                    List<MemberSocialAccount> msaList = await _db.memberSocialAccount
                        .Where(m => m.type.Trim().Equals("wechat_mini_openid") && m.num.Trim().Equals(live.open_id))
                        .OrderByDescending(m => m.id).AsNoTracking().ToListAsync();
                    int? memberId = null;
                    if (msaList.Count > 0)
                    {
                        memberId = msaList[0].member_id;
                    }
                    int orderId = await _db.order.MaxAsync(o => o.id);
                    orderId++;
                    string shopCode = live.shop.IndexOf("万龙") >= 0 ? "WL" : "NS";
                    string orderCode = shopCode + "_YH_" + live.create_date.ToString("yyMMdd") + "_" + orderId.ToString().PadLeft(5, '0');
                    string payMemo = "普通";
                    if (live.pay_memo.IndexOf("招待")>=0)
                    {
                        payMemo = "招待";
                    }
                    if (live.pay_memo.IndexOf("质保")>=0)
                    {
                        payMemo = "质保";
                    }
                    if (live.pay_memo.IndexOf("次卡")>=0)
                    {
                        payMemo = "次卡";
                    }
                    SnowmeetApi.Models.Order order = new SnowmeetApi.Models.Order()
                    {
                        id = orderId,
                        code = orderCode,
                        type = "养护",
                        sub_type = "雪季",
                        shop = live.shop,
                        is_package = 0,
                        member_id = memberId,
                        name = live.confirmed_name.Replace("先生", "").Replace("女士", ""),
                        cell = live.confirmed_cell.Length > 11 ? live.confirmed_cell.Substring(0, 11) : live.confirmed_cell.Trim(),
                        gender = live.confirmed_name.EndsWith("先生") ? "男" : (live.confirmed_name.EndsWith("女士") ? "女" : ""),
                        total_amount = 0,
                        memo = live.confirmed_memo,
                        biz_date = live.create_date,
                        staff_id = staff == null ? null : staff.id,
                        closed = 1,
                        valid = 1,
                        pay_option = payMemo,
                        create_date = live.create_date
                    };
                    await _db.order.AddAsync(order);
                    for(int j = 0; j < mL.Count; j++)
                    {
                        List<Care> subCL = careList.Where(c => c.id == mL[j].id).ToList();
                        for(int k = 0; k < subCL.Count; k++)
                        {
                            subCL[k].order_id = orderId;
                            _db.care.Entry(subCL[k]).State = EntityState.Modified;
                        }
                    }
                    await _db.SaveChangesAsync();
                }
            }
        }
        [HttpGet]
        public async Task MigrateCare()
        {
            StaffController _staffHelper = new StaffController(_db);
            var orderIdList = await _db.care.Where(c => c.order_id != null)
                .Where(c => c.order_id == 36434 || c.order_id == 36996 || c.order_id == 39595 || c.order_id == 34914 || c.order_id == 34922)
                .Select(c => c.order_id).Distinct().ToListAsync();
            foreach (int orderId in orderIdList)
            {
                OrderOnline oo = await _db.OrderOnlines.FindAsync(orderId);
                string orderCode = await CreateTextOrderCode(oo);
                if (orderCode == "")
                {
                    Console.WriteLine(oo.id.ToString() + " order code is blank");
                    continue;
                }
                Staff? staff = await _staffHelper.GetStaffBySocialNum(oo.staff_open_id, "wechat_mini_openid", oo.create_date);
                if (staff == null)
                {
                    Console.WriteLine(oo.id.ToString() + " staff is null");
                }
                List<MemberSocialAccount> msaList = await _db.memberSocialAccount
                    .Where(m => m.type.Trim().Equals("wechat_mini_openid") && m.num.Trim().Equals(oo.open_id))
                    .OrderByDescending(m => m.id).AsNoTracking().ToListAsync();
                int? memberId = null;
                if (msaList.Count > 0)
                {
                    memberId = msaList[0].member_id;
                }
                
                SnowmeetApi.Models.Order order = new SnowmeetApi.Models.Order()
                {
                    id = oo.id,
                    code = orderCode,
                    type = "养护",
                    sub_type = "雪季",
                    shop = oo.shop,
                    is_package = 0,
                    member_id = memberId,
                    name = oo.name.Replace("先生", "").Replace("女士", ""),
                    cell = oo.cell_number.Length > 11 ? oo.cell_number.Substring(0, 11) : oo.cell_number.Trim(),
                    gender = oo.name.EndsWith("先生") ? "男" : (oo.name.EndsWith("女士") ? "女" : ""),
                    total_amount = oo.final_price,
                    memo = oo.memo,
                    biz_date = oo.pay_time == null ? oo.create_date : (DateTime)oo.pay_time,
                    staff_id = staff == null ? null : staff.id,
                    closed = 1,
                    valid = 1,
                    create_date = oo.create_date
                };
                await _db.order.AddAsync(order);
                await _db.SaveChangesAsync();
            }
        }
        [HttpGet]
        public async Task MigrateCareTask()
        {
            StaffController _staffHelper = new StaffController(_db);
            List<SnowmeetApi.Models.Maintain.MaintainLog> l = await _db.MaintainLog
                .Include(m => m.msa).AsNoTracking().ToListAsync();
            for(int i = 0; i < l.Count; i++)
            {
                MaintainLog log = l[i];
                CareTask oriTask = await _db.careTask.FindAsync(log.id);
                if (oriTask != null)
                {
                    continue;
                }
                int? staffId = null;
                int? memberId = null;
                int? terminateStaffId = null;
                if (log.staff_open_id != null && !log.staff_open_id.Trim().Equals(""))
                {
                    Staff staff = await _staffHelper.GetStaffBySocialNum(log.staff_open_id, "wechat_mini_openid");
                    if (staff != null)
                    {
                        staffId = staff.id;
                    }
                }
                if (log.msa != null)
                {
                    memberId = log.msa.member_id;
                }
                if (log.stop_open_id == null && !log.stop_open_id.Trim().Equals(""))
                {
                    Staff staff = await _staffHelper.GetStaffBySocialNum(log.stop_open_id, "wechat_mini_openid");
                    if (staff != null)
                    {
                        staffId = staff.id;
                    }
                    terminateStaffId = staff.id;
                }
                CareTask task = new CareTask()
                {
                    id = log.id,
                    care_id = log.task_id,
                    task_name = log.step_name,
                    memo = log.memo,
                    start_time = log.start_time,
                    end_time = log.end_time,
                    status = log.status,
                    staff_id = staffId,
                    terminate_staff_id = terminateStaffId,
                    member_id = memberId
                };
                await _db.careTask.AddAsync(task);
                await _db.SaveChangesAsync();
            }
        }
        [HttpGet]
        public async Task MigrateRetail()
        {
            StaffController _staffHelper = new StaffController(_db);
            var oldList = await _db.OrderOnlines.Include(o => o.mi7Orders)
                .Where(o => o.type.IndexOf("销") >= 0 && o.pay_state == 1 && o.mi7Orders.Count > 0).AsNoTracking().ToListAsync();

            for (int i = 0; i < oldList.Count; i++)
            {
                string staffOpenid = oldList[i].staff_open_id;

                if (staffOpenid == null || staffOpenid.Trim().Length <= 0)
                {
                    Console.WriteLine(oldList[i].id.ToString() + " staff is null");
                    continue;
                }

                Staff? staff = await _staffHelper.GetStaffBySocialNum(staffOpenid, "wechat_mini_openid", oldList[i].create_date);
                if (staff == null)
                {
                    List<MemberSocialAccount> msaList = await _db.memberSocialAccount
                        .Where(m => (m.num.Trim().Equals(staffOpenid.Trim()) && m.valid == 1 && m.type == "wechat_mini_openid"))
                        .AsNoTracking().ToListAsync();

                    if (msaList.Count <= 0)
                    {
                        Console.WriteLine(oldList[i].id.ToString() + " staff is null");
                        continue;

                    }
                    int memberId = msaList[0].member_id;
                    Member? member = await _db.member.Include(m => m.memberSocialAccounts)
                        .Where(m => m.id == memberId)
                        .AsNoTracking().FirstOrDefaultAsync();
                    if (member == null)
                    {
                        Console.WriteLine(oldList[i].id.ToString() + " staff is null");
                        continue;
                    }
                    staff = await _staffHelper.CreateStaff(member.wechatMiniOpenId, member.cell, member.real_name, member.gender, oldList[i].create_date);
                    if (staff == null)
                    {
                        //Console.WriteLine(oldList[i].id.ToString() + " staff is null");
                        continue;
                    }
                }
                string orderCode = await CreateTextOrderCode(oldList[i]);
                if (orderCode == null || orderCode.Trim().Length <= 0)
                {
                    Console.WriteLine(oldList[i].id.ToString() + " order code is null");
                    continue;
                }
                List<MemberSocialAccount> memberMsaList = await _db.memberSocialAccount
                        .Where(m => (m.num.Trim().Equals(oldList[i].open_id) && m.valid == 1 && m.type == "wechat_mini_openid"))
                        .AsNoTracking().ToListAsync();
                int? orderMemberId = null;
                string? name = null;
                string? cell = null;
                string? gender = null;
                if (memberMsaList.Count > 0)
                {
                    List<Member> orderMemberList = await _db.member.Where(m => m.id == memberMsaList[0].member_id)
                        .Include(m => m.memberSocialAccounts)
                        .AsNoTracking().ToListAsync();
                    orderMemberId = memberMsaList[0].member_id;
                    if (orderMemberList.Count > 0)
                    {
                        name = orderMemberList[0].real_name;
                        cell = orderMemberList[0].cell;
                        gender = orderMemberList[0].gender;
                    }

                }
                SnowmeetApi.Models.Order order = new SnowmeetApi.Models.Order()
                {
                    id = 0,
                    code = orderCode,
                    type = "零售",
                    sub_type = "现货",
                    shop = oldList[i].shop,
                    is_package = 0,
                    member_id = orderMemberId,
                    name = name,
                    cell = cell,
                    gender = gender,
                    total_amount = oldList[i].order_price,
                    memo = oldList[i].memo,
                    biz_date = oldList[i].pay_time == null ? oldList[i].create_date : (DateTime)oldList[i].pay_time,
                    staff_id = staff.id,
                    closed = 1,
                    create_date = oldList[i].create_date
                };
                for (int j = 0; j < oldList[i].mi7Orders.Count; j++)
                {
                    string? mi7Code = null;
                    Mi7Order mi7 = oldList[i].mi7Orders[j];
                    if (mi7.mi7_order_id.StartsWith("XSD"))
                    {
                        mi7Code = oldList[i].mi7Orders[j].mi7_order_id;
                    }
                    Retail retail = new Retail()
                    {
                        id = 0,
                        order_id = order.id,
                        mi7_code = mi7Code,
                        sale_price = mi7.sale_price,
                        deal_price = mi7.real_charge,
                        order_type = mi7.order_type,
                        valid = 1,
                        update_date = null,
                        create_date = mi7.create_date
                    };
                    if (!mi7.order_type.Trim().Equals("普通"))
                    {
                        order.pay_option = mi7.order_type.Trim();
                        if (mi7.order_type.Trim().Equals("招待"))
                        {
                            if (mi7.enterain_member_id != null)
                            {
                                order.member_id = (int)mi7.enterain_member_id;
                            }
                            if (mi7.enterain_cell != null)
                            {
                                order.cell = mi7.enterain_cell;
                            }
                            if (mi7.enterain_real_name != null)
                            {
                                order.name = mi7.enterain_real_name;
                            }
                            if (mi7.enterain_gender != null)
                            {
                                order.gender = mi7.enterain_gender;
                            }
                        }
                    }
                    order.retails.Add(retail);
                }
                await _db.order.AddAsync(order);
                await _db.SaveChangesAsync();
            }

        }
    }
}