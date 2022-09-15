using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.Maintain;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class MaintainLiveController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;

        

        public MaintainLiveController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
        }

        [HttpPost]
        public async Task<ActionResult<MaintainOrder>> Recept(string sessionKey, MaintainOrder maintainOrder)
        {
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (!user.isAdmin)
            {
                return NoContent();
            }

            int orderId = 0;

            if (!maintainOrder.payOption.Trim().Equals("无需支付"))
            {
                OrderOnline order = new OrderOnline()
                {
                    id = 0,
                    open_id = maintainOrder.customerOpenId,
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
                    staff_open_id = user.miniAppOpenId.Trim()
                };
                await _context.AddAsync(order);
                await _context.SaveChangesAsync();
                orderId = order.id;
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
                await _context.AddAsync(item);
                await _context.SaveChangesAsync();
            }

            //OrderOnline order = new OrderOnline();
            
            return maintainOrder;
        }



        [HttpGet]
        public async Task<ActionResult<IEnumerable<Brand>>> GetBrand(string type)
        {
            return await _context.Brand.Where(b => b.brand_type.Trim().Equals(type.Trim()))
                .OrderBy(b=>b.brand_name).ToListAsync();
        }

        [HttpGet]
        public async Task<ActionResult<Equip[]>> GetEquip(string openId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            openId = Util.UrlDecode(openId);
            var list = await _context.MaintainLives
                .Where(m => (m.open_id.Trim().Equals(openId) && m.task_flow_num != null ))
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
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
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
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
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
        public ActionResult<OrderOnline> PlaceOrder(int id, string sessionKey)
        {
            OrderOnline order = PlaceOrderAll(id, sessionKey, "");
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
        public ActionResult<OrderOnline> PlaceOrderBatch(int id, string sessionKey)
        {
            OrderOnline order = PlaceOrderAll(id, sessionKey, "batch");
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
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
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
        private OrderOnline PlaceOrderAll(int id, string sessionKey, string idType = "batch")
        {
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
                UnicUser._context = _context;
                UnicUser user = UnicUser.GetUnicUser(sessionKey);
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
                        tasks =  _context.MaintainLives.Where(m => m.batch_id == id).ToList<MaintainLive>();
                        for (int i = 0; canOrder && i < tasks.Count; i++)
                        {
                            if (tasks[i].order_id != 0)
                            {
                                OrderOnline tempOrder =  _context.OrderOnlines.Find(tasks[i].order_id);
                                if (tempOrder.pay_state != 0)
                                {
                                    canOrder = false;
                                }
                            }
                        }
                    }
                    else
                    {
                        MaintainLive task =  _context.MaintainLives.Find(id);
                        OrderOnline tempOrder =  _context.OrderOnlines.Find(task.order_id);
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
