using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using wechat_miniapp_base.Models;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Settings;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using Newtonsoft.Json;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.Order;
using System.IO;
using System.Collections;
using System.Text.RegularExpressions;

using static SKIT.FlurlHttpClient.Wechat.TenpayV3.Models.CreateHKTransactionMicroPayRequest.Types;
using SnowmeetApi.Controllers.User;

namespace SnowmeetApi.Controllers
{
    public class SkiPassMemo
    {
        public string use_date;
    }
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class OrderOnlinesController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public bool isStaff = false;

        private IConfiguration _oriConfig;

        private MemberController _memberHelper;



        public OrderOnlinesController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _memberHelper = new MemberController(context, config);
        }

        [HttpGet]
        public ActionResult<double> GetScoreRate(double orderPrice, double finalPrice)
        {
            return Util.GetScoreRate(finalPrice, orderPrice);
        }

        [HttpPost]
        public async Task<ActionResult<Mi7OrderDetail[]>> SaveMi7OrderDetail(string sessionKey, Mi7OrderDetail[] details)
        {
            for (int i = 0; i < details.Length; i++)
            {
                Mi7OrderDetail detail = details[i];
                var detailOriginList = await _context.mi7OrderDetail
                    .Where(m => (m.order_date.Date == detail.order_date.Date
                    && m.customer_mi7_order.Trim().Equals(detail.customer_mi7_order.Trim())
                    && m.product_code.Trim().Equals(detail.product_code.Trim())
                    && m.count == detail.count)).OrderByDescending(m => m.id).ToListAsync();

                var mi7OrderList = await _context.mi7Order
                        .Where(m => (m.mi7_order_id.Trim().Equals(detail.customer_mi7_order.Trim())))
                        .OrderByDescending(m => m.id).ToListAsync();

                if (mi7OrderList.Count > 0)
                {
                    Mi7Order mi7Order = mi7OrderList[0];
                    if (detailOriginList.Count == 0)
                    {
                        detail.mi7_order_id = mi7Order.order_id;
                        await _context.AddAsync(detail);
                        await _context.SaveChangesAsync();
                    }
                    else
                    {
                        Mi7OrderDetail detailOrigin = detailOriginList[0];
                        bool modified = false;
                        if (!detailOrigin.customer_mi7_name.Trim().Equals(detail.customer_mi7_name.Trim()))
                        {
                            modified = true;
                            detailOrigin.customer_mi7_name = detail.customer_mi7_name.Trim();
                        }
                        if (!detailOrigin.product_name.Trim().Equals(detail.product_name.Trim()))
                        {
                            modified = true;
                            detailOrigin.product_name = detail.product_name.Trim();
                        }
                        if (!detailOrigin.product_class.Trim().Equals(detail.product_class.Trim()))
                        {
                            modified = true;
                            detailOrigin.product_class = detail.product_class.Trim();
                        }
                        if (!detailOrigin.product_scale.Trim().Equals(detail.product_scale.Trim()))
                        {
                            modified = true;
                            detailOrigin.product_scale = detail.product_scale.Trim();
                        }
                        if (!detailOrigin.product_properties.Trim().Equals(detail.product_properties.Trim()))
                        {
                            modified = true;
                            detailOrigin.product_properties = detail.product_properties.Trim();
                        }
                        if (!detailOrigin.unit.Trim().Equals(detail.unit.Trim()))
                        {
                            modified = true;
                            detailOrigin.unit = detail.unit;
                        }
                        if (!detailOrigin.barcode.Trim().Equals(detail.barcode.Trim()))
                        {
                            modified = true;
                            detailOrigin.barcode = detail.barcode.Trim();
                        }
                        if (!detailOrigin.storage.Trim().Equals(detail.storage.Trim()))
                        {
                            modified = true;
                            detailOrigin.storage = detail.storage.Trim();
                        }
                        if (detailOrigin.count != detail.count)
                        {
                            modified = true;
                            detailOrigin.count = detail.count;
                        }
                        if (detailOrigin.product_price != detail.product_price)
                        {
                            modified = true;
                            detailOrigin.product_price = detail.product_price;
                        }
                        if (detailOrigin.discount_rate != detail.discount_rate)
                        {
                            modified = true;
                            detailOrigin.discount_rate = detail.discount_rate;
                        }
                        if (detailOrigin.sale_price != detail.sale_price)
                        {
                            modified = true;
                            detailOrigin.sale_price = detail.sale_price;
                        }
                        if (detailOrigin.total_cost != detail.total_cost)
                        {
                            modified = true;
                            detailOrigin.total_cost = detail.total_cost;
                        }
                        if (modified)
                        {
                            detailOrigin.update_date = DateTime.Now;
                            detailOrigin.updated_file_id = detail.original_file_id;
                            _context.Entry(detailOrigin).State = EntityState.Modified;
                            await _context.SaveChangesAsync();
                            details[i] = detailOrigin;
                        }
                    }
                }

            }
            return details;
        }

        [HttpPost]
        public async Task<ActionResult<Mi7OrderDetail[]>> ParseMi7OrderFromUploadFile(string sessionKey, IFormFile file)
        {

            sessionKey = Util.UrlDecode(sessionKey);
            UploadFileController uploadController = new UploadFileController(_context, _config);
            UploadFile upload = (await uploadController.UploadFile(sessionKey, "7色米订单明细", true, file)).Value;
            if (upload == null)
            {
                return BadRequest();
            }
            string filePath = Util.workingPath + "/wwwroot" + upload.file_path_name.Trim();
            StreamReader sr = System.IO.File.OpenText(filePath);
            string content = await sr.ReadToEndAsync();
            sr.Close();
            content = content.Trim();
            string[] linesArr = content.Split('\r');


            int index_order_date = -1;
            int index_customer_mi7_order = -1;
            int index_customer_mi7_name = -1;
            int index_product_code = -1;
            int index_product_name = -1;
            int index_product_class = -1;
            int index_product_scale = -1;
            int index_product_properties = -1;
            int index_unit = -1;
            int index_barcode = -1;
            int index_storage = -1;
            int index_count = -1;
            int index_product_price = -1;
            int index_discount_rate = -1;
            int index_sale_price = -1;
            int index_charge_summary = -1;
            int index_total_cost = -1;

            ArrayList mi7OrderDetails = new ArrayList();

            Regex reg = new Regex(",\".*,.*\",");

            for (int i = 0; i < linesArr.Length; i++)
            {
                string lineStr = linesArr[i].Trim();

                MatchCollection mc = reg.Matches(lineStr);

                foreach (Match m in mc)
                {
                    Console.WriteLine(m.ToString());
                    string originStr = m.Value.Trim();
                    originStr = originStr.Replace(",\"", "").Replace("\",", "").Trim();
                    string newStr = originStr.Replace(",", "，");
                    lineStr = lineStr.Replace(originStr, newStr);

                }

                string[] fields = lineStr.Split(',');
                if (i == 0)
                {
                    for (int j = 0; j < fields.Length; j++)
                    {
                        switch (fields[j].Trim())
                        {
                            case "业务日期":
                                index_order_date = j;
                                break;
                            case "单据编号":
                                index_customer_mi7_order = j;
                                break;
                            case "客户名称":
                                index_customer_mi7_name = j;
                                break;
                            case "商品编号":
                                index_product_code = j;
                                break;
                            case "商品名称":
                                index_product_name = j;
                                break;
                            case "商品分类":
                                index_product_class = j;
                                break;
                            case "规格":
                                index_product_scale = j;
                                break;
                            case "属性":
                                index_product_properties = j;
                                break;
                            case "单位":
                                index_unit = j;
                                break;
                            case "商品条码":
                                index_barcode = j;
                                break;
                            case "出库仓库":
                                index_storage = j;
                                break;
                            case "数量":
                                index_count = j;
                                break;
                            case "单价":
                                index_product_price = j;
                                break;
                            case "折扣":
                                index_discount_rate = j;
                                break;
                            case "折后单价":
                                index_sale_price = j;
                                break;
                            case "总额":
                                index_charge_summary = j;
                                break;
                            case "成本额":
                                index_total_cost = j;
                                break;
                            default:
                                break;
                        }
                    }
                }
                else
                {
                    try
                    {
                        Mi7OrderDetail detail = new Mi7OrderDetail()
                        {
                            id = 0,
                            order_date = DateTime.Parse(fields[index_order_date].Trim()),
                            mi7_order_id = 0,
                            customer_mi7_order = fields[index_customer_mi7_order].Trim(),
                            customer_mi7_name = fields[index_customer_mi7_name].Trim(),
                            product_code = fields[index_product_code].Trim(),
                            product_name = fields[index_product_name].Trim(),
                            product_class = fields[index_product_class].Trim(),
                            product_scale = fields[index_product_scale].Trim(),
                            product_properties = fields[index_product_properties].Trim(),
                            unit = fields[index_unit].Trim(),
                            barcode = fields[index_barcode].Trim(),
                            storage = fields[index_storage].Trim(),
                            count = int.Parse(fields[index_count].Trim()),
                            product_price = double.Parse(fields[index_product_price].Trim()),
                            discount_rate = double.Parse(fields[index_discount_rate].Trim()),
                            sale_price = double.Parse(fields[index_sale_price].Trim()),
                            charge_summary = double.Parse(fields[index_charge_summary].Trim()),
                            total_cost = double.Parse(fields[index_total_cost].Trim()),
                            original_file_id = upload.id,
                            updated_file_id = 0,
                            create_date = DateTime.Now,
                            update_date = DateTime.Now
                        };

                        mi7OrderDetails.Add(detail);
                    }
                    catch (Exception err)
                    {
                        Console.WriteLine(err.ToString());
                    }
                }
            }

            Mi7OrderDetail[] details = new Mi7OrderDetail[mi7OrderDetails.Count];
            for (int i = 0; i < details.Length; i++)
            {
                details[i] = (Mi7OrderDetail)mi7OrderDetails[i];
            }

            return details;
        }


        [HttpGet("{orderId}")]
        public async Task<ActionResult<OrderOnline>> OrderChargeByStaff(int orderId, double amount, string payMethod, string staffSessionKey)
        {

            UnicUser user = await UnicUser.GetUnicUserAsync(staffSessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            OrderOnline order = (await GetWholeOrderByStaff(orderId, staffSessionKey)).Value;
            if (order == null)
            {
                return NotFound();
            }
            if (order.paidAmount + amount > order.final_price)
            {
                return NoContent();
            }
            OrderPayment payment = new OrderPayment()
            {
                order_id = order.id,
                pay_method = payMethod.Trim(),
                amount = amount,
                staff_open_id = user.miniAppOpenId.Trim()
            };
            if (!payMethod.Trim().Equals("微信支付"))
            {
                payment.status = "支付成功";
            }
            else
            {
                payment.status = "待支付";
            }
            await _context.OrderPayment.AddAsync(payment);
            await _context.SaveChangesAsync();

            order = (await GetWholeOrderByStaff(orderId, staffSessionKey)).Value;

            if (order.paidAmount >= order.final_price)
            {
                order.pay_state = 1;
                _context.Entry(order).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }

            return (await GetWholeOrderByStaff(orderId, staffSessionKey)).Value;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<OrderOnline>>> GetOrdersByStaff(DateTime startDate, DateTime endDate,
            string shop, string status, string staffSessionKey, string mi7Num = "")
        {
            startDate = startDate.Date;
            endDate = endDate.Date.AddHours(24);
            staffSessionKey = Util.UrlDecode(staffSessionKey);
            mi7Num = Util.UrlDecode(mi7Num);
            UnicUser user = await UnicUser.GetUnicUserAsync(staffSessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            var listOri = await _context.OrderOnlines
                .Include(o => o.mi7Orders
                .Where(m => (mi7Num.Trim().Equals("") 
                    || (mi7Num.Equals("未填") && !m.mi7_order_id.StartsWith("XSD"))
                    || (mi7Num.Trim().Equals("已填") && m.mi7_order_id.StartsWith("XSD") )
                    || (mi7Num.Trim().Equals("紧急开单") && m.mi7_order_id.Trim().Equals(mi7Num) ) )))
                .Where(o => (  o.create_date >= startDate && o.create_date <= endDate && (shop == null ? true : (o.shop.Trim().Equals(shop.Trim())))))
                .OrderByDescending(o => o.id).ToListAsync();
            var list = listOri.Where(l => l.mi7Orders.Count > 0).ToList();
            
            for (int i = 0; i < list.Count; i++)
            {
                List<MemberSocialAccount> msaList = await _context.memberSocialAccount.Where(m => m.num.Trim().Equals(list[i].staff_open_id) && m.type.Trim().Equals("wechat_mini_openid"))
                    .Include(m => m.member).AsNoTracking().ToListAsync();
                list[i].msa = msaList.Count > 0? msaList[0]:null;
                list[i].payments = await _context.OrderPayment.Where(p => p.order_id == list[i].id).ToArrayAsync();
            }


            if (status == null || status.Trim().Equals(""))
            {
                return list;
            }
            else
            {
                var newList = new List<OrderOnline>();
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].status.Trim().Equals(status.Trim()))
                    {
                        newList.Add(list[i]);
                    }
                }
                return newList;
            }

        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<OrderOnline>> GetWholeOrderByStaff(int orderId, string staffSessionKey)
        {
            staffSessionKey = Util.UrlDecode(staffSessionKey);
            //UnicUser._context = _context;
            UnicUser user = await UnicUser.GetUnicUserAsync(staffSessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            if (order != null && !order.open_id.Trim().Equals(""))
            {
                try
                {
                    UnicUser customerUser = await UnicUser.GetUnicUserByDetailInfo(order.open_id, "snowmeet_mini", _context);
                    if (customerUser == null)
                    {
                        customerUser = await UnicUser.GetUnicUserByDetailInfo(order.open_id, "snowmeet_official_account_new", _context);
                    }
                    if (customerUser == null)
                    {
                        customerUser = await UnicUser.GetUnicUserByDetailInfo(order.open_id, "snowmeet_official_account", _context);
                    }

                    if (customerUser != null)
                    {
                        Member orderMember =  await _memberHelper.GetMember(order.open_id.Trim(), "wechat_mini_openid");
                        order.member = orderMember;
                        order.user = orderMember.miniAppUser;
                        
                        bool needUpdateMemberInfo = false;
                        string updateCellNum = "";
                        if (orderMember.real_name.Trim().Equals("") && !order.name.Trim().Equals(""))
                        {
                            orderMember.real_name = order.name.Trim().Replace("先生", "").Replace("女士", "");
                            needUpdateMemberInfo = true;
                        }
                        if (orderMember.cell == null && !order.cell_number.Trim().Equals(""))
                        {
                            updateCellNum = order.cell_number.Trim();
                            needUpdateMemberInfo = true;
                        }
                        if (orderMember.gender.Trim().Equals(""))
                        {
                            if (order.name.Trim().IndexOf("先生") >= 0)
                            {
                                orderMember.gender = "男";
                            }
                            if (order.name.Trim().IndexOf("女士") >= 0)
                            {
                                orderMember.gender = "女";
                            }
                            needUpdateMemberInfo = true;
                        }
                        if (needUpdateMemberInfo)
                        {
                            Member newMember = await _context.member.FindAsync(orderMember.id);
                            newMember.real_name = orderMember.real_name.Trim();
                            newMember.gender = orderMember.gender.Trim();
                            _context.member.Entry(newMember).State = EntityState.Modified;
                            if (!updateCellNum.Trim().Equals(""))
                            {
                                List<MemberSocialAccount> msaList = await _context.memberSocialAccount
                                    .Where(m => m.member_id == newMember.id && m.type.Trim().Equals("cell"))
                                    .AsNoTracking().ToListAsync();
                                if (msaList == null || msaList.Count == 0)
                                {
                                    MemberSocialAccount msa = new MemberSocialAccount()
                                    {
                                        id = 0,
                                        member_id = newMember.id,
                                        type = "cell",
                                        num = updateCellNum.Trim(),
                                        valid = 1,
                                        memo = "店销订单同步"
                                    };
                                    await _context.memberSocialAccount.AddAsync(msa);
                                }
                            }
                            await _context.SaveChangesAsync();
                        }
                    }
                }
                catch
                {
                    if (!order.open_id.Trim().Equals(""))
                    {
                        try
                        {
                            Member member = (await _memberHelper.GetMember(order.open_id.Trim(), "wechat_mini_openid"));
                            order.member = member;
                            order.user = member.miniAppUser;
                            bool needUpdateMemberInfo = false;
                            string updateCellNum = "";
                            if (user.member.real_name.Trim().Equals("") && !order.name.Trim().Equals(""))
                            {
                                user.member.real_name = order.name.Trim().Replace("先生", "").Replace("女士", "");
                                needUpdateMemberInfo = true;
                            }
                            if (user.member.cell.Trim().Equals("") && !order.cell_number.Trim().Equals(""))
                            {
                                updateCellNum = order.cell_number.Trim();
                                needUpdateMemberInfo = true;
                            }
                            if (user.member.gender.Trim().Equals(""))
                            {
                                if (order.name.Trim().IndexOf("先生")>=0)
                                {
                                    user.member.gender = "男";
                                }
                                if (order.name.Trim().IndexOf("女士")>=0)
                                {
                                    user.member.gender = "女";
                                }
                                needUpdateMemberInfo = true;
                            }
                            if (needUpdateMemberInfo)
                            {
                                Member newMember = await _context.member.FindAsync(user.member.id);
                                newMember.real_name = user.member.real_name.Trim();
                                newMember.gender = user.member.gender.Trim();
                                _context.member.Entry(newMember).State = EntityState.Modified;
                                if (!updateCellNum.Trim().Equals(""))
                                {
                                    List<MemberSocialAccount> msaList = await _context.memberSocialAccount
                                        .Where(m => m.member_id == user.member.id && m.type.Trim().Equals("cell"))
                                        .AsNoTracking().ToListAsync();
                                    if (msaList == null || msaList.Count == 0)
                                    {
                                        MemberSocialAccount msa = new MemberSocialAccount()
                                        {
                                            id = 0,
                                            member_id = newMember.id,
                                            type = "cell",
                                            num = updateCellNum.Trim(),
                                            valid = 1,
                                            memo = "店销订单同步"
                                        };
                                        await _context.memberSocialAccount.AddAsync(msa);
                                    }
                                }
                                await _context.SaveChangesAsync();
                            }
                        }
                        catch
                        {

                        }
                    }
                }
            }
            var mi7Orders = await _context.mi7Order.Where(o => o.order_id == orderId).ToListAsync();
            if (mi7Orders != null && mi7Orders.Count > 0)
            {
                order.mi7Orders = mi7Orders;
            }
            if (order != null && !order.pay_memo.Trim().Equals("无需支付"))
            {
                var payments = await _context.OrderPayment.Where(p => p.order_id == orderId).ToArrayAsync();
                for (int i = 0; i < payments.Length; i++)
                {
                    var payment = payments[i];
                    if (payment.staff_open_id != null && !payment.staff_open_id.Trim().Equals(""))
                    {
                        Member member = await _memberHelper.GetMember(payment.staff_open_id, "wechat_mini_openid");
                        //var staffUser = await _context.MiniAppUsers.FindAsync(payment.staff_open_id);
                        if (member != null)
                        {
                            payment.staffRealName = member.real_name.Trim();
                        }
                    }
                }
                order.payments = payments;

                var refunds = await _context.OrderPaymentRefund.Where(r => r.order_id == order.id
                    && (r.state == 1 || !r.refund_id.Trim().Equals(""))).ToListAsync();
                if (refunds != null)
                {
                    for(int k = 0; k < refunds.Count; k++)
                    {
                        List<MemberSocialAccount> msaList = await _context.memberSocialAccount
                            .Where(msa => (msa.num.Trim().Equals(refunds[k].oper.Trim()) && msa.type.Trim().Equals("wechat_mini_openid")))
                            .Include(msa => msa.member).AsNoTracking().ToListAsync();
                        if (msaList != null && msaList.Count > 0)
                        {
                            refunds[k].msa = msaList[0];
                        }
                        
                    }
                    order.refunds = refunds;
                }

            }

            string staffRealName = "";
            if (order != null && order.staff_open_id != null && !order.staff_open_id.Trim().Equals(""))
            {
                Member staffMember = await _memberHelper.GetMember(order.staff_open_id, "wechat_mini_openid");
                //MiniAppUser staffUser = await _context.MiniAppUsers.FindAsync(order.staff_open_id);
                if (staffMember != null)
                {
                    staffRealName = staffMember.real_name.Trim();
                }
            }
            if (order != null)
            {
                order.staffRealName = staffRealName.Trim();
            }


            if (order != null && order.ticket_code != null && !order.ticket_code.Trim().Equals(""))
            {
                order.tickets = await _context.Ticket.Where(t => t.code.Trim().Equals(order.ticket_code)).ToArrayAsync();
            }

            return Ok(order);
        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<OrderOnline>> SetOrderPaidManual(int orderId, string payMethod, string sessionKey)
        {
            payMethod = Util.UrlDecode(payMethod);
            sessionKey = Util.UrlDecode(sessionKey);

            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            var paymentList = await _context.OrderPayment.Where(p => p.order_id == order.id).ToListAsync();
            for (int i = 0; i < paymentList.Count; i++)
            {
                OrderPayment payment = (OrderPayment)paymentList[i];
                if (payment.status.Trim().Equals(OrderPayment.PaymentStatus.待支付.ToString()))
                {
                    payment.status = OrderPayment.PaymentStatus.取消.ToString();
                }
                _context.Entry(payment).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
            OrderPayment currentPayment = new OrderPayment()
            {
                order_id = order.id,
                pay_method = payMethod.Trim(),
                status = OrderPayment.PaymentStatus.支付成功.ToString(),
                amount = order.final_price
            };
            await _context.OrderPayment.AddAsync(currentPayment);
            order.pay_method = payMethod.Trim();
            order.pay_state = 1;
            order.pay_time = DateTime.Now;
            _context.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            OrderOnline orderNew = (OrderOnline)
                ((OkObjectResult)(await GetWholeOrderByStaff(order.id, sessionKey)).Result).Value;
            return Ok(orderNew);

        }

        [HttpGet("{paymentId}")]
        public async Task<ActionResult<OrderOnline>> SetPaymentSuccess(int paymentId, string staffSessionKey)
        {
            staffSessionKey = Util.UrlDecode(staffSessionKey);
            
            UnicUser user = await UnicUser.GetUnicUserAsync(staffSessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            OrderPayment payment = await _context.OrderPayment.FindAsync(paymentId);
            if (payment == null || payment.status.Trim().Equals(""))
            {
                return NotFound();
            }
            if (!payment.staff_open_id.Trim().Equals(user.miniAppOpenId.Trim()) && !payment.staff_open_id.Trim().Equals("待支付"))
            {
                return NoContent();
            }
            payment.status = "支付成功";
            _context.Entry(payment).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            var payments =  await _context.OrderPayment.Where(p => (p.status == "支付成功" && p.order_id == payment.order_id)).ToListAsync();

            var paidAmount = payments.Sum(p => p.amount);


            OrderOnline order = await _context.OrderOnlines.FindAsync(payment.order_id);
            if (order != null)
            {
                if (order.final_price <= paidAmount)
                {
                    order.pay_state = 1;
                }
                else
                {
                    order.pay_state = -1;
                }
                order.pay_time = DateTime.Now;
                _context.Entry(order).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                if (order.ticket_code != null && !order.ticket_code.Trim().Equals(""))
                {
                    var ticket = await _context.Ticket.FindAsync(order.ticket_code.Trim());
                    ticket.used = 1;
                    ticket.used_time = DateTime.Now;
                    _context.Entry(ticket).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                return NotFound();
            }


            try
            {
                if (order.generate_score > 0)
                {
                    PointController pc = new PointController(_context, _oriConfig);
                    await pc.SetPoint((int)order.generate_score, staffSessionKey, "店销现货支付赠送龙珠，订单ID：" + order.id.ToString());
                }
            }
            catch
            {

            }

            return await GetWholeOrderByStaff(payment.order_id, staffSessionKey);
        }

        [HttpPost("{orderId}")]
        public async Task<ActionResult<OrderOnline>> ConfirmNonPaymentOrder(int orderId, string staffSessionKey)
        {
            
            UnicUser user = await UnicUser.GetUnicUserAsync(staffSessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            var order = await _context.OrderOnlines.FindAsync(orderId);
            if (order.pay_state == 0 && order.pay_memo.Trim().Equals("无需付款"))
            {
                order.pay_state = 1;
                order.pay_time = DateTime.Now;
                _context.Entry(order).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return await GetWholeOrderByStaff(orderId, staffSessionKey);
            }
            else
            {
                return NotFound();
            }
        }


        [HttpPost]
        public async Task<ActionResult<OrderOnline>> PlaceOrderByStaff(OrderOnline order, string staffSessionKey)
        {
            staffSessionKey = Util.UrlDecode(staffSessionKey);
            
            UnicUser user = await  UnicUser.GetUnicUserAsync(staffSessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            order.staff_open_id = user.miniAppOpenId.Trim();
            if (order.have_score == 1)
            {
                order.score_rate = Util.GetScoreRate(order.final_price, order.order_price);
                order.generate_score = (int)(order.final_price * order.score_rate);
            }
            else
            {
                order.score_rate = 0;
                order.generate_score = 0;
            }
            order.pay_state = 0;
            await _context.OrderOnlines.AddAsync(order);
            int i = await _context.SaveChangesAsync();
            if (i <= 0)
            {
                return NoContent();
            }
            /*
            if (order.mi7Orders != null)
            {
                for (int j = 0; j < order.mi7Orders.Count; j++)
                {
                    order.mi7Orders[j].order_id = order.id;
                    order.mi7Orders[j].id = 0;
                    await _context.mi7Order.AddAsync(order.mi7Orders[j]);
                }
                await _context.SaveChangesAsync();
            }
            */
            if (order.payments != null && order.payments.Length == 1 && !(order.pay_memo.Trim().Equals("无需付款") || order.pay_memo.Trim().Equals("暂缓支付")))
            {
                var payment = order.payments[0];
                payment.order_id = order.id;
                payment.status = "待支付";
                payment.staff_open_id = order.staff_open_id;
                await _context.OrderPayment.AddAsync(payment);
                await _context.SaveChangesAsync();
                order.payments[0] = payment;
            }

            if (order.user != null && order.user.open_id != null &&  !order.user.open_id.Trim().Equals(""))
            {
                //MiniAppUser customerUser = await _context.MiniAppUsers.FindAsync(order.user.open_id);
                //Member member = await _memberHelper.GetMember(order.user.open_id, "wechat_mini_openiud");
                //_memberHelper.UpdateDetailInfo()
                Member member = await _memberHelper.GetMember(order.user.open_id, "wechat_mini_openid");
                bool memberMod = false;
                if (member.real_name.Trim().Equals(""))
                {
                    member.real_name = order.user.real_name;
                    memberMod = true;
                }
                if (member.gender.Trim().Equals(""))
                {
                    member.gender = order.user.gender;
                    memberMod = true;
                }
                if (memberMod)
                {
                    _context.member.Entry(member).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
                if (!order.user.cell_number.Trim().Equals("") && !member.cell.Trim().Equals(order.user.cell_number))
                {
                    await _memberHelper.UpdateDetailInfo(member.id, order.user.cell_number.Trim(), "cell", false);
                }

                

            }

            return order;
        }

        
        [HttpGet("{orderId}")]
        public async Task<ActionResult<bool>> SetSkiPassCertNo(int orderId, string certNo, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            if (!order.open_id.Trim().Equals(user.miniAppOpenId)
                && !order.open_id.Trim().Equals(user.officialAccountOpenId.Trim())
                && !order.open_id.Trim().Equals(user.officialAccountOpenIdOld.Trim()))
            {
                return NoContent();
            }
            string code = order.code;
            if (code.Trim().Equals(""))
            {
                return NotFound();
            }
            var card = await _context.Card.FindAsync(code);
            card.use_memo = certNo.Trim();
            _context.Entry(card).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return true;
        }

        [ActionName("GetSkiPassNum")]
        [HttpGet("{type}")]
        public async Task<ActionResult<int>> GetSkiPassNum(int type, string dateStr)
        {
            int num = 0;
            DateTime date = DateTime.Parse(dateStr);
            DateTime startDate = date.AddDays(-6);
            List<OrderOnline> orders = await _context.OrderOnlines
                .Where<OrderOnline>(o => (o.pay_time >= startDate && o.pay_time <= date.AddDays(1) && o.type.Trim().Equals("雪票") && o.shop.Trim().Equals("南山")))
                .ToListAsync<OrderOnline>();
            for (int i = 0; i < orders.Count; i++)
            {
                SkiPassMemo memo = JsonConvert.DeserializeObject<SkiPassMemo>(orders[i].memo.Trim());
                if (DateTime.Parse(memo.use_date).Date == date.Date)
                {
                    List<OrderOnlineDetail> detail = await _context.OrderOnlineDetails
                        .Where<OrderOnlineDetail>(o => (o.OrderOnlineId == orders[i].id)).ToListAsync<OrderOnlineDetail>();
                    Product prodcut = await _context.Product.FindAsync(detail[0].product_id);
                    if (type == 0 && prodcut.name.IndexOf("夜") < 0)
                    {
                        num = num + detail[0].count;
                    }
                    if (type == 1 && prodcut.name.IndexOf("夜") >= 0)
                    {
                        num = num + detail[0].count;
                    }
                }
            }
            return num;
        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<bool>> BindUser(int orderId, string sessionKey)
        {
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            if (!order.open_id.Trim().Equals(""))
            {
                return false;
            }
            else
            {
                UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
                order.open_id = user.miniAppOpenId.Trim();
                _context.Entry(order).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return true;
            }
        }


        // GET: api/OrderOnlines
        [HttpGet]
        public async Task<ActionResult<IEnumerable<OrderOnline>>> GetOrderOnlines()
        {
            return await _context.OrderOnlines.ToListAsync();
        }

        // GET: api/OrderOnlines/5
        [ActionName("GetOrderOnline")]
        [HttpGet("{id}")]
        public async Task<ActionResult<OrderOnline>> GetOrderOnline(int id, string sessionKey, bool needValid = true)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            //
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null)
            {
                return NotFound();
            }

            var orderOnline = await _context.OrderOnlines.FindAsync(id);

            if (orderOnline == null)
            {
                return NotFound();
            }

            if (user.isAdmin || !needValid
                || orderOnline.open_id.Trim().Equals(user.officialAccountOpenId.Trim())
                || orderOnline.open_id.Trim().Equals(user.miniAppOpenId.Trim())
                || orderOnline.open_id.Trim().Equals(""))
            {
                if (!user.isAdmin)
                {
                    orderOnline.open_id = "";
                }
                orderOnline.paymentList = await _context.OrderPayment.Where(p => (p.order_id == orderOnline.id))
                    .OrderByDescending(p => p.id).ToListAsync();

                orderOnline.refunds = await _context.OrderPaymentRefund.Where(r => r.order_id == orderOnline.id &&  (!r.refund_id.Trim().Equals("") || r.state == 1))
                    .OrderByDescending(r => r.id).ToListAsync();

                if (orderOnline.ticket_code != null && !orderOnline.ticket_code.ToString().Trim().Equals(""))
                {
                    orderOnline.tickets = await _context.Ticket.Where(t => t.code == orderOnline.ticket_code).ToArrayAsync();
                }
                orderOnline.details = await _context.OrderOnlineDetails.Where(d => d.OrderOnlineId == orderOnline.id).ToArrayAsync();
                return orderOnline;
            }

            return NotFound();
        }

        

        
        [HttpGet("{id}")]
        public async Task<ActionResult<WepayOrder>> Pay(int id,string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            int mchid = 3;
            string notify = "http://mini.snowmeet.top/core/WepayOrder/PaymentCallback";
            notify = Util.UrlDecode(notify);
            
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null)
            {
                return NotFound();
            }
            if ((user.miniAppUser != null && user.miniAppUser.is_admin == 1) 
                || (user.officialAccountUser != null && user.officialAccountUser.is_admin == 1))
            {
                isStaff = true;
            }
            else 
            {
                isStaff = false;
            }

            bool canPay = false;

            OrderOnline order = await _context.OrderOnlines.FindAsync(id);

            mchid = GetMchId(order);

            if (order == null)
            {
                return NotFound();
            }
            string timeStamp = Util.getTime13().ToString();
           
            order.open_id = user.miniAppUser.open_id.Trim();
            order.out_trade_no = timeStamp.Trim();
            _context.Entry(order).State = EntityState.Modified;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch
            { 
                
            }
            

            if (order != null && (order.open_id.Trim().Equals(user.miniAppOpenId.Trim())
                ||     order.open_id.Trim().Equals(user.officialAccountOpenId)     ))
            {
                canPay = true;
            }
            else if (isStaff)
            {
                canPay = true;
            }
            if (!canPay)
            {
                return NotFound();
            }

            

            WepayKey key = await _context.WepayKeys.FindAsync(mchid);
            if (key == null)
            {
                return NotFound();
            }

            WepayOrder wepayOrder = await _context.WepayOrders.FindAsync(timeStamp.Trim());

            if (wepayOrder != null)
            {
                return NotFound();
            }

            wepayOrder = new WepayOrder();
            wepayOrder.out_trade_no = timeStamp;
            wepayOrder.open_id = user.miniAppOpenId;
            wepayOrder.notify = notify.Trim();
            wepayOrder.order_id = order.id;
            wepayOrder.amount = (int)Math.Round((order.order_real_pay_price * 100),0);
            wepayOrder.app_id = _appId;
            wepayOrder.description = "";
            wepayOrder.mch_id = mchid;
            _context.WepayOrders.Add(wepayOrder);
            await _context.SaveChangesAsync();
            

            var certManager = new InMemoryCertificateManager();
            var options = new WechatTenpayClientOptions()
            {
                MerchantId = key.mch_id.Trim(),
                MerchantV3Secret = "",
                MerchantCertificateSerialNumber = key.key_serial.Trim(),
                MerchantCertificatePrivateKey = key.private_key.Trim(),
                PlatformCertificateManager = certManager
            };
            var client = new WechatTenpayClient(options);
            var request = new CreatePayTransactionJsapiRequest()
            {
                OutTradeNumber = timeStamp,
                AppId = _appId,
                Description = wepayOrder.description.Trim().Equals("")?"测试商品":wepayOrder.description.Trim(),
                ExpireTime = DateTimeOffset.Now.AddMinutes(30),
                NotifyUrl = wepayOrder.notify.Trim() + "/" + mchid.ToString(),
                Amount = new CreatePayTransactionJsapiRequest.Types.Amount()
                { 
                    Total = wepayOrder.amount
                },
                Payer = new CreatePayTransactionJsapiRequest.Types.Payer()
                { 
                    OpenId = wepayOrder.open_id.Trim()
                }
            };
            var response = await client.ExecuteCreatePayTransactionJsapiAsync(request);
            if (response != null && response.PrepayId != null && !response.PrepayId.Trim().Equals(""))
            {
                wepayOrder.prepay_id = response.PrepayId.Trim();
                wepayOrder.state = 1;
                _context.Entry(wepayOrder).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                var paraMap = client.GenerateParametersForJsapiPayRequest(request.AppId, response.PrepayId);
                wepayOrder.timestamp = paraMap["timeStamp"].Trim();
                wepayOrder.nonce = paraMap["nonceStr"].Trim();
                wepayOrder.sign = paraMap["paySign"].Trim();
                return wepayOrder;
            }



            return NotFound();
        }
        

        // PUT: api/OrderOnlines/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutOrderOnline(int id, OrderOnline orderOnline)
        {
            if (id != orderOnline.id)
            {
                return BadRequest();
            }

            _context.Entry(orderOnline).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!OrderOnlineExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        [HttpGet("{orderId}")]
        public async Task SetOrderMemo(int orderId, string memo, string sessionKey)
        {
            memo = Util.UrlDecode(memo);
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null || !user.isAdmin)
            {
                return;
                //return NotFound();
            }
            OrderOnline? order = await _context.OrderOnlines.FindAsync(orderId);
            if (order == null)
            {
                return;
            }
            order.memo = memo;
            _context.OrderOnlines.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();
        }


        [NonAction]
        private bool OrderOnlineExists(int id)
        {
            return _context.OrderOnlines.Any(e => e.id == id);
        }
        [NonAction]
        private int GetMchId(OrderOnline order)
        {
            int mchId = 3;
            if (order.type == "押金")
            {
                mchId = 5;
            }
            if (order.type != "雪票" && order.shop == "南山")
            {
                mchId = 6;
            }
            if (order.type == "雪票" && order.shop == "南山")
            {
                mchId = 7;
            }
            return mchId;
        }
    }
}
