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
namespace SnowmeetApi.Controllers
{
    [Route("[controller]/[action]")]
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
