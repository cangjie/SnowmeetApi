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
using System.IO;
//using LuqinMiniAppBase.Controllers;
using SnowmeetApi.Models;
using SnowmeetApi.Helpers;
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
        public string source = "万龙自我游";

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
            public List<SnowmeetApi.Models.ZiwoyouListOrder> results { get; set; }
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
            public double? cancelMoney { get; set; }
            public string? cancelId { get; set; }
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



        public WanlongZiwoyouHelper(ApplicationDBContext context, IConfiguration config, string source = "万龙自我游")
        {
            _context = context;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            //apiKey = dhhsApiKey;
            //custId = dhhsCustId;
            apiKey = wlApiKey;
            custId = wlCustId;
            this.source = source;
            SetParam(source);
        }

        [HttpGet]
        public async Task<ActionResult<ProductQueryResult>> GetProductList(string keyword, string? source = null)
        {
            UseSource(source);
            int pageSize = 20;
            //string custId = dhhsCustId;
            //string apiKey = dhhsApiKey;
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
                ZiwoyouSkipassProduct[] newResults = new ZiwoyouSkipassProduct[r.data.results.Length + subR.data.results.Length];
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
            // 第 2 页起也用当前账号（原来这里固定用大好河山账号，万龙自我游账号的翻页会拿到别的目录）
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
            Models.SkiPass skipass = await _context.skiPass.FindAsync(skiPassId);
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

            Models.SkiPassProduct skipassProduct = await _context.skiPassProduct.FindAsync(skipass.product_id);
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
            WebApiLog reqLog = await _miniHelper.PerformRequest(url, "", postData, "POST", "易龙雪聚小程序", "预订雪票", "万龙自我游下单");
            ZiwoyouPlaceOrderResult r = JsonConvert.DeserializeObject<ZiwoyouPlaceOrderResult>(reqLog.response.Trim());
            return r;
        }
        [NonAction]
        public async Task<PayResult> Pay(int orderId)
        {
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim() + ",\"orderId\": " + orderId.ToString() + "}";
            WebApiLog reqLog = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/order/pay",
                "", postData, "POST", "易龙雪聚小程序", "预订雪票", "万龙自我游支付");
            PayResult p = JsonConvert.DeserializeObject<PayResult>(reqLog.response);
            return p;

        }

        [HttpGet]
        public async Task<ActionResult<string>> GetProductDetail(int productId)
        {
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim() + ",\"productNo\": " + productId.ToString() + "}";
            WebApiLog log = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/prod/detail",
                 "", postData, "POST", "易龙雪聚小程序", "预订雪票", "万龙自我游产品查询");

            return Ok(log.response);
        }

        [HttpGet]
        public async Task<ActionResult<ZiwoyouProductDailyPrice>> GetProductPrice(int productId, DateTime date, string? source = null)
        {
            UseSource(source);
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim() + ",\"productNo\": "
                + productId.ToString() + ", \"travelDate\": \"" + date.ToString("yyyy-MM-dd") + "\" }";
            WebApiLog log = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/prod/price",
                 "", postData, "POST", "易龙雪聚小程序", "预订雪票", "万龙自我游产品价格查询");
            ZiwoyouQueryResult r = JsonConvert.DeserializeObject<ZiwoyouQueryResult>(log.response);
            ZiwoyouProductDailyPrice price = JsonConvert.DeserializeObject<ZiwoyouProductDailyPrice>(r.data.ToString());
            return price;
        }

        [HttpGet]
        public async Task<ActionResult<ZiwoyouOrder>> GetOrder(int orderId, string? source = null)
        {
            UseSource(source);
            ZiwoyouOrder? order = await TryGetOrder(orderId);
            if (order == null)
            {
                // 自我游的订单号是全局唯一的，当前账号查不到就换另一个账号查一次：
                // 万龙试营业的票是用大好河山账号下的单，而出票、核销、退款的回环调用默认用万龙自我游账号。
                // 查完把账号换回来，后面同一个实例上的取消订单等调用仍用原账号。
                bool isDhhs = apiKey == dhhsApiKey;
                SetParam(isDhhs ? SkipassSaleRules.SOURCE_WANLONG : SkipassSaleRules.SOURCE_DHHS);
                order = await TryGetOrder(orderId);
                SetParam(isDhhs ? SkipassSaleRules.SOURCE_DHHS : SkipassSaleRules.SOURCE_WANLONG);
            }
            if (order == null)
            {
                return NotFound();
            }
            return Ok(order);
        }

        // 用当前账号查订单；订单不存在或接口报错返回 null
        [NonAction]
        private async Task<ZiwoyouOrder?> TryGetOrder(int orderId)
        {
            try
            {
                MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
                string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim()
                    + ",\"orderId\": " + orderId.ToString() + "}";
                WebApiLog log = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/order/detail",
                     "", postData, "POST", "易龙雪聚小程序", "预订雪票", "万龙自我游订单查询");
                ZiwoyouQueryResult? r = JsonConvert.DeserializeObject<ZiwoyouQueryResult>(log.response.Trim());
                if (r == null || r.state != 1 || r.data == null)
                {
                    return null;
                }
                return JsonConvert.DeserializeObject<ZiwoyouOrder>(r.data.ToString());
            }
            catch
            {
                return null;
            }
        }

        // 带了 source 就切到对应的自我游账号（不带沿用构造时的账号）
        [NonAction]
        private void UseSource(string? source)
        {
            if (!string.IsNullOrWhiteSpace(source))
            {
                this.source = source.Trim();
                SetParam(this.source);
            }
        }

        [NonAction]
        public async Task<ZiwoyouQueryResult> CancelOrder(int orderId)
        {
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            ZiwoyouOrder order = (ZiwoyouOrder)((OkObjectResult)(await GetOrder(orderId)).Result).Value;
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim()
                + ",\"orderId\": " + orderId.ToString() + ", \"cancelNum\": " + order.num.ToString() + "}";
            WebApiLog log = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/order/cancel",
                 "", postData, "POST", "易龙雪聚小程序", "预订雪票", "万龙自我游订单取消");
            ZiwoyouQueryResult r = JsonConvert.DeserializeObject<ZiwoyouQueryResult>(log.response);
            ZiwoyouCancel cancel = JsonConvert.DeserializeObject<ZiwoyouCancel>(r.data.ToString());
            r.data = cancel;
            return r;
        }

        [NonAction]
        public async Task<Models.SkiPassProduct> GetSkipassProductByCode(string code)
        {
            var l = await _context.skiPassProduct.Where(s => s.third_party_no.Trim().Equals(code.Trim()))
                .AsNoTracking().ToListAsync();
            if (l == null || l.Count <= 0)
            {
                return null;
            }
            Models.SkiPassProduct skipass = l[0];
            skipass.product = await _context.product.FindAsync(skipass.product_id);
            return skipass;

        }

        // 自我游查价格一次只返回到当月月底（从 travelDate 算起），所以从起始日起按月往后查，
        // 连续两个月没有价格就停；商品已下架（接口报错）整个商品跳过。
        // source 不传同步所有来源的商品，传了只同步该账号的商品；每个商品用它自己的账号取价。
        const int MONTHS_TO_SYNC = 8;

        [HttpGet]
        public async Task UpdateSkipassProductPrice(DateTime? date = null, string? source = null)
        {
            DateTime currentDate = date == null? DateTime.Now: (DateTime)date;

            var query = _context.skiPassProduct.Where(s => s.third_party_no != null);
            if (!string.IsNullOrWhiteSpace(source))
            {
                string src = source.Trim();
                query = query.Where(s => s.source == src);
            }
            var l = await query.AsNoTracking().ToListAsync();
            foreach (var item in l)
            {
                int emptyMonths = 0;
                foreach (DateTime monthFrom in SkipassSaleRules.MonthStarts(currentDate, MONTHS_TO_SYNC))
                {
                    int rows;
                    try
                    {
                        rows = await SyncProductMonthPrice(item, monthFrom);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                        break;
                    }
                    emptyMonths = rows == 0 ? emptyMonths + 1 : 0;
                    if (emptyMonths >= 2)
                    {
                        break;
                    }
                }
            }
        }

        // 取一个商品从 from 起到当月月底的每日价格并写库；返回这次接口给了多少天
        [NonAction]
        private async Task<int> SyncProductMonthPrice(Models.SkiPassProduct item, DateTime from)
        {
            string priceStr = Util.GetWebContent("https://mini.snowmeet.top/core/WanlongZiwoyouHelper/GetProductPrice?productId=" + item.third_party_no.Trim()
                + "&date=" + from.ToString("yyyy-MM-dd") + "&source=" + Uri.EscapeDataString((item.source ?? "").Trim()));
            ZiwoyouProductDailyPrice price = JsonConvert.DeserializeObject<ZiwoyouProductDailyPrice>(priceStr);
            ZiwoyouDailyPrice[] priceArr = price.ticketPrices;
            if (priceArr.Length == 0)
            {
                return 0;
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
                        // 结算价、建议售价、门市价任一变了都要换新行：成交价取门市价，只比结算价会漏掉门市价调整
                        if (SkipassSaleRules.PriceChanged(oriPrice, priceObj.settlementPrice, priceObj.salePrice, priceObj.marketPrice))
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
                    // 成交价取自我游门市价；只有门市价缺失时才需要「结算价 + 加价」，这时才去查上一次的加价
                    if (revenu == 0 && !(priceObj.marketPrice > 0))
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
                        deal_price = SkipassSaleRules.ResolveDealPrice(priceObj.marketPrice, priceObj.settlementPrice, revenu),
                        reserve_date = priceObj.date.Date,
                        day_type = ((priceObj.date.Date.DayOfWeek == DayOfWeek.Sunday || priceObj.date.Date.DayOfWeek == DayOfWeek.Saturday) ? "周末" : "平日")
                    };
                    await _context.skipassDailyPrice.AddAsync(newPrice);
                    await _context.SaveChangesAsync();
                }

            }
            return priceArr.Length;
        }



        [HttpGet]
        public async Task UpdateSkipassProduct(string keyword, string? source = null, int unhide = 0)
        {
            // source 选自我游账号（不传沿用万龙自我游）；同步到的商品来源会记成这个账号，之后查价、下单都用它。
            // unhide=1 把这次同步到的商品置为可见（商品原来被隐藏、现在要卖时用；默认不动隐藏状态）。
            UseSource(source);
            ProductQueryResult originProductInfo = (ProductQueryResult)((OkObjectResult)(await GetProductList(keyword)).Result).Value;
            // 万龙自我游同步过来的都是「崇礼旗舰店卖的万龙雪场票」：
            // 门店 = 崇礼旗舰店（shop_id 关联 shop_list），雪场 = 万龙（category「万龙雪票」）。
            // 分类必须写，否则新同步进来的商品不会出现在按分类筛的雪票列表里。
            int? syncShopId = await _context.shop.AsNoTracking()
                .Where(x => x.name == "崇礼旗舰店").Select(x => (int?)x.id).FirstOrDefaultAsync();
            int? syncCategoryId = await _context.category.AsNoTracking()
                .Where(c => c.biz_type == "雪票" && c.name == "万龙雪票" && c.valid == 1)
                .Select(c => (int?)c.id).FirstOrDefaultAsync();
            for (int i = 0; i < originProductInfo.data.results.Length; i++)
            {
                ZiwoyouSkipassProduct skipassProduct = originProductInfo.data.results[i];
                /*
                if (skipassProduct.productNo!="37696044")
                {
                    continue;
                }
                */
                // 大好河山账号的目录里搜「万龙」还会返回「万龙&常规」这类酒店套餐，只收万龙滑雪场的雪票和次卡
                if (this.source == SkipassSaleRules.SOURCE_DHHS
                    && !SkipassSaleRules.IsWanlongLiftProduct(skipassProduct.productName, skipassProduct.viewName))
                {
                    continue;
                }
                Models.SkiPassProduct skipass = await GetSkipassProductByCode(skipassProduct.productNo);
                // 对外价格 = 自我游门市价（不再加价）；列表接口没给门市价时退回建议售价
                double retailPrice = SkipassSaleRules.ResolveDealPrice(skipassProduct.marketPrice, skipassProduct.salePrice, 0);
                if (skipass != null)
                {
                    skipass.product.name = skipassProduct.productName;
                    skipass.product.update_date = DateTime.Now;

                    skipass.product.sale_price = retailPrice;
                    skipass.product.market_price = retailPrice;
                    skipass.product.cost = skipassProduct.settlementPrice;
                    if (unhide == 1)
                    {
                        skipass.product.hidden = 0;
                    }
                    // 去年的商品，有效期还停在去年的雪季：按商品名里的雪季（没写就按今天所在雪季）刷新
                    if (skipass.product.end_date == null || skipass.product.end_date < DateTime.Today)
                    {
                        var renewed = SkipassSaleRules.ResolveSeasonDates(skipassProduct.productName, DateTime.Now);
                        skipass.product.start_date = renewed.Start;
                        skipass.product.end_date = renewed.End;
                    }
                    skipass.source = this.source.Trim();
                    skipass.update_date = DateTime.Now;
                    _context.Entry<Models.Product>(skipass.product).State = EntityState.Modified;
                    _context.skiPassProduct.Entry(skipass).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
                else
                {
                    // 雪季起止日按商品名里的「26-27」推算，没写就按今天所在雪季
                    var season = SkipassSaleRules.ResolveSeasonDates(skipassProduct.productName, DateTime.Now);
                    Models.Product p = new Models.Product()
                    {
                        id = 0,
                        name = skipassProduct.productName.Trim(),
                        sale_price = retailPrice,
                        market_price = retailPrice,
                        cost = skipassProduct.settlementPrice,
                        type = "雪票",
                        shop_id = syncShopId,
                        category_id = syncCategoryId,
                        hidden = 0,
                        start_date = season.Start,
                        end_date = season.End,
                        intro = skipassProduct.orderDesc.Trim(),
                        principal = ""

                    };
                    await _context.product.AddAsync(p);
                    await _context.SaveChangesAsync();
                    Models.SkiPassProduct ski = new Models.SkiPassProduct()
                    {
                        product_id = p.id,
                        resort = keyword.Trim(),
                        rules = skipassProduct.orderDesc.Trim(),
                        source = this.source.Trim(),
                        third_party_no = skipassProduct.productNo
                    };
                    await _context.skiPassProduct.AddAsync(ski);
                    await _context.SaveChangesAsync();

                }
            }

        }


        // 会话是在职店员才算「能看进货价」；没传会话、会话无效、不是店员都按顾客处理
        [NonAction]
        private async Task<bool> CanSeeCostAsync(string? sessionKey, string sessionType)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
            {
                return false;
            }
            Staff? staff = await Util.GetStaffBySessionKey(_context, sessionKey, sessionType);
            return SkipassSaleRules.CanSeeCost(staff);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetProductById(int id, string? sessionKey = null,
            string sessionType = "wechat_mini_openid")
        {
            var l = await _context.skiPassProduct.Include(s => s.dailyPrice)
                .Join(_context.product, s => s.product_id, p => p.id,
                (s, p) => new { s.product_id, s.resort, s.rules, s.source, s.third_party_no, p.name, p.shop_id, p.sale_price, p.market_price, p.cost, p.type, s.dailyPrice })
                .Where(p => p.type.Trim().Equals("雪票") && p.product_id == id
                && p.third_party_no != null)
                .AsNoTracking().ToListAsync();
            if (l == null || l.Count <= 0)
            {
                return NotFound();
            }
            // 门店名以 shop_id 关联 shop_list 为准，product.shop 自由文本只作兜底
            var row = l[0];
            string shopName = "";
            if (row.shop_id != null)
            {
                Models.Shop sh = await _context.shop.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.id == row.shop_id);
                shopName = sh == null ? "" : (sh.name ?? "").Trim();
            }
            // 顾客能看价格，但看不到进货价：商品 cost、每日结算价和建议售价只给店员
            bool canSeeCost = await CanSeeCostAsync(sessionKey, sessionType);
            if (!canSeeCost)
            {
                SkipassSaleRules.RedactDailyPrices(row.dailyPrice);
            }
            return Ok(new { row.product_id, row.resort, row.rules, row.source, row.third_party_no,
                row.name, shop = shopName, row.sale_price, row.market_price,
                cost = canSeeCost ? row.cost : null, row.type, row.dailyPrice });
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
                 "", postData, "POST", "易龙雪聚小程序", "预订雪票", "万龙自我游订单获取分页列表");
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
        // 自我游预存款余额只给店员看；顾客、没带会话的请求一律 401
        [HttpGet]
        public async Task<ActionResult<double>> GetBalance(string? sessionKey = null,
            string sessionType = "wechat_mini_openid", string? source = null)
        {
            if (!await CanSeeCostAsync(sessionKey, sessionType))
            {
                return Unauthorized();
            }
            return Ok(await QueryBalance(source));
        }

        // 服务端内部（出票前查余额）直接调用，不经过 HTTP
        [NonAction]
        public async Task<double> QueryBalance(string? account = null)
        {
            // account 不传就用构造时的账号（出票前查余额走这条，账号由商品的来源决定）
            SetParam(string.IsNullOrWhiteSpace(account) ? source : account.Trim());
            string postData = "{\"apikey\": \"" + apiKey + "\", \"custId\": " + custId + "}";
            MiniAppHelperController _miniHelper = new MiniAppHelperController(_context, _oriConfig);
            WebApiLog reqLog = await _miniHelper.PerformRequest("https://task-api.zowoyoo.com/api/thirdPaty/order/balance",
                "", postData.Trim(), "POST", "易龙雪聚小程序", "预订雪票", "查询万龙自我游储值");
            try
            {
                ZiwoyouQueryResult r = JsonConvert.DeserializeObject<ZiwoyouQueryResult>(reqLog.response.Trim());
                ZiwoyouAccountBalance b = JsonConvert.DeserializeObject<ZiwoyouAccountBalance>(r.data.ToString());
                return b.accountBalance;
            }
            catch
            {
                return 0;
            }
        }

        [HttpGet]
        public async Task<ActionResult<List<ZiwoyouListOrder>>> GetOrderBills(DateTime start, DateTime end)
        {
            var l = await _context.ziwoyouOrder
            .Include(z => z.skipasses)
                .ThenInclude(s => s.order)
                    .ThenInclude(o => o.availablePayments.Where(p => p.status.Equals("支付成功")))
                        .ThenInclude(p => p.refunds.Where(r => (r.state == 1 || r.refund_id.Trim().Equals(""))))
            .Include(z => z.skipasses)
                .ThenInclude(s => s.order)
                    .ThenInclude(o => o.availablePayments.Where(p => p.status.Equals("支付成功")))
                        //.ThenInclude(p => p.shares)
                        //    .ThenInclude(s => s.kol)


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
                        maxPaymentNum = Math.Max(maxPaymentNum, order.skipasses[j].order.availablePayments.Count);

                        if (order.skipasses[j].order.refunds != null)
                        {
                            maxRefundNum = Math.Max(maxRefundNum, order.skipasses[j].order.refunds.Count);
                        }

                    }
                }
            }
            List<string> head = [
                "序号","下单渠道" ,"自我游单号", "日期" ,"时间", "名称", "数量", "状态", "结算价", "收款",
                "退款", "利润", "联系人", "联系电话", "客服", "分账金额","分账状态","分账日期" ,"分账时间", "分账单号"];
            int commonFieldsNum = head.Count;
            string[] headPayment = ["收款单号", "收款金额", "收款日期", "收款时间"];
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
                        case 0:
                        case 6:
                        case 9:
                        case 10:
                        case 11:
                            sheet.SetColumnWidth(i, 1500);
                            break;
                        case 2:
                        case 3:
                        case 13:
                            sheet.SetColumnWidth(i, 3000);
                            break;
                        case 4:
                            sheet.SetColumnWidth(i, 2500);
                            break;
                        case 5:
                            sheet.SetColumnWidth(i, 8000);
                            break;
                        case 7:
                        case 8:
                            sheet.SetColumnWidth(i, 2000);
                            break;
                        case 14:
                            sheet.SetColumnWidth(i, 3500);
                            break;
                        case 19:
                        
                            sheet.SetColumnWidth(i, 11000);
                            break;
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
                            sheet.SetColumnWidth(i, 7000);
                            break;
                        case 2:
                        case 3:
                            sheet.SetColumnWidth(i, 2600);
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
                            sheet.SetColumnWidth(i, 11000);
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

                double settlePrice = order.settlementPrice * order.num;
                double charge = 0;
                double refund = 0;

                if (order.skipasses.Count > 0)
                {
                    charge = order.skipasses[0].order.paidAmount;
                    refund = order.skipasses[0].order.refundAmount;
                }
                double revenu = charge - settlePrice - refund;
                if (order.orderState == 3)
                {
                    revenu = 0;
                }
                PaymentShare? share = null;
                bool haveOrder = false;
                if (order.skipasses.Count > 0 && order.skipasses[0].order != null
                    && order.skipasses[0].order.availablePayments.Count > 0)
                {
                    haveOrder = true;
                }
                /*
                if (haveOrder && order.skipasses[0].order.availablePayments[0].shares.Count > 0)
                {
                    share = order.skipasses[0].order.availablePayments[0].shares[0];
                }
*/
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
                            if (order.skipasses[0].order.refunds.Count == 0)
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
                    if (revenu < 0)
                    {
                        styleText.SetFont(fontProblem);
                        styleMoney.SetFont(fontProblem);
                        styleNum.SetFont(fontProblem);
                        styleDate.SetFont(fontProblem);
                        styleTime.SetFont(fontProblem);
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
                            if (!haveOrder)
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
                        case 3:
                            cell.SetCellValue(order.orderDate);
                            cell.CellStyle = styleDate;
                            break;
                        case 4:
                            cell.SetCellValue(order.orderDate);
                            cell.CellStyle = styleTime;
                            break;
                        case 5:
                            cell.SetCellValue(order.productName.Trim());
                            cell.CellStyle = styleText;
                            break;
                        case 6:
                            cell.SetCellValue(order.num);
                            cell.CellStyle = styleNum;
                            break;
                        case 7:
                            string status = "";
                            switch (order.orderState)
                            {
                                case 1:
                                    status = "已预订";
                                    break;
                                case 2:
                                    status = "已出票";
                                    break;
                                case 3:
                                    status = "已取消";
                                    break;
                                case 4:
                                    status = "已核销";
                                    break;
                                default:
                                    break;
                            }
                            cell.SetCellValue(status);
                            cell.CellStyle = styleText;
                            break;
                        case 8:
                            cell.SetCellValue(settlePrice);
                            cell.CellStyle = styleMoney;
                            break;
                        case 9:
                            cell.SetCellValue(charge);
                            cell.CellStyle = styleMoney;
                            break;
                        case 10:
                            cell.SetCellValue(refund);
                            cell.CellStyle = styleMoney;
                            break;
                        case 11:
                            cell.SetCellValue(revenu);
                            cell.CellStyle = styleMoney;
                            break;
                        case 12:
                            cell.SetCellValue(order.linkMan);
                            cell.CellStyle = styleText;
                            break;
                        case 13:
                            cell.SetCellValue(order.linkPhone);
                            cell.CellStyle = styleText;
                            break;
                        case 14:
                        /*
                            if (share != null && share.kol != null && order.orderState == 4)
                            {
                                cell.SetCellValue(share.kol.real_name.Trim());
                            }
                            else
                            {
                                cell.SetCellValue(nullStr);
                            }
                            */
                            cell.CellStyle = styleText;
                            break;
                        case 15:
                            if (share != null && order.orderState == 4)
                            {
                                cell.SetCellValue(share.amount);
                                cell.CellStyle = styleMoney;
                            }
                            else
                            {
                                cell.SetCellValue(nullStr);
                                cell.CellStyle = styleText;
                            }
                            break;
                        case 16:
                        /*
                            if (share != null && order.orderState == 4)
                            {
                                cell.SetCellValue(share.state == 1 ? "已分" : "未分");
                            }
                            else
                            {
                                cell.SetCellValue(nullStr);
                            }
                            */
                            cell.CellStyle = styleText;
                            break;
                        case 17:
                        /*
                            if (share != null && share.state == 1 && share.submit_date != null && order.orderState == 4)
                            {
                                cell.SetCellValue((DateTime)share.submit_date);
                                cell.CellStyle = styleDate;
                            }
                            else
                            {
                                cell.SetCellValue(nullStr);
                                cell.CellStyle = styleText;
                            }
                            */
                            break;
                        case 18:
                        /*
                            if (share != null && share.state == 1 && share.submit_date != null && order.orderState == 4)
                            {
                                cell.SetCellValue((DateTime)share.submit_date);
                                cell.CellStyle = styleTime;
                            }
                            else
                            {
                                cell.SetCellValue(nullStr);
                                cell.CellStyle = styleText;
                            }
                            */
                            break;
                        case 19:
                        /*
                            if (share != null && share.state == 1 && share.submit_date != null && order.orderState == 4)
                            {
                                cell.SetCellValue(share.out_trade_no);
                            }
                            else
                            {
                                cell.SetCellValue(nullStr);
                            }
                            */
                            cell.CellStyle = styleText;
                            break;
                        default:
                            break;
                    }
                }
                for (int j = 0; j < maxPaymentNum; j++)
                {
                    for (int k = 0; k < headPayment.Length; k++)
                    {
                        int index = commonFieldsNum + j * headPayment.Length + k;
                        ICell cell = dr.CreateCell(index);
                        if (haveOrder && j < order.skipasses[0].order.availablePayments.Count)
                        {
                            OrderPayment payment = order.skipasses[0].order.availablePayments[0];
                            switch (k)
                            {
                                case 0:
                                    cell.SetCellValue(payment.out_trade_no.Trim());
                                    cell.CellStyle = styleText;
                                    break;
                                case 1:
                                    cell.SetCellValue(payment.amount);
                                    cell.CellStyle = styleMoney;
                                    break;
                                case 2:
                                    cell.SetCellValue(payment.create_date);
                                    cell.CellStyle = styleDate;
                                    break;
                                case 3:
                                    cell.SetCellValue(payment.create_date);
                                    cell.CellStyle = styleTime;
                                    break;
                                default:
                                    break;
                            }
                        }
                        else
                        {
                            cell.SetCellValue(nullStr);
                            cell.CellStyle = styleText;
                        }


                    }
                }
                for (int j = 0; j < maxRefundNum; j++)
                {
                    for (int k = 0; k < headRefund.Length; k++)
                    {
                        int index = commonFieldsNum + headPayment.Length * maxPaymentNum + j * headRefund.Length + k;
                        ICell cell = dr.CreateCell(index);
                        if (haveOrder && j < order.skipasses[0].order.refunds.Count)
                        {
                            OrderPaymentRefund r = order.skipasses[0].order.refunds[j];
                            switch (k)
                            {
                                case 0:
                                    cell.SetCellValue(r.out_refund_no);
                                    cell.CellStyle = styleText;
                                    break;
                                case 1:
                                    cell.SetCellValue(r.amount);
                                    cell.CellStyle = styleMoney;
                                    break;
                                case 2:
                                    cell.SetCellValue(r.create_date);
                                    cell.CellStyle = styleDate;
                                    break;
                                case 3:
                                    cell.SetCellValue(r.create_date);
                                    cell.CellStyle = styleTime;
                                    break;
                                default:
                                    break;
                            }
                        }
                        else
                        {
                            cell.SetCellValue(nullStr);
                            cell.CellStyle = styleText;
                        }
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

