using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aop.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Maintain;
using SnowmeetApi.Models.Rent;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class MigrationController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;
        public MigrationController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
        }
        public class RentalPackage
        {
            public List<RentOrderDetail> details = new List<RentOrderDetail>();
            public string packageCode = "";
            public RentOrderDetail mainItem;
        }
        public class RentalList
        {
            public List<RentalPackage> packages = new List<RentalPackage>();
            public List<RentOrderDetail> details = new List<RentOrderDetail>();
        }
        [HttpGet]
        public async Task UpdatePackageRental()
        {
            RentController _rHelper = new RentController(_db, _config, _http);
            List<int> list = await _db.Database.SqlQueryRaw<int>("select distinct rent_list_id from rent_list_detail where [id] in ( select distinct rent_item_id from rental_detail where amount = 0) order by rent_list_id desc")
                .ToListAsync();
            for (int i = 0; i < list.Count; i++)
            {
                RentOrder rentOrder = (RentOrder)((OkObjectResult)(await _rHelper.GetRentOrder(list[i], "", false)).Result).Value;
                Console.WriteLine(i.ToString() + "\t" + rentOrder.id.ToString());
                foreach (SnowmeetApi.Models.Rent.RentalDetail dtl in rentOrder.rentalDetails)
                {
                    if (dtl.rental != 0)
                    {
                        int itemId = dtl.item.id;
                        DateTime rentalDate = dtl.date;
                        List<SnowmeetApi.Models.RentItem> itemList = await _db.rentItem
                            .Where(r => r.id == itemId).AsNoTracking().ToListAsync();
                        int? rentalId = null;
                        foreach (SnowmeetApi.Models.RentItem item in itemList)
                        {
                            rentalId = item.rental_id;
                            break;
                        }
                        if (rentalId == null)
                        {
                            continue;
                        }
                        List<SnowmeetApi.Models.RentalDetail> dtlList = await _db.rentalDetail
                            .Where(r => r.rental_id == rentalId && r.rental_date.Date == rentalDate.Date)
                            .ToListAsync();
                        for (int j = 0; j < dtlList.Count; j++)
                        {
                            SnowmeetApi.Models.RentalDetail d = dtlList[j];
                            d.amount = dtl.rental;
                            d.update_date = DateTime.Now;
                            _db.rentalDetail.Entry(d).State = EntityState.Modified;
                        }

                    }
                }
                await _db.SaveChangesAsync();
            }
        }
        [HttpGet]
        public async Task MigrateRentItemLog()
        {
            StaffController _staffH = new StaffController(_db);
            List<RentOrderDetailLog> l = await _db.rentOrderDetailLog.ToListAsync();
            Console.WriteLine("total count" + l.Count.ToString());
            for (int i = 0; i < l.Count; i++)
            {
                RentOrderDetailLog rLog = l[i];
                Staff staff = await _staffH.GetStaffBySocialNum(rLog.staff_open_id, "wechat_mini_openid", rLog.create_date);
                CoreDataModLog log = new CoreDataModLog()
                {
                    id = 0,
                    table_name = "rent_item",
                    key_value = rLog.detail_id,
                    scene = "租赁物状态改变",
                    staff_id = staff == null ? null : staff.id,
                    prev_value = rLog.prev_value,
                    current_value = rLog.status,
                    is_manual = 1,
                    manual_memo = "界面造作"
                };
                Console.WriteLine(i.ToString() + "\t" + rLog.id.ToString());
                await _db.coreDataModLog.AddAsync(log);
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (Exception err)
                {
                    Console.WriteLine(err.ToString());
                }
            }
        }
        [NonAction]
        public RentalList OrgniazeDetails(List<RentOrderDetail> details)
        {
            ////////
            /// 明天，考虑日志的导入和多笔支付的导入，超时费，损坏赔偿，以及押金
            /// ////////////////
            RentalList ret = new RentalList();
            var pList = from p in details group p by p.package_code into g select new { g.Key, Count = g.Count() };
            foreach (var p in pList)
            {
                if (p.Key != null)
                {
                    string pCode = p.Key;
                    RentalPackage package = new RentalPackage();
                    package.packageCode = pCode;
                    try
                    {
                        var subList = details.Where(d => d.package_code != null && d.package_code.Trim().Equals(pCode.Trim())).ToList();
                        if (subList != null)
                        {
                            package.details = subList;
                        }
                        //package.details = subList != null ? subList : package.details;
                        foreach (RentOrderDetail d in package.details)
                        {
                            if (d.rent_item_class.IndexOf("板") >= 0)
                            {
                                package.mainItem = d;
                            }
                        }
                        if (package.mainItem==null)
                        {
                            for(int i = 0; i < details.Count; i++)
                            {
                                if (details[i].unit_rental > 0)
                                {
                                    package.mainItem = details[i];
                                    break;
                                }
                            }
                        }
                        if (package.mainItem == null)
                        {
                            package.mainItem = details[0];
                        }
                        ret.packages.Add(package);
                    }
                    catch (Exception err)
                    {
                        Console.WriteLine(err.ToString());
                    }
                }
            }
            ret.details = details.Where(d => d.package_code == null).ToList();
           
            return ret;
        }
        [HttpGet]
        public async Task MigrateRent()
        {

            RentController _rHelper = new RentController(_db, _config, _http);
            StaffController _staffHelper = new StaffController(_db);

            /*
            RentOrder commonOrder = (RentOrder)((OkObjectResult)(await _rHelper.GetRentOrder(14384, "", false)).Result).Value;
            RentOrder entOrder = (RentOrder)((OkObjectResult)(await _rHelper.GetRentOrder(14382, "", false)).Result).Value;
            RentOrder closeOrder = (RentOrder)((OkObjectResult)(await _rHelper.GetRentOrder(13370, "", false)).Result).Value;
            */


            List<RentOrder> rentIdList = await _db.RentOrder
            //.Where(r => r.id == 4562)
            .AsNoTracking().OrderByDescending(r => r.id).ToListAsync();

            //string sessionKey = "KB2ziprfR0VIPCtXsYWO6w==";
            Console.WriteLine(rentIdList.Count.ToString());
            for (int i = 0; i < rentIdList.Count; i++)
            {

                RentOrder rentOrder = (RentOrder)((OkObjectResult)(await _rHelper.GetRentOrder(rentIdList[i].id, "", false)).Result).Value;

                if (rentOrder.order != null && rentOrder.order.pay_state == 0)
                {
                    continue;
                }

                SnowmeetApi.Models.Order order = new SnowmeetApi.Models.Order();
                if (rentOrder.order_id != null && rentOrder.order_id != 0)
                {
                    order.id = (int)rentOrder.order_id;
                }
                else
                {
                    order.id = (await _db.order.MaxAsync(o => o.id))+1;
                }
                order.biz_date = rentOrder.create_date;
                order.code = await CreateRentTextOrderCode(rentOrder);
                order.shop = rentOrder.shop.Trim();
                Console.WriteLine(i.ToString() + "\t: " + order.code.Trim());
                order.type = "租赁";
                if (rentOrder.order == null)
                {
                    order.pay_option = "招待";
                }
                else
                {
                    order.pay_option = "普通";
                }
                Staff staff = await _staffHelper.GetStaffBySocialNum(rentOrder.staff_open_id.Trim(), "wechat_mini_openid");
                if (staff != null)
                {
                    order.staff_id = staff.id;
                }
                order.memo = rentOrder.memo.Trim();
                List<MemberSocialAccount> msaL = await _db.memberSocialAccount.Where(m => m.num.Trim().Equals(rentOrder.open_id.Trim()) && m.valid == 1)
                    .OrderByDescending(m => m.id).AsNoTracking().ToListAsync();
                if (msaL.Count > 0)
                {
                    order.member_id = msaL[0].member_id;
                }
                string memo = (rentOrder.order == null ? "" : rentOrder.order.memo) + " " + rentOrder.memo.Trim();
                RentalList rentalList = OrgniazeDetails(rentOrder.details);
                List<Rental> rentalObjList = new List<Rental>();
                for (int j = 0; j < rentalList.packages.Count; j++)
                {

                    Rental r = new Rental()
                    {
                        order_id = order.id,
                        package_id = 0,
                        name = "【" + rentalList.packages[j].mainItem.rent_item_class + "】"
                            + rentalList.packages[j].mainItem.rent_item_name
                            + "(" + rentalList.packages[j].mainItem.rent_item_code + ")",

                        valid = 1,
                        settled = 1,
                        hide = rentOrder.hide,
                        memo = rentOrder.memo,
                        start_date = rentalList.packages[j].mainItem.start_date,
                        end_date = rentalList.packages[j].mainItem.real_end_date
                    };
                    double totalGuaranty = 0;
                    for (int k = 0; k < rentalList.packages[j].details.Count; k++)
                    {
                        RentOrderDetail dtl = rentalList.packages[j].details[k];
                        totalGuaranty += dtl.deposit;
                        Models.RentItem item = new Models.RentItem()
                        {
                            id = dtl.id,
                            rental_id = r.id,
                            pick_time = dtl.pick_date == null ? dtl.start_date : dtl.pick_date,
                            return_time = dtl.return_date == null ? dtl.real_end_date : dtl.return_date,
                            name = rentalList.packages[j].details[k].rent_item_name,
                            code = rentalList.packages[j].details[k].rent_item_code,
                            memo = rentalList.packages[j].details[k].memo,
                            create_date = rentalList.packages[j].details[k].create_date

                        };
                        r.rentItems.Add(item);
                        if (dtl.overtime_charge > 0)
                        {
                            SnowmeetApi.Models.RentalDetail rentalDetail = new SnowmeetApi.Models.RentalDetail()
                            {
                                id = 0,
                                rent_item_id = dtl.id,
                                amount = dtl.overtime_charge,
                                rental_date = dtl.real_end_date == null ? dtl.create_date.Date : (DateTime)dtl.real_end_date,
                                charge_type = "超时费",
                                rental_id = r.id,
                                valid = 1
                            };
                            r.details.Add(rentalDetail);
                        }
                        if (dtl.reparation > 0)
                        {
                            SnowmeetApi.Models.RentalDetail rentalDetail = new SnowmeetApi.Models.RentalDetail()
                            {
                                id = 0,
                                rent_item_id = dtl.id,
                                amount = dtl.reparation,
                                rental_date = dtl.real_end_date == null ? dtl.create_date.Date : (DateTime)dtl.real_end_date,
                                charge_type = "赔偿费",
                                rental_id = r.id,
                                valid = 1
                            };
                            r.details.Add(rentalDetail);
                        }
                    }
                    //r.guaranty_amount = totalGuaranty;
                    Guaranty guaranty = new Guaranty()
                    {
                        id = 0,
                        biz_type = "租赁",
                        biz_id = r.id,
                        sub_biz_type = "套餐押金",
                        sub_biz_id = rentalList.packages[j].mainItem.id,
                        amount = totalGuaranty,
                        valid = 1,
                        relieve = 1,
                        create_date = rentOrder.create_date
                    };
                    r.guaranties.Add(guaranty);

                    List<SnowmeetApi.Models.Rent.RentalDetail> oriRentalDetails = rentOrder.rentalDetails
                        .Where(rd => rd.item.id == rentalList.packages[j].mainItem.id).ToList();
                    for (int k = 0; k < oriRentalDetails.Count; k++)
                    {
                        double rentalAmount = oriRentalDetails[k].rental;
                        if (k == oriRentalDetails.Count - 1)
                        {
                            rentalAmount += (oriRentalDetails[k].item.rental_discount + oriRentalDetails[k].item.rental_ticket_discount);
                            if (oriRentalDetails[k].item.rental_discount > 0)
                            {
                                Discount discount = new Discount()
                                {
                                    order_id = order.id,
                                    amount = oriRentalDetails[k].item.rental_discount,
                                    biz_type = "租赁",
                                    sub_biz_type = "租赁项",
                                    sub_biz_id = oriRentalDetails[k].item.id,
                                    valid = 1
                                };
                                order.discounts.Add(discount);
                            }
                            if (oriRentalDetails[k].item.rental_ticket_discount > 0)
                            {
                                Discount discount = new Discount()
                                {
                                    order_id = order.id,
                                    amount = oriRentalDetails[k].item.rental_discount,
                                    biz_type = "租赁",
                                    sub_biz_type = "租赁项",
                                    sub_biz_id = oriRentalDetails[k].item.id,
                                    ticket_code = "",
                                    valid = 1
                                };
                                order.discounts.Add(discount);
                            }

                        }
                        SnowmeetApi.Models.RentalDetail dtl = new Models.RentalDetail()
                        {
                            id = 0,
                            rental_id = r.id,
                            rent_item_id = oriRentalDetails[k].item.id,
                            rental_date = oriRentalDetails[k].date,
                            charge_type = "租金",
                            amount = rentalAmount,
                            valid = 1,
                            memo = ""
                        };
                        r.details.Add(dtl);
                    }
                    order.rentals.Add(r);

                }
                for (int j = 0; j < rentalList.details.Count; j++)
                {
                    Rental r = new Rental()
                    {
                        order_id = order.id,
                        package_id = 0,
                        name = "【" + rentalList.details[j].rent_item_class + "】"
                            + rentalList.details[j].rent_item_name
                            + "(" + rentalList.details[j].rent_item_code + ")",

                        valid = 1,
                        settled = 1,
                        hide = rentOrder.hide,
                        memo = rentOrder.memo,
                        start_date = rentalList.details[j].start_date,
                        end_date = rentalList.details[j].real_end_date
                    };
                    if (rentalList.details[j].overtime_charge > 0)
                    {
                        SnowmeetApi.Models.RentalDetail rentalDetail = new SnowmeetApi.Models.RentalDetail()
                        {
                            id = 0,
                            rent_item_id = rentalList.details[j].id,
                            amount = rentalList.details[j].overtime_charge,
                            rental_date = rentalList.details[j].real_end_date == null ? rentalList.details[j].create_date.Date : (DateTime)rentalList.details[j].real_end_date,
                            charge_type = "超时费",
                            rental_id = r.id,
                            valid = 1
                        };
                        r.details.Add(rentalDetail);
                    }
                    if (rentalList.details[j].reparation > 0)
                    {
                        SnowmeetApi.Models.RentalDetail rentalDetail = new SnowmeetApi.Models.RentalDetail()
                        {
                            id = 0,
                            rent_item_id = rentalList.details[j].id,
                            amount = rentalList.details[j].reparation,
                            rental_date = rentalList.details[j].real_end_date == null ? rentalList.details[j].create_date.Date : (DateTime)rentalList.details[j].real_end_date,
                            charge_type = "赔偿金",
                            rental_id = r.id,
                            valid = 1
                        };
                        r.details.Add(rentalDetail);
                    }
                    Guaranty guaranty = new Guaranty()
                    {
                        id = 0,
                        biz_type = "租赁",
                        biz_id = r.id,
                        sub_biz_type = "单品押金",
                        sub_biz_id = rentalList.details[j].id,
                        amount = rentalList.details[j].deposit,
                        valid = 1,
                        relieve = 1,
                        create_date = rentOrder.create_date
                    };
                    r.guaranties.Add(guaranty);
                    Models.RentItem item = new Models.RentItem()
                    {
                        id = rentalList.details[j].id,
                        rental_id = r.id,
                        pick_time = rentalList.details[j].pick_date == null ? rentalList.details[j].start_date : rentalList.details[j].pick_date,
                        return_time = rentalList.details[j].return_date == null ? rentalList.details[j].real_end_date : rentalList.details[j].return_date,
                        name = rentalList.details[j].rent_item_name,
                        code = rentalList.details[j].rent_item_code,
                        memo = rentalList.details[j].memo,
                        create_date = rentalList.details[j].create_date,

                    };
                    r.rentItems.Add(item);
                    List<SnowmeetApi.Models.Rent.RentalDetail> oriRentalDetails = rentOrder.rentalDetails
                        .Where(rd => rd.item.id == rentalList.details[j].id).ToList();
                    for (int k = 0; k < oriRentalDetails.Count; k++)
                    {
                        double rentalAmount = oriRentalDetails[k].rental;
                        if (k == oriRentalDetails.Count - 1)
                        {
                            rentalAmount += (oriRentalDetails[k].item.rental_discount + oriRentalDetails[k].item.rental_ticket_discount);
                            if (oriRentalDetails[k].item.rental_discount > 0)
                            {
                                Discount discount = new Discount()
                                {
                                    order_id = order.id,
                                    amount = oriRentalDetails[k].item.rental_discount,
                                    biz_type = "租赁",
                                    sub_biz_type = "租赁项",
                                    sub_biz_id = oriRentalDetails[k].item.id,
                                    valid = 1
                                };
                                order.discounts.Add(discount);
                            }
                            if (oriRentalDetails[k].item.rental_ticket_discount > 0)
                            {
                                Discount discount = new Discount()
                                {
                                    order_id = order.id,
                                    amount = oriRentalDetails[k].item.rental_discount,
                                    biz_type = "租赁",
                                    sub_biz_type = "租赁项",
                                    sub_biz_id = oriRentalDetails[k].item.id,
                                    ticket_code = "",
                                    valid = 1
                                };
                                order.discounts.Add(discount);
                            }

                        }
                        SnowmeetApi.Models.RentalDetail dtl = new Models.RentalDetail()
                        {
                            id = 0,
                            rental_id = r.id,
                            rent_item_id = oriRentalDetails[k].item.id,
                            rental_date = oriRentalDetails[k].date,
                            charge_type = "租金",
                            amount = rentalAmount,
                            valid = 1,
                            memo = ""
                        };
                        r.details.Add(dtl);
                    }
                    order.rentals.Add(r);
                }

                if (rentOrder.deposit_reduce > 0)
                {
                    Guaranty gOrderDiscount = new Guaranty()
                    {
                        id = 0,
                        order_id = order.id,
                        biz_type = "租赁",
                        memo = "租赁整单押金减免",
                        amount = -1 * rentOrder.deposit_reduce,
                        valid = 1,
                        relieve = 1,
                        create_date = rentOrder.create_date
                    };
                    order.guarantys.Add(gOrderDiscount);
                }
                if (rentOrder.deposit_reduce_ticket > 0)
                {
                    Guaranty gOrderDiscount = new Guaranty()
                    {
                        id = 0,
                        order_id = order.id,
                        biz_type = "租赁",
                        memo = "租赁整单押金优惠券减免",
                        amount = -1 * rentOrder.deposit_reduce,
                        valid = 1,
                        relieve = 1,
                        create_date = rentOrder.create_date
                    };
                    order.guarantys.Add(gOrderDiscount);
                }
                for (int j = 0; j < rentOrder.additionalPayments.Count; j++)
                {
                    RentAdditionalPayment addPay = rentOrder.additionalPayments[j];
                    if (addPay.is_paid != 1)
                    {
                        continue;
                    }
                    if (addPay.reason.Trim().Equals("追加押金"))
                    {
                        Guaranty g = new Guaranty()
                        {
                            id = 0,
                            order_id = order.id,
                            biz_type = "租赁",
                            memo = "追加押金",
                            amount = addPay.amount,
                            valid = 1,
                            relieve = 1,
                            create_date = rentOrder.create_date
                        };
                        order.guarantys.Add(g);
                    }
                    if (addPay.order_id == null)
                    {
                        continue;
                    }
                    List<OrderPayment> payments = await _db.orderPayment
                        .Where(p => p.status.Equals("支付成功") && p.order_id == addPay.order_id )
                        .ToListAsync();
                    for(int k = 0; k < payments.Count; k++)
                    {
                        OrderPayment payment = payments[k];
                        CoreDataModLog log = new CoreDataModLog()
                        {
                            id = 0,
                            table_name = "order_payment",
                            field_name = "order_id",
                            key_value = payment.id,
                            scene = "导入老租赁数据，追加支付并入主订单",
                            prev_value = payment.order_id.ToString(),
                            current_value = order.id.ToString()
                        };
                        await _db.coreDataModLog.AddAsync(log);
                        payment.order_id = order.id;
                        _db.orderPayment.Entry(payment).State = EntityState.Modified;

                    }
                    //await _db.SaveChangesAsync();

                }
                await _db.order.AddAsync(order);
                List<SnowmeetApi.Models.Order> dupOrderList = await _db.order
                    .Where(o => o.code.Trim().Equals(order.code.Trim()))
                    .AsNoTracking().ToListAsync();
                if (dupOrderList.Count == 0)
                {
                    try
                    {
                        await _db.SaveChangesAsync();
                    }
                    catch
                    {
                        System.Threading.Thread.Sleep(10000);
                        try
                        {
                            await _db.SaveChangesAsync();
                        }
                        catch
                        {
                            System.Threading.Thread.Sleep(10000);
                            await _db.SaveChangesAsync();
                        }
                    }
                }
            }
        }
        [NonAction]
        public async Task<string> CreateRentTextOrderCode(RentOrder rentOrder)
        {
            if (rentOrder.order != null)
            {
                return await CreateTextOrderCode(rentOrder.order);
            }
            string shopCode = "WL";
            List<SnowmeetApi.Models.Shop> shopList = await _db.shop.Where(s => s.name.Trim().Equals(rentOrder.shop.Trim())).AsNoTracking().ToListAsync();
            if (shopList.Count <= 0)
            {
                if (rentOrder.shop.Trim().Equals("万龙"))
                {
                    shopCode = "WL";
                }
                else
                {
                    shopCode = "QJ";
                }
                //return "";
            }
            else
            {
                shopCode = shopList[0].code;
            }
            string orderCode = shopCode + "_ZL_" + rentOrder.create_date.ToString("yyMMdd") + "_";
            List<SnowmeetApi.Models.Order> ol = await _db.order.Where(o => o.code.StartsWith(orderCode)).AsNoTracking().ToListAsync();
            orderCode = orderCode + (ol.Count + 1).ToString().PadLeft(5, '0');
            return orderCode;

        }
        [NonAction]
        public async Task<string> CreateTextOrderCode(OrderOnline order)
        {
            string shopCode = "";
            List<SnowmeetApi.Models.Shop> shopList = await _db.shop.Where(s => s.name.Trim().Equals(order.shop.Trim())).AsNoTracking().ToListAsync();
            if (shopList.Count <= 0)
            {
                if (order.shop.Trim().Equals("万龙"))
                {
                    shopCode = "WL";
                }
                else
                {
                    shopCode = "QJ";
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
                    if (live.pay_memo.IndexOf("招待") >= 0)
                    {
                        payMemo = "招待";
                    }
                    if (live.pay_memo.IndexOf("质保") >= 0)
                    {
                        payMemo = "质保";
                    }
                    if (live.pay_memo.IndexOf("次卡") >= 0)
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
                    for (int j = 0; j < mL.Count; j++)
                    {
                        List<Care> subCL = careList.Where(c => c.id == mL[j].id).ToList();
                        for (int k = 0; k < subCL.Count; k++)
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
            for (int i = 0; i < l.Count; i++)
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
                    SnowmeetApi.Models.Member? member = await _db.member.Include(m => m.memberSocialAccounts)
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
                    List<SnowmeetApi.Models.Member> orderMemberList = await _db.member.Where(m => m.id == memberMsaList[0].member_id)
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