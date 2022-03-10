using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Ticket;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Card;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Product;
using wechat_miniapp_base.Models;
namespace SnowmeetApi.Controllers
{
    [Route("[controller]/[action]")]
    [ApiController]
    public class SummerMaintainController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;
        private IConfiguration wholeConfig;
        

        public string _appId = "";
        public SummerMaintainController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            wholeConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            
        }


        //NonAction Methods
        [NonAction]
        public async Task<ActionResult<int>> Create(SummerMaintain summerMaintain)
        {
            await _context.SummerMaintain.AddAsync(summerMaintain);
            await _context.SaveChangesAsync();
            return summerMaintain.id;
        }

        [NonAction]
        public async Task<ActionResult<int>> CreateOrder(SummerMaintain summerMaintain)
        {
            int orderId = 0;
            if (summerMaintain.id <= 0 || summerMaintain.order_id != 0)
            {
                return NoContent();
            }
            int productId = 144;
            if (summerMaintain.service.Trim().Equals("代取回寄"))
            {
                productId = 145;
            }
            Product product = await _context.Product.FindAsync(productId);
            List<OrderOnlineDetail> details = new List<OrderOnlineDetail>();
            OrderOnlineDetail detail = new OrderOnlineDetail()
            {
                OrderOnlineId = 0,
                product_id = productId,
                count = 1,
                product_name = product.name,
                price = product.sale_price
            };
            double totalPrice = product.sale_price;
            details.Add(detail);

            OrderOnline orderNew = new OrderOnline()
            {
                type = "服务卡",
                open_id = summerMaintain.open_id.Trim(),
                cell_number = summerMaintain.owner_cell.Trim(),
                name = summerMaintain.owner_name.Trim(),
                pay_method = summerMaintain.pay_method.Trim(),
                order_price = totalPrice,
                order_real_pay_price = totalPrice,
                pay_state = 0,
                shop = "万龙",
                out_trade_no = "",
                ticket_code = "",
                code = ""
            };
            _context.OrderOnlines.Add(orderNew);
            await _context.SaveChangesAsync();
            summerMaintain.order_id = orderNew.id;
            orderId = orderNew.id;
            foreach (OrderOnlineDetail d in details)
            {
                d.OrderOnlineId = orderId;
                _context.OrderOnlineDetails.Add(d);
                await _context.SaveChangesAsync();
            }
            _context.Entry(summerMaintain).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return orderId;
        }

        [NonAction]
        public async Task<ActionResult<string>> SetPaySuccess(SummerMaintain summerMaintain)
        {
            string code = "";
            int productId = 144;
            string type = "服务卡";
            string openId = summerMaintain.open_id.Trim();
            if (summerMaintain.order_id != 0)
            {
                OrderOnline order = await _context.OrderOnlines.FindAsync(summerMaintain.order_id);
                type = order.type.Trim();
                var detailArr = await _context.OrderOnlineDetails.Where(d => d.OrderOnlineId == order.id).ToListAsync();
                if (detailArr.Count > 0)
                {
                    productId = detailArr[0].product_id;
                }
                order.pay_state = 1;
                order.pay_time = DateTime.Now;
                _context.Entry(order).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            CardController cardController = new CardController(_context, wholeConfig);
            code = cardController.CreateCard(type.Trim());
            Card card = _context.Card.Find(code);
            card.product_id = productId;
            card.is_package = 0;
            card.is_ticket = 0;
            card.owner_open_id = openId.Trim();
            card.use_memo = "";
            _context.Entry<Card>(card).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            summerMaintain.code = code;
            summerMaintain.state = "养护中";
            _context.Entry(summerMaintain).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return code;
        }

        [NonAction]
        public async Task<ActionResult<int>> AsignOpenId(SummerMaintain summerMaintainm, string openId)
        {
            int ret = 0;
            int orderId = summerMaintainm.order_id;
            if (orderId == 0)
            {
                return 0;
            }
            OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
            summerMaintainm.open_id = openId.Trim();
            order.open_id = openId.Trim();
            _context.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            _context.Entry(summerMaintainm).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            ret = 1;
            return ret;
        }

        //Web API
        [HttpGet("{id}")]
        public async Task<ActionResult<bool>> SetBlankOpenId(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            SummerMaintain sm = await _context.SummerMaintain.FindAsync(id);
            if (!sm.open_id.Trim().Equals(""))
            {
                return NotFound();
            }
            int orderId = sm.order_id;
            if (orderId != 0)
            {
                OrderOnline order = await _context.OrderOnlines.FindAsync(sm.order_id);
                if (order == null || !order.open_id.Trim().Equals(""))
                {
                    return NotFound();
                }
            }
            await SetPaySuccess(sm);
            return true;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<bool>> SetOpenId(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            string openId = user.miniAppOpenId.Trim();
            if (openId.Trim().Equals(""))
            {
                return NoContent();
            }
            SummerMaintain sm = await _context.SummerMaintain.FindAsync(id);
            
            if (sm.open_id.Trim().Equals(""))
            {
                if (sm.order_id == 0)
                {
                    sm.open_id = openId.Trim();
                    _context.Entry(sm).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    if (!sm.pay_method.Trim().Equals("微信"))
                    {
                        await SetPaySuccess(sm);
                    }
                    return true;
                }
                else
                {
                    OrderOnline order = await _context.OrderOnlines.FindAsync(sm.order_id);
                    if (order == null)
                    {
                        return NotFound();
                    }
                    if (order.open_id.Trim().Equals(""))
                    {
                        order.open_id = openId.Trim();
                        _context.Entry(order).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                        sm.open_id = openId.Trim();
                        _context.Entry(sm).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                        if (!sm.pay_method.Trim().Equals("微信"))
                        {
                            await SetPaySuccess(sm);
                        }
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            else
            {
                return false;
            }
            
        }

        [HttpPost]
        public async Task<ActionResult<int>> ReceptWithOthersPayment(SummerMaintain summerMaintain)
        {
            string sessionKey = Util.UrlDecode(summerMaintain.oper_open_id);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            summerMaintain.oper_open_id = user.miniAppOpenId.Trim();

            string cell = summerMaintain.owner_cell.Trim();
            if (cell.Trim().Equals(""))
            {
                cell = summerMaintain.cell.Trim();
            }
            string openId = "";
            if (!cell.Trim().Equals(""))
            {
                MiniAppUserController userCtrl = new MiniAppUserController(_context, wholeConfig);
                var r = await userCtrl.GetOpenIdByCell(cell);
                openId = r.Value.ToString().Trim();
            }

            summerMaintain.open_id = openId.Trim();
            if (summerMaintain.id == 0)
            {
                _context.SummerMaintain.Add(summerMaintain);
                await _context.SaveChangesAsync();
            }
            else
            {
                _context.Entry(summerMaintain).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            
            if (!summerMaintain.pay_method.Trim().Equals("招待") && summerMaintain.id > 0)
            {
                await CreateOrder(summerMaintain);
            }

            if (!openId.Trim().Equals(""))
            {
                await SetPaySuccess(summerMaintain);
            }

            return summerMaintain.id;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<SummerMaintain>>> GetAll(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (!user.isAdmin)
            {
                return NotFound();
            }
            return await _context.SummerMaintain.Where(s => !s.code.Trim().Equals("")).OrderByDescending(s => s.id).ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<SummerMaintain>> UpdateOwnerInfo(int id, string name, string cell, string sessionKey)
        {
            UnicUser._context = _context;
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (!user.isAdmin)
            {
                return NotFound();
            }
            SummerMaintain summerMaintain = await _context.SummerMaintain.FindAsync(id);
            summerMaintain.owner_name = Util.UrlDecode(name).Trim();
            summerMaintain.owner_cell = Util.UrlDecode(cell).Trim();
            _context.Entry(summerMaintain).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return summerMaintain;

        }

        [HttpGet("{id}")]
        public async Task<ActionResult<SummerMaintain>> UpdateOwnerAndDeliverInfo(int id, string ownerName, string ownerCell,
            bool keep, string deliverName, string deliverCell, string address, string sessionKey)
        {
            UnicUser._context = _context;
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (!user.isAdmin)
            {
                return NotFound();
            }
            if (keep)
            {
                deliverName = "";
                deliverCell = "";
                address = "";
            }
            SummerMaintain summerMaintain = await _context.SummerMaintain.FindAsync(id);
            summerMaintain.owner_name = Util.UrlDecode(ownerName).Trim();
            summerMaintain.owner_cell = Util.UrlDecode(ownerCell).Trim();
            summerMaintain.keep = keep ? "是" : "否";
            summerMaintain.contact_name = deliverName.Trim();
            summerMaintain.cell = deliverCell.Trim();
            summerMaintain.address = address.Trim();
            _context.Entry(summerMaintain).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return summerMaintain;

        }

        [HttpGet("{id}")]
        public async Task<ActionResult<SummerMaintain>> GetSummerMaintain(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            SummerMaintain summerMaintain = await _context.SummerMaintain.FindAsync(id);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (user.isAdmin || user.miniAppOpenId.Trim().Equals(summerMaintain.open_id.Trim()) 
                || summerMaintain.open_id.Trim().Equals(""))
            {
                return summerMaintain;
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<WepayOrder>> Pay(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            SummerMaintain summerMaintain = await _context.SummerMaintain.FindAsync(id);
            if (summerMaintain == null
                || (!summerMaintain.open_id.Trim().Equals("") && !summerMaintain.open_id.Trim().Equals(user.miniAppOpenId))
                || !summerMaintain.code.Trim().Equals(""))
            {
                return NotFound();
            }
            int orderId = summerMaintain.order_id;
            if (orderId != 0)
            {
                OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
                if (order == null || !order.open_id.Trim().Equals(user.miniAppOpenId.Trim()) || order.pay_state > 0)
                {
                    return NotFound();
                }
            }
            summerMaintain.open_id = user.miniAppOpenId.Trim();
            _context.Entry(summerMaintain).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            int productId = 144;
            if (summerMaintain.service.Trim().Equals("代取回寄"))
            {
                productId = 145;
            }

            if (orderId == 0)
            {
                Product product = await _context.Product.FindAsync(productId);
                List<OrderOnlineDetail> details = new List<OrderOnlineDetail>();
                OrderOnlineDetail detail = new OrderOnlineDetail()
                {
                    OrderOnlineId = 0,
                    product_id = productId,
                    count = 1,
                    product_name = product.name,
                    price = product.sale_price
                };
                double totalPrice = product.sale_price;
                details.Add(detail);

                OrderOnline orderNew = new OrderOnline()
                {
                    type = "服务卡",
                    open_id = user.miniAppOpenId,
                    cell_number = user.miniAppUser.cell_number.Trim(),
                    name = user.miniAppUser.nick.Trim(),
                    pay_method = "微信",
                    order_price = totalPrice,
                    order_real_pay_price = totalPrice,
                    pay_state = 0,
                    shop = "万龙",
                    out_trade_no = "",
                    ticket_code = "",
                    code = ""
                };
                _context.OrderOnlines.Add(orderNew);
                await _context.SaveChangesAsync();
                summerMaintain.order_id = orderNew.id;
                orderId = orderNew.id;
                foreach (OrderOnlineDetail d in details)
                {
                    d.OrderOnlineId = orderId;
                    _context.OrderOnlineDetails.Add(d);
                    await _context.SaveChangesAsync();
                }
                _context.Entry(summerMaintain).State = EntityState.Modified;
                await _context.SaveChangesAsync();

            }

            if (orderId == 0)
            {
                return NoContent();
            }

            OrderOnlinesController orderController = new OrderOnlinesController(_context, wholeConfig);
            return await orderController.Pay(orderId, sessionKey);
        }

        [HttpPost]
        public async Task<ActionResult<int>> Recept(SummerMaintain summerMaintain)
        {
            string sessionKey = summerMaintain.oper_open_id.Trim();
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (!user.isAdmin)
            {
                return NotFound();
            }
            summerMaintain.oper_open_id = user.miniAppOpenId.Trim();


            switch (summerMaintain.pay_method)
            {
                case "微信":
                    break;
                default:
                    string ownerCell = summerMaintain.owner_cell.Trim();
                    var users = await _context.MiniAppUsers.Where(u => u.cell_number.Trim().Equals(ownerCell.Trim())).ToListAsync();
                    string openId = "";

                    if (users.Count > 0)
                    {
                        openId = users[0].open_id.Trim();
             
                    }
                    summerMaintain.open_id = openId.Trim();
                    if (!summerMaintain.pay_method.Trim().Equals("招待"))
                    {
                        PlaceOrderInner(summerMaintain);
                    }
                    if (!openId.Trim().Equals(""))
                    {
                        //Generate card code.
                    }

                    break;
            }
            await _context.SummerMaintain.AddAsync(summerMaintain);
            await _context.SaveChangesAsync();
            return summerMaintain.id;
        }

        public int PlaceOrderInner(SummerMaintain summerMaintain)
        {
            if (summerMaintain.order_id != 0)
            {
                return summerMaintain.order_id;
            }
            int productId = 144;
            if (summerMaintain.service.Trim().Equals("代取回寄"))
            {
                productId = 145;
            }
            Product product = _context.Product.Find(productId);
            List<OrderOnlineDetail> details = new List<OrderOnlineDetail>();
            OrderOnlineDetail detail = new OrderOnlineDetail()
            {
                OrderOnlineId = 0,
                product_id = productId,
                count = 1,
                product_name = product.name,
                price = product.sale_price
            };
            double totalPrice = product.sale_price;
            details.Add(detail);

            OrderOnline order = new OrderOnline()
            {
                type = "服务卡",
                open_id = summerMaintain.open_id.Trim(),
                cell_number = summerMaintain.owner_cell.Trim(),
                name = summerMaintain.owner_name,
                pay_method = summerMaintain.pay_method.Trim(),
                order_price = totalPrice,
                order_real_pay_price = totalPrice,
                pay_state = 0,
                shop = "万龙",
                out_trade_no = "",
                ticket_code = "",
                code = ""
            };
            _context.OrderOnlines.Add(order);
            _context.SaveChanges();
            if (order.id > 0)
            {
                foreach(OrderOnlineDetail d in details)
                {
                    d.OrderOnlineId = order.id;
                    _context.OrderOnlineDetails.Add(d);
                    _context.SaveChanges();
                }
                summerMaintain.order_id = order.id;
            }
            _context.Entry(summerMaintain).State = EntityState.Modified;
            _context.SaveChanges();
            return order.id;
        }

        public string SetPaySuccess(SummerMaintain summerMaintain)
        {
            if (!summerMaintain.code.Trim().Equals(""))
            {
                return summerMaintain.code.Trim();
            }
            if (summerMaintain.open_id.Trim().Equals(""))
            {
                return "";
            }
            int productId = 144;
            if (summerMaintain.service.Trim().Equals("代取回寄"))
            {
                productId = 145;
            }
            CardController cardController = new CardController(_context, wholeConfig);
            string code = cardController.CreateCard("服务卡");
            Card card = _context.Card.Find(code);
            card.product_id = productId;
            card.is_package = 0;
            card.is_ticket = 0;
            card.owner_open_id = summerMaintain.open_id.Trim();
            card.use_memo = "";
            _context.Entry<Card>(card).State = EntityState.Modified;
            _context.SaveChanges();

            if (summerMaintain.order_id != 0)
            {
                OrderOnline order = _context.OrderOnlines.Find(summerMaintain.order_id);
                order.pay_state = 1;
                order.pay_time = DateTime.Now;
                _context.Entry(order).State = EntityState.Modified;
                _context.SaveChanges();
            }

            return code;
        }

        public bool AssignOpen(SummerMaintain summerMaintain, string openId)
        {
            if (!summerMaintain.open_id.Trim().Equals(""))
            {
                return false;
            }
            summerMaintain.open_id = openId.Trim();
            _context.Entry(summerMaintain).State = EntityState.Modified;
            _context.SaveChanges();
            if (summerMaintain.order_id != 0)
            {
                OrderOnline order = _context.OrderOnlines.Find(summerMaintain.order_id);
                order.open_id = openId;
                
                _context.Entry(order).State = EntityState.Modified;
                _context.SaveChanges();
            }
            return true;
        }

        [HttpPost]
        public async Task<ActionResult<int>> PlaceOrder(SummerMaintain summerMaintain)
        {
            string sessionKey = summerMaintain.open_id.Trim();
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            summerMaintain.open_id = user.miniAppOpenId.Trim();
            
            int productId = 144;
            if (summerMaintain.service.Trim().Equals("代取回寄"))
            {
                productId = 145;
            }
            Product product = _context.Product.Find(productId);
            List<OrderOnlineDetail> details = new List<OrderOnlineDetail>();
            OrderOnlineDetail detail = new OrderOnlineDetail()
            {
                OrderOnlineId = 0,
                product_id = productId,
                count = 1,
                product_name = product.name,
                price = product.sale_price
            };
            double totalPrice = product.sale_price;
            details.Add(detail);

            OrderOnline order = new OrderOnline()
            {
                type = "服务卡",
                open_id = user.miniAppOpenId,
                cell_number = user.miniAppUser.cell_number.Trim(),
                name = user.miniAppUser.nick.Trim(),
                pay_method = "微信",
                order_price = totalPrice,
                order_real_pay_price = totalPrice,
                pay_state = 0,
                shop = "万龙",
                out_trade_no = "",
                ticket_code = "",
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

            summerMaintain.order_id = order.id;
            _context.SummerMaintain.Add(summerMaintain);
            await _context.SaveChangesAsync();

            return order.id;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<SummerMaintain>>> GetMySummerMaintain(string sessionKey)
        { 
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);

            List<SummerMaintain> summerMaintainList = await _context.SummerMaintain
                .Where(s => (s.open_id.Trim().Equals(user.miniAppOpenId.Trim()) && !s.state.Trim().Equals("未支付")))
                .OrderByDescending(s=>s.id).ToListAsync();


            return summerMaintainList;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<bool>> FillWaybillNo(int id, string waybillNo, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);

            bool ret = true;

            try
            {
                SummerMaintain sm = await _context.SummerMaintain.FindAsync(id);
                if (sm.open_id.Trim().Equals(user.miniAppOpenId.Trim()))
                {
                    sm.waybill_no = waybillNo;
                    _context.Entry(sm).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
                else
                {
                    ret = false;
                }
            }
            catch
            {
                ret = false;
            }
            return ret;
        }

        [HttpPut]
        public async Task<IActionResult> UpdateInfo(string sessionKey, SummerMaintain summerMaintain)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            UnicUser._context = _context;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            _context.Entry(summerMaintain).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SummerMaintainExists(summerMaintain.id))
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

        /*
        // GET: api/SummerMaintain
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SummerMaintain>>> GetSummerMaintain()
        {
            return await _context.SummerMaintain.ToListAsync();
        }

        // GET: api/SummerMaintain/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SummerMaintain>> GetSummerMaintain(int id)
        {
            var summerMaintain = await _context.SummerMaintain.FindAsync(id);

            if (summerMaintain == null)
            {
                return NotFound();
            }

            return summerMaintain;
        }

        // PUT: api/SummerMaintain/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSummerMaintain(int id, SummerMaintain summerMaintain)
        {
            if (id != summerMaintain.id)
            {
                return BadRequest();
            }

            _context.Entry(summerMaintain).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SummerMaintainExists(id))
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

        // POST: api/SummerMaintain
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<SummerMaintain>> PostSummerMaintain(SummerMaintain summerMaintain)
        {
            _context.SummerMaintain.Add(summerMaintain);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetSummerMaintain", new { id = summerMaintain.id }, summerMaintain);
        }

        // DELETE: api/SummerMaintain/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSummerMaintain(int id)
        {
            var summerMaintain = await _context.SummerMaintain.FindAsync(id);
            if (summerMaintain == null)
            {
                return NotFound();
            }

            _context.SummerMaintain.Remove(summerMaintain);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool SummerMaintainExists(int id)
        {
            return _context.SummerMaintain.Any(e => e.id == id);
        }
    }
}
