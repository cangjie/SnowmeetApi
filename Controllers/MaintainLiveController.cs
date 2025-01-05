using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Org.BouncyCastle.Utilities;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Maintain;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Order;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Org.BouncyCastle.Asn1.X509;
using SnowmeetApi.Controllers.Maintain;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class MaintainLiveController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;
        private IConfiguration _originConfig;
        private readonly MaintainLogsController _logHelper;
        private readonly OrderOnlinesController _orderHelper;
        


        public MaintainLiveController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _originConfig = config;
            _logHelper = new MaintainLogsController(context, config);
            _orderHelper = new OrderOnlinesController(context, config);
        }


        [NonAction]
        public async Task UpdateBrand(string brand, string type)
        {
            string[] brandArr = brand.Split('/');
            string brandName = brandArr[0].Trim();
            string brandChineseName = brandArr.Length == 2? brandArr[1].Trim() : "";
            List<Brand> brands = await _context.Brand
                .Where(b => b.brand_type.Trim().Equals(type.Trim()) && b.brand_name.Trim().Equals(brandName.Trim()))
                .AsNoTracking().ToListAsync();
            if (brands != null && brands.Count > 0)
            {
                return;
            }
            Brand newBrand = new Brand()
            {
                brand_type = type.Trim(),
                brand_name = brandName.Trim(),
                chinese_name = brandChineseName.Trim(),
                origin = ""

            };
            await _context.Brand.AddAsync(newBrand);
            await _context.SaveChangesAsync();
        }

        [NonAction]
        public async Task UpdateSerial(string serial, string brand, string type)
        {
            List<Serial> sList = await _context.Serial
                .Where(s => s.brand_name.Trim().Equals(brand.Trim()) 
                && s.type.Trim().Equals(type.Trim()) && s.serial_name.Trim().Equals(serial.Trim()))
                .AsNoTracking().ToListAsync();
            if (sList != null && sList.Count > 0)
            {
                return;
            }
            Serial newSerial = new Serial()
            {
                id = 0,
                brand_name = brand.Trim(),
                type = type.Trim(),
                serial_name = serial.Trim()
            };
            await _context.Serial.AddAsync(newSerial);
            await _context.SaveChangesAsync();
        }


        [HttpPost]
        public async Task<ActionResult<MaintainLive>> UpdateTask(MaintainLive task, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            MaintainLive oriTask = await _context.MaintainLives.Where(t => t.id == task.id)
                .AsNoTracking().FirstAsync();
            if (oriTask == null)
            {
                return NotFound();
            }
            /*
            if (!task.confirmed_serial.Trim().Equals("") && !task.confirmed_brand.Trim().Equals(""))
            {
                var serialList = await _context.Serial.Where(s => (s.brand_name.Trim().Equals(task.confirmed_brand.Trim()) && s.serial_name.Trim().Equals(task.confirmed_serial.Trim()))).ToListAsync();
                if (serialList.Count == 0 && !task.confirmed_serial.Equals("未知"))
                {
                    Serial s = new Serial()
                    {
                        id = 0,
                        type = task.confirmed_equip_type.Trim(),
                        brand_name = task.confirmed_brand.Trim(),
                        serial_name = task.confirmed_serial.Trim()
                    };
                    await _context.Serial.AddAsync(s);
                    await _context.SaveChangesAsync();
                }
            }
            */
            
            
            MaintainLog log = new MaintainLog()
            {
                id = 0,
                task_id = oriTask.id,
                step_name = "修改装备信息",
                memo = "",
                start_time = DateTime.Now,
                end_time = DateTime.Now,
                staff_open_id = user.member.wechatMiniOpenId.Trim(),
                status = "",
                stop_open_id = user.member.wechatMiniOpenId.Trim(),
                backup_data = Newtonsoft.Json.JsonConvert.SerializeObject(oriTask)
            };
            await _context.MaintainLog.AddAsync(log);
            _context.Entry(task).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            await UpdateBrand(task.confirmed_brand, task.confirmed_equip_type.Trim());
            await UpdateSerial(task.confirmed_serial, task.confirmed_brand.Split('/')[0].Trim(), task.confirmed_equip_type);
            return task;
        }


        [HttpGet]
        public async Task<ActionResult<IEnumerable<Serial>>> GetSerials(string brand, string type)
        {
            brand = Util.UrlDecode(brand).Trim();
            return await _context.Serial.Where(s => (s.brand_name.Trim().Equals(brand) && s.type.Trim().Equals(type.Trim()) )).ToListAsync();
        }

        [HttpGet]
        public async Task<ActionResult<Serial>> AddSerial(string brand, string serialName, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            brand = Util.UrlDecode(brand).Trim();
            serialName = Util.UrlDecode(serialName).Trim();
            var list = await _context.Serial.Where(s => (s.brand_name.Trim().Equals(brand)
                && s.serial_name.Trim().Equals(serialName.Trim()))).ToListAsync();
            if (list.Count > 0)
            {
                return NoContent();
            }
            Serial s = new Serial()
            {
                id = 0,
                brand_name = brand,
                serial_name = serialName.Trim()
            };
            await _context.Serial.AddAsync(s);
            await _context.SaveChangesAsync();
            return s;
        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<MaintainOrder>> BindNewMember(int orderId, string sessionKey)
        {
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            if (!order.open_id.Trim().Equals(""))
            {
                return NotFound();
            }
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            order.open_id = user.miniAppOpenId.Trim();
            _context.Entry(order).State = EntityState.Modified;
            MaintainLive[] items = await _context.MaintainLives.Where(m => m.order_id == orderId).ToArrayAsync();
            foreach (MaintainLive i in items)
            {
                i.open_id = user.miniAppOpenId.Trim();
                _context.Entry(i).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
            return await GetMaintainOrder(orderId, sessionKey);

        }

        [HttpGet]
        public async Task<ActionResult<MaintainOrder[]>> GetMyMaintainOrders(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            MaintainOrder[] orders = await GetMaintainOrders(user.miniAppOpenId, DateTime.Parse("2022-9-1"), DateTime.Parse("2999-1-1"));
            for (int i = 0; i < orders.Length; i++)
            {
                orders[i].order.open_id = "";
                for (int j = 0; j < orders[i].items.Length; j++)
                {
                    orders[i].items[j].open_id = "";
                }
            }
            return orders; 
        }

        [NonAction]
        public async Task<MaintainOrder[]> GetMaintainOrders(string openId, DateTime start, DateTime end)
        {
            
            OrderOnline[] orderList;
            if (openId.Trim().Equals(""))
            {
                orderList = await _context.OrderOnlines
                    .Where(o => (o.create_date.Date >= start.Date && o.create_date.Date <= end.Date
                    && o.type.Equals("服务")))
                    .OrderByDescending(o=>o.id).ToArrayAsync();
            }
            else
            {
                orderList = await _context.OrderOnlines
                    .Where(o => (o.create_date.Date >= start.Date && o.create_date.Date <= end.Date
                    && o.open_id.Trim().Equals(openId) && o.type.Equals("服务") ))
                    .OrderByDescending(o=>o.id).ToArrayAsync();
            }
            MaintainOrder[] maintainOrderArray = new MaintainOrder[orderList.Length];
            for (int i = 0; i < maintainOrderArray.Length; i++)
            {
                OrderOnline onlineOrder = orderList[i];
                MaintainLive[] items = await _context.MaintainLives.Where(m => m.order_id == onlineOrder.id).ToArrayAsync();
                MaintainOrder order = new MaintainOrder()
                {
                    cell = onlineOrder.cell_number.Trim(),
                    name = onlineOrder.name,
                    orderId = onlineOrder.id,
                    order = onlineOrder,
                    items = items,
                    orderDate = onlineOrder.create_date

                };
                maintainOrderArray[i] = order;
            }
            return maintainOrderArray;
        }

        [HttpGet]
        public async Task<ActionResult<List<MaintainLive>>> GetMyMaintainTask(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            var taskL = await GetMaintainTask(DateTime.Parse("2020-1-1"), DateTime.Parse("2099-1-1"), user.miniAppOpenId);
            for (int i = 0; taskL != null && i < taskL.Count; i++)
            {
                taskL[i].open_id = "";
                if (taskL[i].order != null)
                {
                    taskL[i].order.open_id = "";
                }
                taskL[i].log = await _context.MaintainLog.Where(t => t.task_id == taskL[i].id)
                    .OrderBy(t => t.id).AsNoTracking().ToArrayAsync();
            }
            return Ok(taskL);
        }


        [NonAction]
        public async Task<List<MaintainLive>> GetMaintainTask(DateTime startDate, DateTime endDate, string openId)
        {
            openId = openId.Trim();
            var taskL = await _context.MaintainLives.Where(m => (m.task_flow_num != null
                && m.create_date.Date >= startDate && m.create_date.Date <= endDate.Date
                && (openId.Equals("") || m.open_id.Trim().Equals(openId))))
                .OrderByDescending(m=> m.id).AsNoTracking().ToListAsync();
            for (int i = 0; taskL != null && i < taskL.Count; i++)
            {
                if (taskL[i].order_id > 0)
                {
                    taskL[i].order = await _context.OrderOnlines.FindAsync(taskL[i].order_id);
                }
            }
            return taskL;
        }



        [HttpGet("{orderId}")]
        public async Task<ActionResult<MaintainOrder>> GetMaintainOrder(int orderId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            if (orderId <= 0)
            {
                return NotFound();
            }
            
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            
            MaintainLive[] items = await _context.MaintainLives.Where(m => m.order_id == orderId)
                .Include(m => m.taskLog).ToArrayAsync();
            double itemPriceSummary = 0;

            foreach (MaintainLive item in items)
            {
                //item.taskLog = await _context.MaintainLog.Where(l => l.task_id == item.id).AsNoTracking().OrderBy(l => l.id).ToArrayAsync();
                Models.Product.Product p = await _context.Product.FindAsync(item.confirmed_product_id);
                itemPriceSummary = itemPriceSummary + (p!=null?p.sale_price:0) + item.confirmed_additional_fee;
            }
            OrderOnlinesController orderController = new OrderOnlinesController(_context, _originConfig);
            OrderOnline order = (await orderController.GetOrderOnline(orderId, sessionKey)).Value; //await _context.OrderOnlines.FindAsync(orderId);   //(await orderController.GetWholeOrderByStaff(orderId, sessionKey)).Value;

            if (!order.open_id.Trim().Equals(user.miniAppOpenId.Trim()) && !order.open_id.Equals(user.officialAccountOpenId.Trim())
                && !order.open_id.Trim().Equals(user.officialAccountOpenIdOld.Trim()) && !user.isAdmin)
            {
                return BadRequest();
            }

            MaintainOrder mOrder = new MaintainOrder()
            {
                cell = order.cell_number,
                name = order.name,
                orderId = orderId,
                order = order,
                items = items,
                orderDate = order.create_date,
                summaryPrice = itemPriceSummary,
                discount = order.other_discount,
                ticketCode = order.ticket_code,
                ticketDiscount = order.ticket_amount
            };
            if (!user.isAdmin)
            {
                foreach (MaintainLive m in mOrder.items)
                {
                    m.open_id = "";
                }
                
            }
            if (!order.ticket_code.Trim().Equals(""))
            {
                mOrder.ticket = await _context.Ticket.FindAsync(order.ticket_code.Trim());
                if (!user.isAdmin && mOrder.ticket != null)
                {
                    mOrder.ticket.open_id = "";
                }
            }
            return Ok(mOrder);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<MaintainLive>> GetTask(int id, string sessionKey)
        {
            MaintainLive task = await _context.MaintainLives.FindAsync(id);
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin && !task.open_id.Trim().Equals(user.miniAppOpenId.Trim()))
            {
                return NoContent();
            }
            if (!user.isAdmin)
            {
                task.open_id = "";
            }
            task.order = (OrderOnline)((OkObjectResult)(await _orderHelper.GetWholeOrderByStaff(task.order_id, sessionKey)).Result).Value;
            task.log = await _context.MaintainLog.Where(l => l.task_id == id).OrderBy(m => m.id).AsNoTracking().ToArrayAsync();
            return Ok(task);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<MaintainLive>>> GetTasks(DateTime start, DateTime end, string sessionKey, string shop = "", string openId = "")
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            start = start.Date;
            end = end.Date.AddDays(1);
            var liveArr = await _context.MaintainLives.Include(m => m.taskLog).Include(m => m.order)
                .Where(m => (!m.task_flow_num.Trim().Equals("") && m.create_date >= start && m.create_date < end
                && (shop.Equals("") || m.shop.Equals(shop)) && (openId.Trim().Equals("") || m.open_id.Trim().Equals(openId)) ))
                .AsNoTracking().OrderByDescending(m => m.id).ToListAsync();

            for (int i = 0; i < liveArr.Count; i++)
            {
                MaintainLive m = (MaintainLive)liveArr[i];
                //var logs = 
                //m.taskLog = ((IEnumerable<MaintainLog>)((OkObjectResult)(await _logHelper.GetStepsByStaff(m.id, sessionKey)).Result).Value).ToArray();
                string lastStep = m.taskLog.Count == 0 ? ""
                    : m.taskLog[m.taskLog.Count - 1].step_name.Trim();
                
                //lastStep = m.taskLog[m.taskLog.Length - 1].step_name.Trim();
                if (m.taskLog == null || m.taskLog.Count == 0)
                {
                    m.status = "未开始";
                }
                else if (lastStep.Trim().Equals("发板") || lastStep.Trim().Equals("强行索回"))
                {
                    m.status = "已完成";
                }
                else
                {
                    m.status = "进行中";
                }

                string desc = "";
                if (m.confirmed_edge == 1)
                {
                    desc += "修刃：" + m.confirmed_degree.ToString();
                }
                if (m.confirmed_candle == 1)
                {
                    desc += " 打蜡 ";
                }
                if (!m.confirmed_more.Trim().Equals(""))
                {
                    desc += " " + m.confirmed_more.Trim() + " ";
                }
                desc += m.confirmed_memo;
                m.description = desc;

                /*
                if (m.order_id > 0)
                {
                    m.order = (OrderOnline)((OkObjectResult)(await _orderHelper.GetWholeOrderByStaff(m.order_id, sessionKey)).Result).Value;
                }
                */
            }

            return Ok(liveArr);
        }

        [HttpGet]
        public async Task<ActionResult<List<MaintainLive>>> GetInStockTask(string shop, string sessionKey)
        {
            shop = Util.UrlDecode(shop.Trim());
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            var liveArr = await _context.MaintainLives.FromSqlRaw(" select top 300 * from maintain_in_shop_request "
                + " where not exists ( select 'a' from maintain_log where maintain_log.task_id = maintain_in_shop_request.[id] and step_name in ('发板','强行索回') ) "
                + (shop.Trim().Equals("")? " " : " and shop = '" + shop.Trim().Replace("'", "") + "'  ")
                + " and task_flow_num <> '' order by [id] desc ").Include(m => m.taskLog).AsNoTracking().ToListAsync();

            for (int i = 0; i < liveArr.Count; i++)
            {
                MaintainLive m = (MaintainLive)liveArr[i];
                //var logs = 
                //m.taskLog = ((IEnumerable<MaintainLog>)((OkObjectResult)(await _logHelper.GetStepsByStaff(m.id, sessionKey)).Result).Value).ToArray();
                string lastStep = m.taskLog.Count == 0 ? ""
                    : m.taskLog[m.taskLog.Count - 1].step_name.Trim();

                //lastStep = m.taskLog[m.taskLog.Length - 1].step_name.Trim();
                if (m.taskLog == null || m.taskLog.Count == 0)
                {
                    m.status = "未开始";
                }
                else if (lastStep.Trim().Equals("发板") || lastStep.Trim().Equals("强行索回"))
                {
                    m.status = "已完成";
                }
                else
                {
                    m.status = "进行中";
                }

                string desc = "";
                if (m.confirmed_edge == 1)
                {
                    desc += "修刃：" + m.confirmed_degree.ToString();
                }
                if (m.confirmed_candle == 1)
                {
                    desc += " 打蜡 ";
                }
                if (!m.confirmed_more.Trim().Equals(""))
                {
                    desc += " " + m.confirmed_more.Trim() + " ";
                }
                desc += m.confirmed_memo;
                m.description = desc;

                if (m.order_id > 0)
                {
                    m.order = (OrderOnline)((OkObjectResult)(await _orderHelper.GetWholeOrderByStaff(m.order_id, sessionKey)).Result).Value;
                }
            }

            return Ok(liveArr);

        }

        [HttpPost]
        public async Task<ActionResult<MaintainOrder>> Recept(string sessionKey, MaintainOrder maintainOrder)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            //
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);//UnicUser.GetUnicUser(sessionKey);
            if (!user.isAdmin)
            {
                return NoContent();
            }

            int orderId = 0;
            string customerName = maintainOrder.name.Trim();
            if (maintainOrder.gender.Trim().Equals("男"))
            {
                customerName += "先生";
            }
            if (maintainOrder.gender.Trim().Equals("女"))
            {
                customerName += "女士";
            }
            if (!maintainOrder.payOption.Trim().Equals("无需支付") && !maintainOrder.payOption.Trim().Equals("次卡支付"))
            {
                double finalPrice = maintainOrder.summaryPrice - maintainOrder.ticketDiscount - maintainOrder.discount;
                
                double scoreRate = Util.GetScoreRate(finalPrice, maintainOrder.summaryPrice);
                double score = finalPrice * scoreRate;
                OrderOnline order = new OrderOnline()
                {
                    id = 0,
                    type = "服务",
                    shop = maintainOrder.shop.Trim(),
                    open_id = maintainOrder.customerOpenId,
                    name = customerName,
                    cell_number = maintainOrder.cell.Trim(),
                    pay_method = maintainOrder.payMethod.Trim(),
                    pay_memo = maintainOrder.payOption.Trim(),
                    pay_state = 0,
                    order_price = maintainOrder.summaryPrice,
                    order_real_pay_price = maintainOrder.summaryPrice,
                    ticket_amount = maintainOrder.ticketDiscount,
                    other_discount = maintainOrder.discount,
                    final_price = maintainOrder.summaryPrice - maintainOrder.ticketDiscount - maintainOrder.discount,
                    ticket_code = maintainOrder.ticketCode.Trim(),
                    staff_open_id = user.miniAppOpenId.Trim(),
                    score_rate = scoreRate,
                    generate_score = score

                };
                await _context.AddAsync(order);
                await _context.SaveChangesAsync();

                OrderPayment payment = new OrderPayment()
                {
                    order_id = order.id,
                    pay_method = order.pay_method.Trim(),
                    amount = order.final_price,
                    status = "待支付",
                    staff_open_id = user.miniAppOpenId.Trim()
                };
                await _context.OrderPayment.AddAsync(payment);
                await _context.SaveChangesAsync();

                orderId = order.id;
                maintainOrder.order = order;
                if (order.id <= 0)
                {
                    return NotFound();
                }
                for (int i = 0; i < maintainOrder.items.Length; i++)
                {
                    MaintainLive item = maintainOrder.items[i];
                    if (item.confirmed_product_id > 0)
                    {
                        OrderOnlineDetail detail = new OrderOnlineDetail()
                        {
                            OrderOnlineId = orderId,
                            product_id = item.confirmed_product_id,
                            count = 1
                        };
                        await _context.AddAsync(detail);
                        await _context.SaveChangesAsync();
                    }
                    if (item.confirmed_additional_fee > 0)
                    {
                        OrderOnlineDetail detail = new OrderOnlineDetail()
                        {
                            OrderOnlineId = orderId,
                            product_id = 146,
                            count = (int)item.confirmed_additional_fee
                        };
                        await _context.AddAsync(detail);
                        await _context.SaveChangesAsync();
                    }
                }

            }

            for (int i = 0; i < maintainOrder.items.Length; i++)
            {
                MaintainLive item = maintainOrder.items[i];
                item.order_id = orderId;
                item.service_open_id = user.miniAppOpenId.Trim();
                item.pay_method = maintainOrder.payMethod.Trim();
                if (maintainOrder.payOption.Trim().Equals("无需支付") || maintainOrder.payOption.Trim().Equals("次卡支付"))
                {
                    item.pay_memo = maintainOrder.payOption.Trim();
                }
                await _context.AddAsync(item);
                await _context.SaveChangesAsync();
                if (maintainOrder.payOption.Trim().Equals("无需支付") || maintainOrder.payOption.Trim().Equals("次卡支付"))
                {
                    await GenerateFlowNum(item.id);
                }
                
            }

            maintainOrder.orderId = orderId;



            return maintainOrder;
        }

        [NonAction]
        public async Task MaitainOrderPaySuccess(int orderId)
        {
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            var tastList = await _context.MaintainLives.Where(m => m.order_id == orderId).ToListAsync();
            for (int i = 0; i < tastList.Count; i++)
            {
                if (tastList[i].open_id == null || tastList[i].open_id.Trim().Equals(""))
                {
                    MaintainLive task = tastList[i];
                    task.open_id = order.open_id.Trim();
                    _context.Entry(task).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
                await GenerateFlowNum(tastList[i].id);
            }
        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<MaintainOrder>> MaitainOrderPaySuccessManual(int orderId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            //
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);



            if (!order.pay_method.Trim().Equals("微信支付"))
            {
                OrderOnlinesController orderController = new OrderOnlinesController(_context, _originConfig);
                order = (await orderController.OrderChargeByStaff(orderId, order.final_price, order.pay_method.Trim(), sessionKey)).Value;
                if (order == null)
                {
                    order = (await orderController.GetWholeOrderByStaff(orderId, sessionKey)).Value;
                }
                if (order.payments.Length > 0)
                {
                    int paymentId = order.payments[0].id;
                    order = (await orderController.SetPaymentSuccess(paymentId, sessionKey)).Value;
                }
                if (order.status.Trim().Equals("支付完成"))
                {
                    await MaitainOrderPaySuccess(order.id);
                }

                return await GetMaintainOrder(orderId, sessionKey);

            }
            else
            {
                return NoContent();
            }

        }

        [NonAction]
        public async Task<ActionResult<MaintainLive>> GenerateFlowNum(int taskId)
        {
            
            MaintainLive task = await _context.MaintainLives.FindAsync(taskId);
            if (!task.task_flow_num.Trim().Equals(""))
            {
                return task;
            }
            string dateStr = task.create_date.Year.ToString().Substring(2, 2)
                + task.create_date.Month.ToString().PadLeft(2, '0') + task.create_date.Day.ToString().PadLeft(2, '0');
            string flowNum = "";
            try
            {
                MaintainLive lastTask = await _context.MaintainLives.Where(m => m.task_flow_num.StartsWith(dateStr)).OrderByDescending(m => m.task_flow_num).FirstAsync();

                if (lastTask != null)
                {
                    if (lastTask.task_flow_num.StartsWith(dateStr))
                    {
                        int seq = int.Parse(lastTask.task_flow_num.Split('-')[1].Trim());
                        flowNum = dateStr + "-" + (seq + 1).ToString().PadLeft(5, '0');

                    }
                }
            }
            catch
            {

            }
            if (flowNum.Trim().Equals(""))
            {
                flowNum = dateStr + "-00001";
            }
            task.task_flow_num = flowNum.Trim();
            _context.Entry(task).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            return task;
        }


        //public async Task<ActionResult<MaintainOrder>> 



        [HttpGet]
        public async Task<ActionResult<IEnumerable<Brand>>> GetBrand(string type)
        {
            return await _context.Brand.Where(b => b.brand_type.Trim().Equals(type.Trim()))
                .OrderBy(b => b.brand_name).ToListAsync();
        }

        [HttpGet]
        public async Task<ActionResult<Equip[]>> GetEquip(string openId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            //
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            openId = Util.UrlDecode(openId);
            var list = await _context.MaintainLives
                .Where(m => (m.open_id.Trim().Equals(openId) && m.task_flow_num != null))
                .Select(m => new { m.confirmed_equip_type, m.confirmed_brand, m.confirmed_scale })
                .Distinct()
                .ToListAsync();
            Equip[] equipArr = new Equip[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                equipArr[i] = new Equip()
                {
                    type = list[i].confirmed_equip_type.Trim(),
                    brand = list[i].confirmed_brand.Trim(),
                    serial = "",
                    year = "",
                    scale = list[i].confirmed_scale.Trim()
                };
            }
            return equipArr;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<MaintainLive>>> GetMaintainLog(string openId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            //
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            openId = Util.UrlDecode(openId);
            var list = await _context.MaintainLives
                .Where(m => (m.open_id.Trim().Equals(openId) && m.task_flow_num != null))
                .OrderByDescending(m => m.id).ToListAsync();
            return list;

        }


        [HttpGet("{openId}")]
        public async Task<ActionResult<MaintainLive>> GetLast(string openId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            openId = Util.UrlDecode(sessionKey);
            
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null || !user.isAdmin)
            {
                return NoContent();
            }
            var lastItemArr = await _context.MaintainLives.Where(m => m.open_id.Equals(openId)).OrderByDescending(m => m.id).ToListAsync();
            if (lastItemArr != null && lastItemArr.Count > 0)
            {
                return (MaintainLive)lastItemArr[0];
            }
            return NoContent();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<OrderOnline>> PlaceOrder(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            OrderOnline order = (await PlaceOrderAll(id, sessionKey, "")).Value;
            if (order == null)
            {
                return NoContent();
            }
            else
            {
                return order;
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<OrderOnline>> PlaceOrderBatch(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            OrderOnline order = (await PlaceOrderAll(id, sessionKey, "batch")).Value;
            if (order == null)
            {
                return NoContent();
            }
            else
            {
                return order;
            }
        }

        [HttpGet("{batchId}")]
        public async Task<ActionResult<OrderOnline>> PlaceBlankOrderBatch(int batchId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            //string openId = Util.UrlDecode(sessionKey);
            
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null || !user.isAdmin)
            {
                return NoContent();
            }
            string openId = user.miniAppOpenId.Trim();
            var taskList = await _context.MaintainLives.Where(m => m.batch_id == batchId).ToListAsync();
            if (taskList.Count == 0)
            {
                return NotFound();
            }
            string payMethod = taskList[0].pay_method.Trim();
            if (payMethod.Trim().Equals("微信"))
            {
                return NoContent();
            }
            OrderOnline order = new OrderOnline()
            {
                type = "服务",
                open_id = openId,
                cell_number = taskList[0].confirmed_cell.Trim(),
                name = taskList[0].confirmed_name.Trim(),
                pay_method = taskList[0].pay_method.Trim(),
                order_price = 0,
                order_real_pay_price = 0,
                pay_state = 1,
                pay_time = DateTime.Now,
                shop = taskList[0].shop.Trim(),
                out_trade_no = "",
                ticket_code = "",
                code = ""
            };
            _context.OrderOnlines.Add(order);
            _context.SaveChanges();
            if (order.id == 0)
            {
                return NoContent();
            }
            else
            {
                for (int i = 0; i < taskList.Count; i++)
                {
                    var task = taskList[i];
                    task.order_id = order.id;
                    _context.Entry<MaintainLive>(task);
                    _context.SaveChanges();
                }
                return order;
            }
        }

        [HttpGet("id")]
        private async Task<ActionResult<OrderOnline>> PlaceOrderAll(int id, string sessionKey, string idType = "batch")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            bool exists = false;
            string ticketCode = "";
            if (idType.Trim().Equals("batch"))
            {
                exists = MaintainLiveBatchExists(id);
            }
            else
            {
                exists = MaintainLiveExists(id);
            }
            if (exists)
            {
                sessionKey = Util.UrlDecode(sessionKey);
                
                UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
                if (user == null && user.miniAppOpenId == null && user.miniAppOpenId.Trim().Equals("")
                    && user.officialAccountOpenId == null && user.officialAccountOpenId.Trim().Equals(""))
                {
                    return null;
                }
                else
                {
                    List<MaintainLive> tasks = new List<MaintainLive>();
                    List<OrderOnlineDetail> details = new List<OrderOnlineDetail>();
                    bool canOrder = true;
                    if (idType.Trim().Equals("batch"))
                    {
                        tasks = _context.MaintainLives.Where(m => m.batch_id == id).ToList<MaintainLive>();
                        for (int i = 0; canOrder && i < tasks.Count; i++)
                        {
                            if (tasks[i].order_id != 0)
                            {
                                OrderOnline tempOrder = _context.OrderOnlines.Find(tasks[i].order_id);
                                if (tempOrder.pay_state != 0)
                                {
                                    canOrder = false;
                                }
                            }
                        }
                    }
                    else
                    {
                        MaintainLive task = _context.MaintainLives.Find(id);
                        OrderOnline tempOrder = _context.OrderOnlines.Find(task.order_id);
                        if (tempOrder.pay_state != 0)
                        {
                            canOrder = false;
                        }
                        tasks.Add(task);
                    }
                    if (!canOrder)
                    {
                        return null;
                    }
                    double totalPrice = 0;
                    if (tasks.Count > 0)
                    {
                        if (tasks[0].ticket_code != null)
                        {
                            ticketCode = tasks[0].ticket_code.Trim();
                        }
                        for (int i = 0; i < tasks.Count; i++)
                        {
                            int productId = tasks[i].confirmed_product_id;


                            if (productId > 0)
                            {
                                Product product = _context.Product.Find(productId);
                                OrderOnlineDetail detail = new OrderOnlineDetail()
                                {
                                    OrderOnlineId = 0,
                                    product_id = productId,
                                    count = 1,
                                    product_name = product.name,
                                    price = product.sale_price
                                };
                                totalPrice = totalPrice + product.sale_price;
                                details.Add(detail);
                            }

                            if (tasks[i].confirmed_additional_fee != 0)
                            {
                                int count = 0;
                                productId = tasks[i].AddtionalFeeProductId;
                                Product product = _context.Product.Find(productId);
                                count = (int)(tasks[i].confirmed_additional_fee / product.sale_price);
                                totalPrice = totalPrice + count * product.sale_price;
                            }



                        }
                        if (totalPrice == 0)
                        {
                            return null;
                        }
                        else
                        {
                            string openId = user.miniAppOpenId;
                            string cellNumber = "";
                            string name = "";
                            if (openId.Trim().Equals(""))
                            {
                                openId = user.officialAccountOpenId;
                                cellNumber = user.officialAccountUser.cell_number;
                                name = user.officialAccountUser.nick;
                            }
                            else
                            {
                                cellNumber = user.miniAppUser.cell_number;
                                name = user.miniAppUser.nick;
                            }
                            OrderOnline order = new OrderOnline()
                            {
                                type = "服务",
                                open_id = openId,
                                cell_number = cellNumber,
                                name = name,
                                pay_method = "微信",
                                order_price = totalPrice,
                                order_real_pay_price = totalPrice,
                                pay_state = 0,
                                shop = tasks[0].shop.Trim(),
                                out_trade_no = "",
                                ticket_code = ticketCode.Trim(),
                                code = ""
                            };
                            _context.OrderOnlines.Add(order);
                            _context.SaveChanges();
                            if (order.id == 0)
                            {
                                return null;
                            }
                            foreach (OrderOnlineDetail d in details)
                            {
                                d.OrderOnlineId = order.id;
                                _context.OrderOnlineDetails.Add(d);
                                _context.SaveChanges();
                            }
                            OrderOnline orderRet = new OrderOnline()
                            {
                                id = order.id,
                                order_real_pay_price = order.order_real_pay_price
                            };
                            if (order.id > 0)
                            {
                                foreach (MaintainLive task in tasks)
                                {
                                    task.order_id = order.id;
                                    task.open_id = openId.Trim();
                                    _context.Entry(task).State = EntityState.Modified;
                                    _context.SaveChanges();
                                }
                            }
                            return orderRet;

                        }


                    }
                    else
                    {
                        return null;
                    }

                }
            }
            else
            {
                return null;
            }

        }

        /*
        // GET: api/MaintainLive
        [HttpGet]
        public async Task<ActionResult<IEnumerable<MaintainLive>>> GetMaintainLives()
        {
            return await _context.MaintainLives.ToListAsync();
        }

        // GET: api/MaintainLive/5
        [HttpGet("{id}")]
        public async Task<ActionResult<MaintainLive>> GetMaintainLive(int id)
        {
            var maintainLive = await _context.MaintainLives.FindAsync(id);

            if (maintainLive == null)
            {
                return NotFound();
            }

            return maintainLive;
        }

        // PUT: api/MaintainLive/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutMaintainLive(int id, MaintainLive maintainLive)
        {
            if (id != maintainLive.id)
            {
                return BadRequest();
            }

            _context.Entry(maintainLive).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!MaintainLiveExists(id))
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

        // POST: api/MaintainLive
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<MaintainLive>> PostMaintainLive(MaintainLive maintainLive)
        {
            _context.MaintainLives.Add(maintainLive);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetMaintainLive", new { id = maintainLive.id }, maintainLive);
        }

        // DELETE: api/MaintainLive/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMaintainLive(int id, string sessionKey)
        {
            sessionKey = System.Web.HttpUtility.UrlDecode(sessionKey);
            if (sessionKey.Trim().Equals(""))
            {
                return NoContent();
            }
            var maintainLive = await _context.MaintainLives.FindAsync(id);
            if (maintainLive == null)
            {
                return NotFound();
            }

            _context.MaintainLives.Remove(maintainLive);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */

        private bool MaintainLiveExists(int id)
        {
            return _context.MaintainLives.Any(e => e.id == id);
        }

        private bool MaintainLiveBatchExists(int batchId)
        {
            return _context.MaintainLives.Any(e => e.batch_id == batchId);
        }
    }
}
