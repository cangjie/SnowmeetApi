using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
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
            DateTime orderDate = (DateTime)(order.pay_time == null? order.create_date : order.pay_time);
            string orderCode = shopCode.Trim() + "_LS_" + orderDate.ToString("yyMMdd") + "_" + order.id.ToString().PadLeft(5, '0');
            return orderCode;
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
                    final_charge = oldList[i].final_price,
                    memo = oldList[i].memo,
                    biz_date = oldList[i].pay_time == null ? oldList[i].create_date : (DateTime)oldList[i].pay_time,
                    staff_id = staff.id,
                    closed = 1,
                    create_date = oldList[i].create_date
                };
                for(int j = 0; j < oldList[i].mi7Orders.Count; j++)
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