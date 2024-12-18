using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models;
using System.Configuration;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Card;
using Newtonsoft.Json;
using SnowmeetApi.Controllers.User;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class SkiPassController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        private MemberController _memberHelper;

        public SkiPassController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
            _memberHelper = new MemberController(context,  config);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetSkiPassDetailInfo(int id)
        {
            return await _context.Product.Where(p => p.id == id)
                .Join(_context.SkiPass, p => p.id, s => s.product_id,
                (p, s) => new
                {
                    p.id,
                    p.name,
                    p.sale_price,
                    p.deposit,
                    s.product_id,
                    s.resort,
                    s.end_sale_time,
                    s.rules,
                    s.available_days,
                    s.unavailable_days,
                    s.tags
                }).FirstAsync();
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetSkiPassProduct(string resort, DateTime date, string tags)
        {

            if ((date >= DateTime.Parse("2023-12-30") && date <= DateTime.Parse("2024-1-1") )
                || (date >= DateTime.Parse("2024-2-9") && date <= DateTime.Parse("2024-2-18")) )
            {
                tags = "节假日";
            }

            if (date.Date == DateTime.Parse("2024-2-19"))
            {
                tags = "平日";
            }

            string[] tagArr = tags == null ? new string[] { } : Util.UrlDecode(tags.Trim()).Split(',');

            


            var skiPassProdustList = await _context.Product.Where(p => (p.shop.Trim().Equals(resort.Trim()) && p.hidden == 0 && p.end_date >= DateTime.Now.Date))
                .Join(_context.SkiPass, p => p.id, s => s.product_id,
                (p, s) => new {
                    p.id,
                    p.name,
                    p.sale_price,
                    p.deposit,
                    p.sort,
                    s.product_id,
                    s.resort,
                    s.end_sale_time,
                    s.rules,
                    s.available_days,
                    s.unavailable_days,
                    s.tags
                })

                .OrderBy(p => p.sort).ToListAsync();


            for (int i = 0; i < skiPassProdustList.Count; i++)
            {
                var r = skiPassProdustList[i];
                Models.Product.SkiPass skiPass = new Models.Product.SkiPass()
                {
                    product_id = r.product_id,
                    resort = r.resort.Trim(),
                    end_sale_time = r.end_sale_time,
                    rules = r.rules,
                    available_days = r.available_days,
                    unavailable_days = r.unavailable_days,
                    tags = r.tags

                };
                if (date >= DateTime.Parse("2023-1-28") && date <= DateTime.Parse("2023-1-29"))
                {
                    if (!skiPass.DateMatch(date))
                    {
                        skiPassProdustList.RemoveAt(i);
                        i--;
                    }
                }
                else if (!skiPass.DateMatch(date) || !skiPass.TagMatch(tagArr))
                {
                    skiPassProdustList.RemoveAt(i);
                    i--;
                }


            }
            return Ok(skiPassProdustList);
        }

        [HttpGet("{productId}")]
        public async Task<ActionResult<object>> ReserveSkiPass(int productId, DateTime date, int count, string cell, string name, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            Product product = await _context.Product.FindAsync(productId);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null || product == null)
            {
                return BadRequest();
            }
            OrderOnline order = (await CreateSkiPassOrder(new Product[] { product }, user, null, date, count));

            order.cell_number = cell.Trim();
            order.name = name.Trim();
            _context.Entry(order).State = EntityState.Modified;




            //MiniAppUser miniUser = await _context.MiniAppUsers.FindAsync(order.open_id.Trim());
            Member miniUser = await _memberHelper.GetMember(order.open_id.Trim(), "wechat_mini_openid");
            if (miniUser != null)
            {
                if (miniUser.real_name.Trim().Length <= 1)
                {
                    miniUser.real_name = name;
                }
                if (miniUser.cell.Length != 11)
                {
                    await _memberHelper.UpdateDetailInfo(miniUser.id, cell.Trim(), "cell", false);
                    //miniUser.cell = cell.Trim();
                }
                _context.Entry(miniUser).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
            return order;
        }

        [HttpGet("{productId}")]
        public async Task<ActionResult<object>> PlaceSkiPassOrderNanshan(int productId, DateTime date, int count, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            Product productTicket = await _context.Product.FindAsync(productId);
            if (user == null || !user.isAdmin || productTicket == null)
            {
                return BadRequest();
            }
            Product productService = await _context.Product.FindAsync(297);
            return (await CreateSkiPassOrder(new Product[] { productTicket, productService }, null, user, date, count));
        }


        [NonAction]
        public async Task<OrderOnline> CreateSkiPassOrder(Product[] prodctArr, UnicUser? user, UnicUser? staff, DateTime date, int count)
        {
            double totalPrice = 0;
            string openId = "";
            string staffOpenId = "";
            bool needRent = false;

            if (user != null)
            {
                openId = user.miniAppOpenId.Trim();
            }
            if (staff != null)
            {
                staffOpenId = staff.miniAppOpenId.Trim();
            }
            for (int i = 0; i < prodctArr.Length; i++)
            {
                totalPrice = totalPrice + (prodctArr[i].deposit + prodctArr[i].sale_price) * count;
                if (prodctArr[i].name.IndexOf("租") >= 0)
                {
                    needRent = true;
                }
                
            }
            OrderOnline order = new OrderOnline()
            {
                type = "雪票",
                shop = prodctArr[0].shop.Trim(),
                order_price = totalPrice,
                order_real_pay_price = totalPrice,
                final_price = totalPrice,
                open_id = openId.Trim(),
                staff_open_id = staffOpenId.Trim(),
                memo = "{ \"use_date\": \"" + date.Year.ToString() + "-" + date.Month.ToString().PadLeft(2, '0') + "-" + date.Day.ToString().PadLeft(2, '0') + "\", \"rent\" : \"" + (needRent? "1" : "0") + "\"}"
            };
            await _context.AddAsync(order);
            await _context.SaveChangesAsync();
            if (order.id > 0)
            {
                for (int i = 0; i < prodctArr.Length; i++)
                {
                    OrderOnlineDetail detail = new OrderOnlineDetail()
                    {
                        OrderOnlineId = order.id,
                        product_id = prodctArr[i].id,
                        count = count,
                        product_name = prodctArr[i].name.Trim(),
                        retail_price = prodctArr[i].sale_price,
                        price = prodctArr[i].sale_price
                    };
                    await _context.AddAsync(detail);
                    await _context.SaveChangesAsync();
                }
                
            }

            OrderPayment payment = new OrderPayment()
            {
                order_id = order.id,
                pay_method = order.pay_method.Trim(),
                amount = order.final_price,
                status = "待支付",
                staff_open_id = (staff != null) ? staff.miniAppOpenId : ""
            };
            await _context.OrderPayment.AddAsync(payment);
            await _context.SaveChangesAsync();
            order.payments = new OrderPayment[] { payment };
            return order;
        }

        [NonAction]
        public async Task CreateSkiPass(OrderOnline order)
        {
            List<Models.SkiPass.SkiPass> skipassList = await _context.skiPass
                .Where(s => s.order_id == order.id).ToListAsync();
            for(int i = 0; i < skipassList.Count; i++)
            {
                Models.SkiPass.SkiPass skipass = skipassList[i];
                skipass.valid = 1;
                _context.skiPass.Entry(skipass).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
            
        }

        

        [NonAction]
        private bool SkiPassExists(int id)
        {
            return _context.SkiPass.Any(e => e.product_id == id);
        }
    }
}
