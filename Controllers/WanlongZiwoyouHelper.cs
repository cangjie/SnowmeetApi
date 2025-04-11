using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Users;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.WanLong;
using Newtonsoft.Json;
using Aop.Api.Domain;
using AlipaySDKNet.OpenAPI.Model;
using NuGet.Packaging;
using Microsoft.EntityFrameworkCore.Internal;
using static SKIT.FlurlHttpClient.Wechat.TenpayV3.Models.AddHKSubMerchantRequest.Types;
using SnowmeetApi.Models.Order;
using System.IO;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.SkiPass;
using LuqinMiniAppBase.Controllers;
using SnowmeetApi.Models;
using NPOI.XSSF.UserModel;
using NPOI.SS.UserModel;
using TencentCloud.Ocr.V20181119.Models;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class WanlongZiwoyouHelper : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _oriConfig;
        private IConfiguration _config;

        public string _appId = "";

        public string wlCustId = "6914348";

        public string wlApiKey = "3951EA4CA7BF7B679787F67E6262E1DD";

        public string dhhsCustId = "3230671";

        public string dhhsApiKey = "B71DD78AE810D436D09380505FF28120";

        public string apiKey = "";

        public string custId = "";
        public string source = "大好河山";

        public class Voucher
        {
            public string code { get; set; }
            public string qrcodeUrl { get; set; }
            public int type { get; set; }
            public int voucherStatus { get; set; }

        }

        public class ZiwoyouOrder
        {
            public int? orderId { get; set; } = null;
            public string? orderSourceId { get; set; } = null;
            public int? orderState { get; set; } = null;
            public string? orderState2 { get; set; } = null;
            public int? productNo { get; set; } = null;
            public string? productName { get; set; } = null;
            public int? num { get; set; } = null;
            public double? settlementPrice { get; set; } = null;
            public double? salePrice { get; set; } = null;
            public double? marketPrice { get; set; } = null;
            public double? orderMoney { get; set; } = null;
            public double? memOrderMoney { get; set; } = null;
            public string? sendContent1 { get; set; } = null;
            public string? sendContent2 { get; set; } = null;
            public string? sendContent3 { get; set; } = null;
            public Voucher[]? vouchers { get; set; } = null;
        }

        public class ZiwoyouQueryResult
        {
            public string msg { get; set; }
            public int state { get; set; }
            public object data { get; set; }
        }

        public class ZiwoyouQueryList
        {
            public int page { get; set; }
            public int pageCount { get; set; }
            public int resultNum { get; set; }
            public List<SnowmeetApi.Models.SkiPass.ZiwoyouListOrder> results { get; set; }
            public int size { get; set; }
            public int sizeAll { get; set; }
            public int startIndex { get; set; }
        }


        public class ZiwoyouAccountBalance
        {
            public double accountBalance { get; set; }
        }

        public class ZiwoyouCancel
        {
            public int orderId { get; set; }
            public int cancelState { get; set; }
        }
        public class ZiwoyouDailyPrice
        {
            public DateTime date { get; set; }
            public double settlementPrice { get; set; }
            public double salePrice { get; set; }
            public double marketPrice { get; set; }
            public int? num { get; set; }
            public int? seats { get; set; }
        }
        public class ZiwoyouProductDailyPrice
        {
            public int infoId { get; set; }
            public ZiwoyouDailyPrice[] ticketPrices { get; set; }
        }



        public WanlongZiwoyouHelper(ApplicationDBContext context, IConfiguration config, string source = "大好河山")
        {
            _context = context;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            apiKey = dhhsApiKey;
            custId = dhhsCustId;
            this.source = source;
            SetParam(source);
        }

        [HttpGet]
        public async Task<ActionResult<ProductQueryResult>> GetProductList(string keyword)
        {
            int pageSize = 20;
            string custId = dhhsCustId;
            string apiKey = dhhsApiKey;
            string postJson = "{\"apikey\": \"" + apiKey + "\",\t\"catIds\": \"\",\t\"cityId\": \"\",\t\"cityName\": \"\",\t\"custId\": " + custId + " ,\t\"isConfirm\": \"0\",\t\"isExpress\": \"0\",\t\"isMulti\": \"\",\t\"isPackage\": \"\",\t\"isPay\": \"\",\t\"keyWord\": \"" + keyword.Trim() + "\",\t\"orderBy\": \"\",\t\"page\": 0,\t\"productNos\": \"\",\t\"resultNum\": " + pageSize.ToString() + ",\t\"tagIds\": \"\",\t\"treeId\": \"\",\t\"viewId\": \"\"}";
            string ret = Util.GetWebContent("https://task-api.zowoyoo.com/api/thirdPaty/prod/list", postJson, "application/json");
            Console.WriteLine(postJson);
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            WebApiLog reqLog = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/prod/list"
                , "", postJson, "POST", "易龙雪聚小程序", "预订雪票", "获取产品列表");
            ProductQueryResult r = JsonConvert.DeserializeObject<ProductQueryResult>(reqLog.response.Trim());
            int pageCount = r.data.pageCount;
            for (int i = 1; i < pageCount; i++)
            {
                ProductQueryResult subR = await GetProductListByPage(keyword, i);
                if (subR.state != 1)
                {
                    continue;
                }
                SkiPassProduct[] newResults = new SkiPassProduct[r.data.results.Length + subR.data.results.Length];
                for (int j = 0; j < r.data.results.Length; j++)
                {
                    newResults[j] = r.data.results[j];
                }

                for (int j = r.data.results.Length; j < newResults.Length; j++)
                {
                    newResults[j] = subR.data.results[j - r.data.results.Length];
                }
                r.data.results = newResults;


            }
            return Ok(r);
        }



        [NonAction]
        public async Task<ProductQueryResult> GetProductListByPage(string keyword, int pageNum)
        {
            int pageSize = 20;
            string custId = dhhsCustId;
            string apiKey = dhhsApiKey;
            string postJson = "{\"apikey\": \"" + apiKey + "\",\t\"catIds\": \"\",\t\"cityId\": \"\",\t\"cityName\": \"\",\t\"custId\": " + custId + " ,\t\"isConfirm\": \"0\",\t\"isExpress\": \"0\",\t\"isMulti\": \"\",\t\"isPackage\": \"\",\t\"isPay\": \"\",\t\"keyWord\": \"" + keyword.Trim() + "\",\t\"orderBy\": \"\",\t\"page\": " + pageNum.ToString() + ",\t\"productNos\": \"\",\t\"resultNum\": " + pageSize.ToString() + ",\t\"tagIds\": \"\",\t\"treeId\": \"\",\t\"viewId\": \"\"}";
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            WebApiLog reqLog = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/prod/list"
                , "", postJson, "POST", "易龙雪聚小程序", "预订雪票", "获取产品分页列表");
            Console.WriteLine(postJson);
            ProductQueryResult r = JsonConvert.DeserializeObject<ProductQueryResult>(reqLog.response.Trim());
            return r;
        }

        [HttpGet("skiPassId")]
        public async Task<ActionResult<ZiwoyouPlaceOrderResult>> Book(int skiPassId)
        {
            Models.SkiPass.SkiPass skipass = await _context.skiPass.FindAsync(skiPassId);
            if (skipass == null)
            {
                return NotFound();
            }
            if (skipass.card_member_pick_time != null || skipass.valid != 1)
            {
                return BadRequest();
            }
            List<OrderPayment> payList = await _context.OrderPayment.Where(p => p.order_id == skipass.order_id)
                .AsNoTracking().ToListAsync();
            string outOrderNo = "";
            foreach (OrderPayment payment in payList)
            {
                if (!payment.out_trade_no.Trim().Equals(""))
                {
                    outOrderNo = payment.out_trade_no;
                    break;
                }
            }
            if (outOrderNo.Trim().Equals(""))
            {
                return NoContent();
            }

            Models.Product.SkiPass skipassProduct = await _context.SkiPass.FindAsync(skipass.product_id);
            if (skipassProduct.source.Trim().Equals("大好河山"))
            {
                apiKey = dhhsApiKey;
                custId = dhhsCustId;
            }
            else
            {
                apiKey = wlApiKey;
                custId = wlCustId;
            }
            ZiwoyouPlaceOrderResult bookResult = await PlaceOrder(skipassProduct.third_party_no, skipass.contact_name, skipass.contact_cell, skipass.contact_id_type,
                skipass.contact_id_no, skipass.count, (DateTime)skipass.reserve_date, "", outOrderNo);
            skipass.reserve_no = bookResult.data.orderId.Trim();
            return Ok(bookResult);
        }

        [NonAction]
        public async Task<ZiwoyouPlaceOrderResult> PlaceOrder(string productNo, string name, string cell,
            string idType, string idNo, int count, DateTime date, string memo, string orderId)
        {
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            string postData = "{\n\t\"apikey\": \"" + apiKey
                + "\",\n\t\"custId\": " + custId + " ,\n\t\"infoId\": " + productNo
                + ",\n\t\"isSend\": \"1\",\n\t\"linkMan\": \"" + name + "\", \"linkCreditType\": 0, \"linkCreditNo\": \"" + idNo.Trim()
                + "\",\n\t\"linkPhone\": \"" + cell + "\",\n\t\"num\": " + count.ToString()
                + ",\n\t\"orderMemo\": \"" + memo + "\",\n\t\"orderSourceId\": \"" + orderId.Trim()
                + "\",\n\t\"travelDate\": \"" + date.ToString("yyyy-MM-dd") + "\"\n}";
            string url = "https://task-api.zowoyoo.com/api/thirdPaty/order/add";
            WebApiLog reqLog = await _miniHelper.PerformRequest(url, "", postData, "POST", "易龙雪聚小程序", "预订雪票", "大好河山下单");
            ZiwoyouPlaceOrderResult r = JsonConvert.DeserializeObject<ZiwoyouPlaceOrderResult>(reqLog.response.Trim());
            return r;
        }
        [NonAction]
        public async Task<PayResult> Pay(int orderId)
        {
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim() + ",\"orderId\": " + orderId.ToString() + "}";
            WebApiLog reqLog = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/order/pay",
                "", postData, "POST", "易龙雪聚小程序", "预订雪票", "大好河山支付");
            PayResult p = JsonConvert.DeserializeObject<PayResult>(reqLog.response);
            return p;

        }

        [HttpGet]
        public async Task<ActionResult<string>> GetProductDetail(int productId)
        {
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim() + ",\"productNo\": " + productId.ToString() + "}";
            WebApiLog log = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/prod/detail",
                 "", postData, "POST", "易龙雪聚小程序", "预订雪票", "大好河山产品查询");

            return Ok(log.response);
        }

        [HttpGet]
        public async Task<ActionResult<ZiwoyouProductDailyPrice>> GetProductPrice(int productId, DateTime date)
        {
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim() + ",\"productNo\": "
                + productId.ToString() + ", \"travelDate\": \"" + date.ToString("yyyy-MM-dd") + "\" }";
            WebApiLog log = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/prod/price",
                 "", postData, "POST", "易龙雪聚小程序", "预订雪票", "大好河山产品价格查询");
            ZiwoyouQueryResult r = JsonConvert.DeserializeObject<ZiwoyouQueryResult>(log.response);
            ZiwoyouProductDailyPrice price = JsonConvert.DeserializeObject<ZiwoyouProductDailyPrice>(r.data.ToString());
            return price;
        }

        [HttpGet]
        public async Task<ActionResult<ZiwoyouOrder>> GetOrder(int orderId)
        {
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim()
                + ",\"orderId\": " + orderId.ToString() + "}";
            WebApiLog log = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/order/detail",
                 "", postData, "POST", "易龙雪聚小程序", "预订雪票", "大好河山订单查询");
            ZiwoyouQueryResult r = JsonConvert.DeserializeObject<ZiwoyouQueryResult>(log.response.Trim());
            ZiwoyouOrder order = JsonConvert.DeserializeObject<ZiwoyouOrder>(r.data.ToString());

            return Ok(order);

        }

        [NonAction]
        public async Task<ZiwoyouQueryResult> CancelOrder(int orderId)
        {
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            ZiwoyouOrder order = (ZiwoyouOrder)((OkObjectResult)(await GetOrder(orderId)).Result).Value;
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim()
                + ",\"orderId\": " + orderId.ToString() + ", \"cancelNum\": " + order.num.ToString() + "}";
            WebApiLog log = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/order/cancel",
                 "", postData, "POST", "易龙雪聚小程序", "预订雪票", "大好河山订单取消");
            ZiwoyouQueryResult r = JsonConvert.DeserializeObject<ZiwoyouQueryResult>(log.response);
            ZiwoyouCancel cancel = JsonConvert.DeserializeObject<ZiwoyouCancel>(r.data.ToString());
            r.data = cancel;
            return r;
        }

        [NonAction]
        public async Task<Models.Product.SkiPass> GetSkipassProductByCode(string code)
        {
            var l = await _context.SkiPass.Where(s => s.third_party_no.Trim().Equals(code.Trim()))
                .AsNoTracking().ToListAsync();
            if (l == null || l.Count <= 0)
            {
                return null;
            }
            Models.Product.SkiPass skipass = l[0];
            skipass.product = await _context.Product.FindAsync(skipass.product_id);
            return skipass;

        }

        [HttpGet]
        public async Task UpdateSkipassProductPrice()
        {
            var l = await _context.SkiPass.Where(s => s.third_party_no != null).AsNoTracking().ToListAsync();
            //var l = await _context.SkiPass.Where(s => s.third_party_no.Equals("80018099")).AsNoTracking().ToListAsync();
            foreach (var item in l)
            {
                try
                {
                    string priceStr = Util.GetWebContent("https://mini.snowmeet.top/core/WanlongZiwoyouHelper/GetProductPrice?productId=" + item.third_party_no.Trim() + "&date=" + DateTime.Now.ToString("yyyy-MM-dd"));//+ DateTime.Now.ToString("yyyy-MM-dd"));
                    //string priceStr = Util.GetWebContent("https://mini.snowmeet.top/core/WanlongZiwoyouHelper/GetProductPrice?productId=" + item.third_party_no.Trim() + "&date=2025-02-01"); //+ DateTime..ToString("yyyy-MM-dd") );//+ DateTime.Now.ToString("yyyy-MM-dd"));
                    ZiwoyouProductDailyPrice price = JsonConvert.DeserializeObject<ZiwoyouProductDailyPrice>(priceStr);
                    ZiwoyouDailyPrice[] priceArr = price.ticketPrices;
                    if (priceArr.Length == 0)
                    {
                        continue;
                    }
                    DateTime startDate = priceArr[0].date.Date;
                    var oriPriceList = await _context.skipassDailyPrice
                        .Where(s => s.third_party_id.Trim().Equals(price.infoId.ToString()) && s.valid == 1 && s.reserve_date.Date >= startDate).ToListAsync();
                    for (int i = 0; i < priceArr.Length; i++)
                    {
                        var priceObj = priceArr[i];
                        bool changed = false;
                        bool exists = false;
                        double revenu = 0;
                        string dayType = "";
                        for (int j = 0; j < oriPriceList.Count; j++)
                        {
                            var oriPrice = oriPriceList[j];
                            if (oriPrice.reserve_date.Date == priceObj.date.Date)
                            {
                                exists = true;
                                if (oriPrice.settlementPrice != priceObj.settlementPrice)
                                {
                                    changed = true;
                                    oriPrice.valid = 0;
                                    revenu = oriPrice.deal_price - oriPrice.settlementPrice;
                                    dayType = oriPrice.day_type.Trim();
                                    _context.skipassDailyPrice.Entry(oriPrice).State = EntityState.Modified;

                                }
                                break;
                            }
                        }
                        if (changed || !exists)
                        {
                            if (dayType.Trim().Equals(""))
                            {
                                dayType = ((priceObj.date.Date.DayOfWeek == DayOfWeek.Sunday || priceObj.date.Date.DayOfWeek == DayOfWeek.Saturday) ? "周末" : "平日");
                            }
                            if (revenu == 0)
                            {
                                try
                                {
                                    SkipassDailyPrice lastPrice = await _context.skipassDailyPrice
                                        .Where(s => (s.product_id == item.product_id && s.day_type.Trim().Equals(dayType.Trim())
                                        && s.valid == 1)).OrderByDescending(s => s.reserve_date).FirstAsync();
                                    if (lastPrice != null)
                                    {
                                        revenu = lastPrice.deal_price - lastPrice.settlementPrice;
                                    }
                                    else
                                    {

                                    }
                                }
                                catch
                                {
                                    revenu = 20;
                                }
                            }
                            SkipassDailyPrice newPrice = new SkipassDailyPrice()
                            {
                                product_id = item.product_id,
                                third_party_id = item.third_party_no,
                                salePrice = priceObj.salePrice,
                                settlementPrice = priceObj.settlementPrice,
                                marketPrice = priceObj.marketPrice,
                                valid = 1,
                                deal_price = priceObj.settlementPrice + revenu,
                                reserve_date = priceObj.date.Date,
                                day_type = ((priceObj.date.Date.DayOfWeek == DayOfWeek.Sunday || priceObj.date.Date.DayOfWeek == DayOfWeek.Saturday) ? "周末" : "平日")
                            };
                            await _context.skipassDailyPrice.AddAsync(newPrice);
                            await _context.SaveChangesAsync();
                        }

                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }



        [HttpGet]
        public async Task UpdateSkipassProduct(string keyword)
        {
            ProductQueryResult originProductInfo = (ProductQueryResult)((OkObjectResult)(await GetProductList(keyword)).Result).Value;
            for (int i = 0; i < originProductInfo.data.results.Length; i++)
            {
                SkiPassProduct skipassProduct = originProductInfo.data.results[i];
                Models.Product.SkiPass skipass = await GetSkipassProductByCode(skipassProduct.productNo);
                if (skipass != null)
                {
                    skipass.product.market_price = skipassProduct.salePrice;
                    skipass.product.cost = skipassProduct.settlementPrice;
                    _context.Entry<Models.Product.Product>(skipass.product).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
                else
                {
                    Models.Product.Product p = new Models.Product.Product()
                    {
                        id = 0,
                        name = skipassProduct.productName.Trim(),
                        sale_price = skipassProduct.settlementPrice + 15,
                        market_price = skipassProduct.salePrice,
                        cost = skipassProduct.settlementPrice,
                        type = "雪票",
                        shop = "崇礼旗舰店",
                        hidden = 0,
                        start_date = DateTime.Parse("2024-10-1"),
                        end_date = DateTime.Parse("2025-6-1"),
                        intro = skipassProduct.orderDesc.Trim(),
                        principal = ""

                    };
                    await _context.Product.AddAsync(p);
                    await _context.SaveChangesAsync();
                    Models.Product.SkiPass ski = new Models.Product.SkiPass()
                    {
                        product_id = p.id,
                        resort = keyword.Trim(),
                        rules = skipassProduct.orderDesc.Trim(),
                        source = this.source.Trim(),
                        third_party_no = skipassProduct.productNo
                    };
                    await _context.SkiPass.AddAsync(ski);
                    await _context.SaveChangesAsync();

                }
            }

        }


        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetProductById(int id)
        {
            var l = await _context.SkiPass.Include(s => s.dailyPrice)
                .Join(_context.Product, s => s.product_id, p => p.id,
                (s, p) => new { s.product_id, s.resort, s.rules, s.source, s.third_party_no, p.name, p.shop, p.sale_price, p.market_price, p.cost, p.type, s.dailyPrice })
                .Where(p => p.type.Trim().Equals("雪票") && p.product_id == id
                && p.third_party_no != null)
                .AsNoTracking().ToListAsync();
            if (l == null || l.Count <= 0)
            {
                return NotFound();
            }
            return Ok(l[0]);
        }



        [HttpPost]
        public ActionResult CallBack()
        {
            return Ok();
        }
        [HttpGet]
        public async Task<ActionResult<ZiwoyouQueryList>> GetOrderListByPage(DateTime start, DateTime end, int page = 0)
        {
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            string postData = "{\"apikey\": \"" + apiKey + "\", \"custId\": " + custId + ", \"resultNum\": 20, \"page\": " + page.ToString()
                + ", \"startDate\": \"" + start.ToString("yyyy-MM-dd HH:mm:ss") + "\", \"endDate\": \"" + end.ToString("yyyy-MM-dd HH:mm:ss") + "\" }";
            WebApiLog log = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/order/list",
                 "", postData, "POST", "易龙雪聚小程序", "预订雪票", "大好河山订单获取分页列表");
            ZiwoyouQueryResult r = JsonConvert.DeserializeObject<ZiwoyouQueryResult>(log.response);
            ZiwoyouQueryList l = JsonConvert.DeserializeObject<ZiwoyouQueryList>(r.data.ToString());
            return Ok(l);
        }
        [HttpGet]
        public async Task UpdateZiwoyouOrderHourly()
        {
            await UpdateZiwoyouOrder(DateTime.Now.Date.AddDays(-7), DateTime.Now.Date);
        }

        [HttpGet]
        public async Task UpdateZiwoyouOrder(DateTime start, DateTime end)
        {
            int page = 0;
            string ret = Util.GetWebContent("https://mini.snowmeet.top/core/WanlongZiwoyouHelper/GetOrderListByPage?start=" + start.ToString("yyyy-MM-dd")
                + "&end=" + end.ToString("yyyy-MM-dd") + "&page=" + page.ToString());
            ZiwoyouQueryList list = JsonConvert.DeserializeObject<ZiwoyouQueryList>(ret);
            List<ZiwoyouOrder> orders = new List<ZiwoyouOrder>();
            int pageCount = list.pageCount;
            for (; ; )
            {
                for (int i = 0; i < list.results.Count; i++)
                {
                    ZiwoyouListOrder order = list.results[i];
                    ZiwoyouListOrder dbOrder = await _context.ziwoyouOrder.FindAsync(order.orderId);
                    if (dbOrder == null)
                    {
                        order.create_date = DateTime.Now;
                        await _context.ziwoyouOrder.AddAsync(order);
                    }
                    else
                    {
                        //dbOrder = order;
                        dbOrder.cancelDate = order.cancelDate;
                        dbOrder.orderState = order.orderState;
                        dbOrder.orderState2 = order.orderState2;
                        dbOrder.orderMemo = order.orderMemo;
                        dbOrder.update_date = DateTime.Now;
                        _context.ziwoyouOrder.Entry(dbOrder).State = EntityState.Modified;

                    }
                    await _context.SaveChangesAsync();
                }
                page++;
                if (page >= pageCount)
                {
                    break;
                }
                else
                {
                    ret = ret = Util.GetWebContent("https://mini.snowmeet.top/core/WanlongZiwoyouHelper/GetOrderListByPage?start=" + start.ToShortDateString()
                        + "&end=" + end.ToShortDateString() + "&page=" + page.ToString());
                    list = JsonConvert.DeserializeObject<ZiwoyouQueryList>(ret);
                }
            }
        }


        [HttpGet]
        public async Task<ActionResult<double>> GetBalance()
        {
            SetParam(source);
            string postData = "{\"apikey\": \"" + apiKey + "\", \"custId\": " + custId + "}";
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            WebApiLog reqLog = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/order/balance",
                "", postData.Trim(), "POST", "易龙雪聚小程序", "预订雪票", "查询大好河山储值");
            try
            {
                ZiwoyouQueryResult r = JsonConvert.DeserializeObject<ZiwoyouQueryResult>(reqLog.response.Trim());
                ZiwoyouAccountBalance b = JsonConvert.DeserializeObject<ZiwoyouAccountBalance>(r.data.ToString());
                return Ok(b.accountBalance);
            }
            catch
            {
                return Ok(double.Parse("0"));
            }
        }

        [HttpGet]
        public async Task<ActionResult<List<ZiwoyouListOrder>>> GetOrderBills(DateTime start, DateTime end)
        {
            var l = await _context.ziwoyouOrder
            .Include(z => z.skipasses)
                .ThenInclude(s => s.order)
                    .ThenInclude(o => o.paymentList.Where(p => p.status.Equals("支付成功")))
                        .ThenInclude(p => p.refunds.Where(r => (r.state == 1 || r.refund_id.Trim().Equals(""))))
            .Include(z => z.skipasses)
                .ThenInclude(s => s.order)
                    .ThenInclude(o => o.paymentList.Where(p => p.status.Equals("支付成功")))
                        .ThenInclude(p => p.shares)
                            .ThenInclude(s => s.kol)


            .Where(z => (z.orderDate.Date >= start.Date && z.orderDate.Date <= end.Date))
            .ToListAsync();
            return Ok(l.OrderByDescending(l => l.orderDate).ToList());
        }
        [HttpGet]
        public async Task ExportOrderBills()
        {
            List<ZiwoyouListOrder> orderList = (List<ZiwoyouListOrder>)((OkObjectResult)(await GetOrderBills(DateTime.Parse("2024-10-01"), DateTime.Parse("2025-05-01"))).Result).Value;
            int maxPaymentNum = 1;
            int maxRefundNum = 0;
            for (int i = 0; i < orderList.Count; i++)
            {
                ZiwoyouListOrder order = orderList[i];
                for (int j = 0; j < order.skipasses.Count; j++)
                {
                    if (order.skipasses[j].order != null)
                    {
                        maxPaymentNum = Math.Max(maxPaymentNum, order.skipasses[j].order.paymentList.Count);

                        if (order.skipasses[j].order.refundList != null)
                        {
                            maxRefundNum = Math.Max(maxRefundNum, order.skipasses[j].order.refundList.Count);
                        }

                    }
                }
            }
            List<string> head = [
                "序号","下单渠道" ,"自我游单号", "日期" ,"时间", "名称", "数量", "状态", "结算价", "收款",
                "退款", "利润", "联系人", "联系电话", "客服", "分账金额","分账状态", "分账时间", "分账单号"];
            int commonFieldsNum = head.Count;
            string[] headPayment = ["收款门店", "支付方式", "收款单号", "收款金额", "收款日期", "收款时间"];
            string[] headRefund = ["退款单号", "退款金额", "退款日期", "退款时间"];
            //List<string> head = hea
            for (int i = 0; i < maxPaymentNum; i++)
            {
                for (int j = 0; j < headPayment.Length; j++)
                {
                    head.Add(headPayment[j] + (i + 1).ToString());
                }
            }
            for (int i = 0; i < maxRefundNum; i++)
            {
                for (int j = 0; j < headRefund.Length; j++)
                {
                    head.Add(headRefund[j] + (i + 1).ToString());
                }
            }
            string nullStr = "【-】";


            XSSFWorkbook workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("大好河山雪票");
            IDataFormat format = workbook.CreateDataFormat();
            IFont headFont = workbook.CreateFont();
            headFont.Color = NPOI.HSSF.Util.HSSFColor.White.Index;
            headFont.IsBold = true;


            ICellStyle headStyle = workbook.CreateCellStyle();
            headStyle.Alignment = HorizontalAlignment.Center;
            headStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Black.Index;
            headStyle.FillPattern = FillPattern.SolidForeground;
            headStyle.SetFont(headFont);
            headStyle.VerticalAlignment = VerticalAlignment.Center;




            IRow headRow = sheet.CreateRow(0);
            headRow.Height = 500;

            for (int i = 0; i < head.Count; i++)
            {
                ICell headCell = headRow.CreateCell(i);
                headCell.SetCellValue(head[i].Trim());
                headCell.SetCellType(CellType.String);
                headCell.CellStyle = headStyle;
                if (i < commonFieldsNum)
                {
                    switch (i)
                    {
                        /*
                        case 0:
                            sheet.SetColumnWidth(i, 1500);
                            break;
                        */
                        default:
                            break;
                    }
                }
                else if (i < commonFieldsNum + maxPaymentNum * headPayment.Length)
                {

                    int paymentIndex = (i - commonFieldsNum) % headPayment.Length;
                    switch (paymentIndex)
                    {
                        case 0:
                            sheet.SetColumnWidth(i, 3000);
                            break;
                        case 2:
                            sheet.SetColumnWidth(i, 5500);
                            break;
                        case 4:
                        case 5:
                            sheet.SetColumnWidth(i, 3000);
                            break;

                        default:
                            break;
                    }
                }
                else
                {
                    int refundIndex = (i - commonFieldsNum - headPayment.Length * maxPaymentNum) % headRefund.Length;
                    switch (refundIndex)
                    {
                        case 0:
                            sheet.SetColumnWidth(i, 5500);
                            break;
                        case 2:
                        case 3:
                            sheet.SetColumnWidth(i, 3000);
                            break;
                        default:
                            break;
                    }
                }

            }

            IFont fontUnuse = workbook.CreateFont();
            fontUnuse.Color = NPOI.HSSF.Util.HSSFColor.Black.Index;

            IFont fontUsed = workbook.CreateFont();
            fontUsed.Color = NPOI.HSSF.Util.HSSFColor.Green.Index;

            IFont fontCanCel = workbook.CreateFont();
            fontCanCel.Color = NPOI.HSSF.Util.HSSFColor.DarkYellow.Index;

            IFont fontUnRefund = workbook.CreateFont();
            fontUnRefund.Color = NPOI.HSSF.Util.HSSFColor.DarkBlue.Index;

            IFont fontProblem = workbook.CreateFont();
            fontProblem.Color = NPOI.HSSF.Util.HSSFColor.Red.Index;

            IFont fontFromWeb = workbook.CreateFont();
            fontFromWeb.Color = NPOI.HSSF.Util.HSSFColor.Grey50Percent.Index;

            for (int i = 0; i < orderList.Count; i++)
            {
                ZiwoyouListOrder order = orderList[i];

                IRow dr = sheet.CreateRow(i + 1);
                dr.Height = 500;
                ICellStyle styleText = workbook.CreateCellStyle();
                styleText.Alignment = HorizontalAlignment.Center;
                styleText.DataFormat = format.GetFormat("General");
                ICellStyle styleMoney = workbook.CreateCellStyle();
                styleMoney.DataFormat = format.GetFormat("(¥#,##0.00);(¥#,##0.00)");
                ICellStyle styleNum = workbook.CreateCellStyle();
                styleNum.DataFormat = format.GetFormat("0");
                ICellStyle styleDate = workbook.CreateCellStyle();
                styleDate.DataFormat = format.GetFormat("yyyy-MM-dd");
                ICellStyle styleTime = workbook.CreateCellStyle();
                styleTime.DataFormat = format.GetFormat("HH:mm:ss");

                if (order.skipasses.Count <= 0)
                {
                    styleText.SetFont(fontFromWeb);
                    styleMoney.SetFont(fontFromWeb);
                    styleNum.SetFont(fontFromWeb);
                    styleDate.SetFont(fontFromWeb);
                    styleTime.SetFont(fontFromWeb);
                }
                else
                {
                    switch (order.orderState)
                    {
                        case 4:
                            styleText.SetFont(fontUsed);
                            styleMoney.SetFont(fontUsed);
                            styleNum.SetFont(fontUsed);
                            styleDate.SetFont(fontUsed);
                            styleTime.SetFont(fontUsed);
                            break;
                        case 3:
                            if (order.skipasses[0].order.refundList.Count == 0)
                            {
                                styleText.SetFont(fontUnRefund);
                                styleMoney.SetFont(fontUnRefund);
                                styleNum.SetFont(fontUnRefund);
                                styleDate.SetFont(fontUnRefund);
                                styleTime.SetFont(fontUnRefund);
                            }
                            else
                            {
                                styleText.SetFont(fontCanCel);
                                styleMoney.SetFont(fontCanCel);
                                styleNum.SetFont(fontCanCel);
                                styleDate.SetFont(fontCanCel);
                                styleTime.SetFont(fontCanCel);
                            }
                            break;
                        default:
                            styleText.SetFont(fontFromWeb);
                            styleMoney.SetFont(fontFromWeb);
                            styleNum.SetFont(fontFromWeb);
                            styleDate.SetFont(fontFromWeb);
                            styleTime.SetFont(fontFromWeb);
                            break;
                    }
                }
                for (int j = 0; j < commonFieldsNum; j++)
                {
                    ICell cell = dr.CreateCell(j);
                    switch (j)
                    {
                        case 0:
                            cell.SetCellValue((i + 1));
                            cell.CellStyle = styleNum;
                            break;
                        case 1:
                            if (order.skipasses.Count <= 0)
                            {
                                cell.SetCellValue("大好河山");
                            }
                            else
                            {
                                cell.SetCellValue("小程序");
                            }
                            cell.CellStyle = styleText;
                            break;
                        case 2:
                            cell.SetCellValue(order.orderId.Trim());
                            cell.CellStyle = styleText;
                            break;
                        default:
                            break;
                    }



                }
            }


            string filePath = $"{Environment.CurrentDirectory}" + "/dhhs.xlsx";
            using (var file = System.IO.File.Create(filePath))
            {
                workbook.Write(file);
            }


        }

        [NonAction]
        public void SetParam(string source)
        {
            switch (source)
            {
                case "大好河山":
                    apiKey = dhhsApiKey;
                    custId = dhhsCustId;
                    break;
                default:
                    apiKey = wlApiKey;
                    custId = wlCustId;
                    break;
            }
        }






    }
}

