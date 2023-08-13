using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using static SKIT.FlurlHttpClient.Wechat.TenpayV3.Models.CreateApplyForSubMerchantApplymentRequest.Types.Business.Types.SaleScene.Types;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class WepayBillReportController: ControllerBase
	{
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public bool isStaff = false;

        private IConfiguration _oriConfig;

        private readonly IHttpContextAccessor _httpContextAccessor;

        public class Refund
        {
            public string refund_type { get; set; }
            public string date { get; set; }
            public string time { get; set; }
            public string wepay_refund_id { get; set; }
            public string out_refund_id { get; set; }
            public string refund_amount { get; set; }
            public string return_fee { get; set; }
            public string summary { get; set; }
            public string status { get; set; }
            public string oper { get; set; }
            public string openId { get; set; } = "";
        }

        public class MemberInfo
        {
            public string real_name { get; set; }
            public string gender { get; set; }
            public string cell { get; set; }
            public string nick { get; set; }
            public string oaOpenId { get; set; }
            public string oaOldOpenId { get; set; }
            public string miniOpenId { get; set; }
            public string commonOpenId { get; set; }

        }

        public class BusinessInfo
        {
            public string type { get; set; }
            public string id { get; set; }
            public string name { get; set; } = "";
            public string cell { get; set; } = "";
            public string description { get; set; } = "";
        }

        public class Balance
        {
            public int id { get; set; }
            public string date { get; set; } = "";
            public string time { get; set; } = "";
            public string trans_type { get; set; } = "";
            public string month { get; set; } = "";
            public string season { get; set; } = "";
            public MemberInfo member { get; set; } 
            public string payMethod { get; set; } = "微信支付";
            public string mch_id { get; set; } = "";
            public string out_trade_no { get; set; } = "";
            public string TransactId { get; set; } = "";
            public string duplicate_num { get; set; } = "";
            public string order_type { get; set; } = "";
            public string shop { get; set; } = "";
            public string task_id { get; set; } = "";
            public string order_id { get; set; } = "";
            public string open_id { get; set; } = "";
            public string income { get; set; } = "";
            public string fee { get; set; } = "";
            public string summary { get; set; } = "";

            public string refund_amount { get; set; } = "";
            public string refund_fee { get; set; } = "";
            public string refund_summary { get; set; } = "";
            public string refund_type { get; set; } = "";

            public string operOpenId { get; set; } = "";

            public List<Refund> refunds { get; set; } 
            public string total_refund { get; set; } = "";
            public string total_refund_fee { get; set; } = "";
            public string total_refund_summary { get; set; } = "";
            public string total_summary { get; set; } = "";
            public string fee_rate { get; set; } = "";
            public BusinessInfo business { get; set; } 
            public string oper { get; set; } = "";
           
        }

        [HttpGet]
        public async Task<ActionResult<BusinessReport>> Test()
        {
            BusinessReport br = new BusinessReport()
            {
                date = DateTime.Now.Date,
                time = DateTime.Now.Hour.ToString()+":" + DateTime.Now.Minute.ToString()+":" + DateTime.Now.Second.ToString()
            };
            await _context.businessReport.AddAsync(br);
            await _context.SaveChangesAsync();
            return Ok(br);
        }

        

        [HttpGet]
        public async Task<ActionResult<string>> ExportDataFile(string mch_id)
        {

            string dirPath = Util.workingPath + "/wwwroot/bidata";
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }
            string fileName = mch_id.Trim() + "_" + Util.GetLongTimeStamp(DateTime.Now) + ".csv";
            Balance[] bArr = await GetBalance(mch_id);
            string headLine = "序号,日期,时间,类别,月份,运营区间,门店,昵称,手机,姓名,性别,支付渠道,渠道商户号,商户订单号,支付订单号,业务单号,收入类型,业务明细,收入,手续费,入账金额,退款合计,退回手续费合计,实际出账合计,支付订单当前结余,退款次数,退款方式,退款,手续费退回,出账金额";
            int maxRefunds = 0;
            for (int i = 0; i < bArr.Length; i++)
            {
                maxRefunds = Math.Max(maxRefunds, bArr[i].refunds.Count);
            }
            for (int i = 0; i < maxRefunds; i++)
            {
                headLine += ",退款日期i,退款时间i,退款单号i,退款金额i,退款返回手续费i,退款实际出账i,退款方式i,退款操作员i".Replace("i", (i + 1).ToString());
            }
            headLine += ",操作员";

            string content = headLine + "\r\n";


            for (int i = 0; i < bArr.Length; i++)
            {
                string s = GetLineString(bArr[i], headLine);
                content += s + "\r\n";
            }

            System.IO.File.AppendAllText(dirPath + "/" + fileName.Trim(), content, System.Text.Encoding.UTF8);

            return Ok(dirPath + "/" + fileName.Trim());
        }

        [HttpGet]
        public async Task<ActionResult<int>> ExportToDbPerLine()
        {
            Balance[] bArr = await GetBalance("");
            for (int i = 0; i < bArr.Length; i++)
            {
                try
                {
                    await SaveToDb(bArr[i]);
                }
                catch (Exception err)
                {
                    throw new Exception("save db error, line " + i.ToString() + " " + err.ToString());
                }
            }
            return Ok(0);
        }

        [NonAction]
        public async Task<bool> SaveToDb(Balance b)
        {
            BusinessReport br = new BusinessReport();
            br.date = DateTime.Parse(b.date);
            br.time = b.time;
            br.type = b.trans_type.Trim();
            br.month = b.month;
            br.season = b.season;
            br.shop = b.shop;
            MemberInfo memberInfo = await GetUnicUser(b.open_id);
            br.mini_open_id = memberInfo.miniOpenId != null ? memberInfo.miniOpenId.Trim() : "";
            br.oa_open_id = memberInfo.oaOpenId != null ? memberInfo.oaOpenId.Trim() : "";
            br.oaold_open_id = memberInfo.oaOldOpenId != null ? memberInfo.oaOldOpenId.Trim() : "";
            br.real_name = memberInfo.real_name != null ? memberInfo.real_name.Trim() : "";
            br.nick = memberInfo.nick != null ? memberInfo.nick : "";
            br.cell = memberInfo.cell != null ? memberInfo.cell : "";
            br.gender = memberInfo.gender != null ? memberInfo.gender : "";
            br.pay_method = "微信支付";
            br.mch_id = b.mch_id;
            br.out_trade_no = b.out_trade_no;
            br.TransactionId = b.TransactId;



            if (b.business != null)
            {
                br.business_id = b.business.id;
                br.business_type = b.business.type;
                br.business_detail = b.business.description;
            }

            
            br.is_test = 0;
            br.order_status = "";
            br.order_type = "";
            br.income_type = "";
            br.refund_times = b.refunds.Count;




            switch (br.type.Trim())
            {
                case "收入":
                    try
                    {
                        br.income = double.Parse(b.income);
                    }
                    catch
                    {
                        br.income = 0;
                    }
                    try
                    {
                        br.income_fee = double.Parse(b.fee);
                    }
                    catch
                    {
                        br.income_fee = 0;
                    }
                    try
                    {
                        br.income_real = br.income - br.income_fee;
                    }
                    catch
                    {
                        br.income_real = 0;
                    }
                    try
                    {
                        br.refund_summary = double.Parse(b.total_refund);
                    }
                    catch
                    {
                        br.refund_summary = 0;
                    }
                    try
                    {
                        br.refund_fee_summary = double.Parse(b.total_refund_fee);
                    }
                    catch
                    {
                        br.refund_fee_summary = 0;
                    }
                    try
                    {
                        br.refund_real_summary = br.refund_summary + br.refund_fee_summary;
                    }
                    catch
                    {
                        br.refund_real_summary = 0;
                    }
                    try
                    {
                        br.total_summary = br.income_real - br.refund_real_summary;
                    }
                    catch
                    {
                        br.total_summary = 0;
                    }

                    for (int j = 0; j < b.refunds.Count; j++)
                    {
                        Refund r = b.refunds[j];
                        switch (j)
                        {
                            case 0:
                                br.refund1_oper_open_id = r.openId;
                                MemberInfo member1 = await GetUnicUser(r.openId);
                                br.refund1_oper_name = member1.real_name.Trim();
                                br.refund1_date = DateTime.Parse(r.date);
                                br.refund1_time = r.time;
                                br.refund1_amount = double.Parse(r.refund_amount);
                                br.refund1_fee = double.Parse(r.return_fee);
                                br.refund1_summary = br.refund1_amount + br.refund1_fee;
                                br.refund1_no = r.wepay_refund_id;
                                br.refund1_type = r.refund_type;
                                break;
                            case 1:
                                br.refund2_oper_open_id = r.openId;
                                MemberInfo member2 = await GetUnicUser(r.openId);
                                br.refund2_oper_name = member2.real_name.Trim();
                                br.refund2_date = DateTime.Parse(r.date);
                                br.refund2_time = r.time;
                                br.refund2_amount = double.Parse(r.refund_amount);
                                br.refund2_fee = double.Parse(r.return_fee);
                                br.refund2_summary = br.refund1_amount + br.refund1_fee;
                                br.refund2_no = r.wepay_refund_id;
                                br.refund2_type = r.refund_type;
                                break;
                            case 2:
                                br.refund3_oper_open_id = r.openId;
                                MemberInfo member3 = await GetUnicUser(r.openId);
                                br.refund3_oper_name = member3.real_name.Trim();
                                br.refund3_date = DateTime.Parse(r.date);
                                br.refund3_time = r.time;
                                br.refund3_amount = double.Parse(r.refund_amount);
                                br.refund3_fee = double.Parse(r.return_fee);
                                br.refund3_summary = br.refund1_amount + br.refund1_fee;
                                br.refund3_no = r.wepay_refund_id;
                                br.refund3_type = r.refund_type;
                                break;
                            case 3:
                                br.refund4_oper_open_id = r.openId;
                                MemberInfo member4 = await GetUnicUser(r.openId);
                                br.refund4_oper_name = member4.real_name.Trim();
                                br.refund4_date = DateTime.Parse(r.date);
                                br.refund4_time = r.time;
                                br.refund4_amount = double.Parse(r.refund_amount);
                                br.refund4_fee = double.Parse(r.return_fee);
                                br.refund4_summary = br.refund1_amount + br.refund1_fee;
                                br.refund4_no = r.wepay_refund_id;
                                br.refund4_type = r.refund_type;
                                break;
                            case 4:
                                br.refund5_oper_open_id = r.openId;
                                MemberInfo member5 = await GetUnicUser(r.openId);
                                br.refund5_oper_name = member5.real_name.Trim();
                                br.refund5_date = DateTime.Parse(r.date);
                                br.refund5_time = r.time;
                                br.refund5_amount = double.Parse(r.refund_amount);
                                br.refund5_fee = double.Parse(r.return_fee);
                                br.refund5_summary = br.refund1_amount + br.refund1_fee;
                                br.refund5_no = r.wepay_refund_id;
                                br.refund5_type = r.refund_type;
                                break;
                            case 5:
                                br.refund6_oper_open_id = r.openId;
                                MemberInfo member6 = await GetUnicUser(r.openId);
                                br.refund6_oper_name = member6.real_name.Trim();
                                br.refund6_date = DateTime.Parse(r.date);
                                br.refund6_time = r.time;
                                br.refund6_amount = double.Parse(r.refund_amount);
                                br.refund6_fee = double.Parse(r.return_fee);
                                br.refund6_summary = br.refund1_amount + br.refund1_fee;
                                br.refund6_no = r.wepay_refund_id;
                                br.refund6_type = r.refund_type;
                                break;
                            default:
                                break;
                        }

                    }

                    br.income_oper_open_id = b.operOpenId.Trim();
                    MemberInfo openMember = await GetUnicUser(b.operOpenId.Trim());

                    br.income_oper_name = openMember.real_name.Trim();

                    break;
                case "退款":
                    try
                    {
                        br.refund_amount = double.Parse(b.refund_amount);
                    }
                    catch
                    {
                        br.refund_amount = 0;
                    }
                    try
                    {
                        br.refund_fee = double.Parse(b.refund_fee);
                    }
                    catch
                    {
                        br.refund_fee = 0;
                    }
                    try
                    {
                        br.refund_real = br.refund_amount + br.refund_fee;
                    }
                    catch
                    {
                        br.refund_real = 0;
                    }
                    br.refund_oper_open_id = b.operOpenId;
                    br.refund_oper_name = (await GetUnicUser(b.operOpenId)).real_name.Trim();

                    break;
                default:
                    break;
            }
            try
            {
                await _context.businessReport.AddAsync(br);
                await _context.SaveChangesAsync();
                return true;
            }
            catch(Exception err)
            {
                throw new Exception(err.ToString());
                //return false;
            }

            
        }

        [HttpGet]
        public async Task<ActionResult<int>> ExportDataToDb()
        {
            int num = 0;
            Balance[] bArr = await GetBalance("");
            for (int i = 0; i < bArr.Length; i++)
            {
                Balance b = bArr[i];
                BusinessReport br = new BusinessReport();
                br.date = DateTime.Parse(b.date);
                br.time = b.time;
                br.type = b.trans_type.Trim();
                br.month = b.month;
                br.season = b.season;
                br.shop = b.shop;
                MemberInfo memberInfo = await GetUnicUser(b.open_id);
                br.mini_open_id = memberInfo.miniOpenId != null ? memberInfo.miniOpenId.Trim() : "";
                br.oa_open_id = memberInfo.oaOpenId != null ? memberInfo.oaOpenId.Trim() : "";
                br.oaold_open_id = memberInfo.oaOldOpenId != null ? memberInfo.oaOldOpenId.Trim() : "";
                br.real_name = memberInfo.real_name != null ? memberInfo.real_name.Trim() : "";
                br.nick = memberInfo.nick != null ? memberInfo.nick : "";
                br.cell = memberInfo.cell != null ? memberInfo.cell : "";
                br.gender = memberInfo.gender != null ? memberInfo.gender : "";
                br.pay_method = "微信支付";
                br.mch_id = b.mch_id;
                br.out_trade_no = b.out_trade_no;
                br.TransactionId = b.TransactId;
                br.business_id = b.task_id;
                br.business_type = b.business.type;
                br.business_detail = b.business.description;
                br.is_test = 0;
                br.order_status = "";
                br.order_type = "";
                br.income_type = "";
                br.refund_times = b.refunds.Count;




                switch (br.type.Trim())
                {
                    case "收入":
                        try
                        {
                            br.income = double.Parse(b.income);
                        }
                        catch
                        {
                            br.income = 0;
                        }
                        try
                        {
                            br.income_fee = double.Parse(b.fee);
                        }
                        catch
                        {
                            br.income_fee = 0;
                        }
                        try
                        {
                            br.income_real = br.income - br.income_fee;
                        }
                        catch
                        {
                            br.income_real = 0;
                        }
                        try
                        {
                            br.refund_summary = double.Parse(b.total_refund);
                        }
                        catch
                        {
                            br.refund_summary = 0;
                        }
                        try
                        {
                            br.refund_fee_summary = double.Parse(b.total_refund_fee);
                        }
                        catch
                        {
                            br.refund_fee_summary = 0;
                        }
                        try
                        {
                            br.refund_real_summary = br.refund_summary + br.refund_fee_summary;
                        }
                        catch
                        {
                            br.refund_real_summary = 0;
                        }
                        try
                        {
                            br.total_summary = br.income_real - br.refund_real_summary;
                        }
                        catch
                        {
                            br.total_summary = 0;
                        }

                        for (int j = 0; j < b.refunds.Count; j++)
                        {
                            Refund r = b.refunds[j];
                            switch (j)
                            {
                                case 0:
                                    br.refund1_oper_open_id = r.openId;
                                    MemberInfo member1 = await GetUnicUser(r.openId);
                                    br.refund1_oper_name = member1.real_name.Trim();
                                    br.refund1_date = DateTime.Parse(r.date);
                                    br.refund1_time = r.time;
                                    br.refund1_amount = double.Parse(r.refund_amount);
                                    br.refund1_fee = double.Parse(r.return_fee);
                                    br.refund1_summary = br.refund1_amount + br.refund1_fee;
                                    br.refund1_no = r.wepay_refund_id;
                                    br.refund1_type = r.refund_type;
                                    break;
                                case 1:
                                    br.refund2_oper_open_id = r.openId;
                                    MemberInfo member2 = await GetUnicUser(r.openId);
                                    br.refund2_oper_name = member2.real_name.Trim();
                                    br.refund2_date = DateTime.Parse(r.date);
                                    br.refund2_time = r.time;
                                    br.refund2_amount = double.Parse(r.refund_amount);
                                    br.refund2_fee = double.Parse(r.return_fee);
                                    br.refund2_summary = br.refund1_amount + br.refund1_fee;
                                    br.refund2_no = r.wepay_refund_id;
                                    br.refund2_type = r.refund_type;
                                    break;
                                case 2:
                                    br.refund3_oper_open_id = r.openId;
                                    MemberInfo member3 = await GetUnicUser(r.openId);
                                    br.refund3_oper_name = member3.real_name.Trim();
                                    br.refund3_date = DateTime.Parse(r.date);
                                    br.refund3_time = r.time;
                                    br.refund3_amount = double.Parse(r.refund_amount);
                                    br.refund3_fee = double.Parse(r.return_fee);
                                    br.refund3_summary = br.refund1_amount + br.refund1_fee;
                                    br.refund3_no = r.wepay_refund_id;
                                    br.refund3_type = r.refund_type;
                                    break;
                                case 3:
                                    br.refund4_oper_open_id = r.openId;
                                    MemberInfo member4 = await GetUnicUser(r.openId);
                                    br.refund4_oper_name = member4.real_name.Trim();
                                    br.refund4_date = DateTime.Parse(r.date);
                                    br.refund4_time = r.time;
                                    br.refund4_amount = double.Parse(r.refund_amount);
                                    br.refund4_fee = double.Parse(r.return_fee);
                                    br.refund4_summary = br.refund1_amount + br.refund1_fee;
                                    br.refund4_no = r.wepay_refund_id;
                                    br.refund4_type = r.refund_type;
                                    break;
                                case 4:
                                    br.refund5_oper_open_id = r.openId;
                                    MemberInfo member5 = await GetUnicUser(r.openId);
                                    br.refund5_oper_name = member5.real_name.Trim();
                                    br.refund5_date = DateTime.Parse(r.date);
                                    br.refund5_time = r.time;
                                    br.refund5_amount = double.Parse(r.refund_amount);
                                    br.refund5_fee = double.Parse(r.return_fee);
                                    br.refund5_summary = br.refund1_amount + br.refund1_fee;
                                    br.refund5_no = r.wepay_refund_id;
                                    br.refund5_type = r.refund_type;
                                    break;
                                case 5:
                                    br.refund6_oper_open_id = r.openId;
                                    MemberInfo member6 = await GetUnicUser(r.openId);
                                    br.refund6_oper_name = member6.real_name.Trim();
                                    br.refund6_date = DateTime.Parse(r.date);
                                    br.refund6_time = r.time;
                                    br.refund6_amount = double.Parse(r.refund_amount);
                                    br.refund6_fee = double.Parse(r.return_fee);
                                    br.refund6_summary = br.refund1_amount + br.refund1_fee;
                                    br.refund6_no = r.wepay_refund_id;
                                    br.refund6_type = r.refund_type;
                                    break;
                                default:
                                    break;
                            }

                        }



                        break;
                    case "退款":
                        try
                        {
                            br.refund_amount = double.Parse(b.refund_amount);
                        }
                        catch
                        {
                            br.refund_amount = 0;
                        }
                        try
                        {
                            br.refund_fee = double.Parse(b.refund_fee);
                        }
                        catch
                        {
                            br.refund_fee = 0;
                        }
                        try
                        {
                            br.refund_real = br.refund_amount + br.refund_fee;
                        }
                        catch
                        {
                            br.refund_real = 0;
                        }
                        br.refund_oper_open_id = b.operOpenId;
                        br.refund_oper_name = (await GetUnicUser(b.operOpenId)).real_name.Trim();

                        break;
                    default:
                        break;
                }
                try
                {
                    await _context.AddAsync(br);
                    await _context.SaveChangesAsync();
                    num++;

                }
                catch(Exception err)
                {
                    
                }
                
            }
            return Ok(num);
        }

        [NonAction]
        public string GetLineString(Balance b, string headLine)
        {
            string s = "";
            string[] fields = headLine.Split(',');
            for (int i = 0; i < fields.Length; i++)
            {
                switch (fields[i])
                {
                    case "序号":
                        s += b.id.ToString() + ",";
                        break;
                    case "日期":
                        s += b.date.Replace(",", "，") + ",";
                        break;
                    case "时间":
                        s += b.time.Replace(",", "，") + ",";
                        break;
                    case "类别":
                        s += b.trans_type.Replace(",", "，") + ",";
                        break;
                    case "月份":
                        s += b.month.Replace(",", "，") + ",";
                        break;
                    case "运营区间":
                        s += b.season.Replace(",", "，") + ",";
                        break;
                    case "支付渠道":
                        s += b.payMethod.Replace(",", "，") + ",";
                        break;
                    case "渠道商户号":
                        s += b.mch_id.Replace(",", "，") + ",";
                        break;
                    case "商户订单号":
                        s += b.out_trade_no.Replace(",", "，") + ",";
                        break;
                    case "支付订单号":
                        s += b.TransactId.Replace(",", "，") + ",";
                        break;
                    case "退款次数":
                        s += b.refunds.Count.ToString().Replace(",", "，") + ",";
                        break;
                    case "收入类型":
                        s += (b.business==null? "-,":b.business.type.Replace(",", "，") + ",");
                        break;
                    case "业务明细":
                        s += (b.business == null ? "-," : b.business.description.Replace(",", "，") + ",");
                        break;
                    case "业务单号":
                        s += (b.business == null? "-," : b.business.id.Trim().Replace(",", "，") + ",");
                        break;
                    case "收入":
                        s += b.income.Replace(",", "，") + ",";
                        break;
                    case "手续费":
                        s += b.fee.Replace(",", "，") + ",";
                        break;
                    case "入账金额":
                        s += b.summary.Replace(",", "，") + ",";
                        break;
                    case "退款方式":
                        s += b.refund_type.Replace(",", "，") + ",";
                        break;
                    case "退款":
                        s += b.refund_amount.Replace(",", "，") + ",";
                        break;
                    case "手续费退回":
                        s += b.refund_fee.Replace(",", "，") + ",";
                        break;
                    case "出账金额":
                        s += b.refund_summary.Replace(",", "，") + ",";
                        break;
                    case "退款合计":
                        s += b.total_refund.Replace(",", "，") + ",";
                        break;
                    case "退回手术费合计":
                        s += b.total_refund_fee.Replace(",", "，") + ",";
                        break;
                    case "实际出账合计":
                        s += b.total_refund_summary.Replace(",", "，") + ",";
                        break;
                    case "支付订单当前结余":
                        s += b.total_summary.Replace(",", "，") + ",";
                        break;
                    case "昵称":
                        s += b.member.nick.Replace(",", "，") + ",";
                        break;
                    case "手机":
                        s += b.member.cell.Replace(",", "，") + ",";
                        break;
                    case "姓名":
                        s += b.member.real_name.Replace(",", "，") + ",";
                        break;
                    case "性别":
                        s += b.member.gender.Replace(",", "，") + ",";
                        break;
                    case "门店":
                        s += b.shop.Replace(",", "，") + ",";
                        break;
                    case "退回手续费合计":
                        s += b.total_refund_fee.Replace(",", "，") + ",";
                        break;
                    case "操作员":
                        s += b.oper.Trim().Replace(",", "，") + ",";
                        break;
                    default:
                        if (fields[i].StartsWith("退款"))
                        {
                            int index = int.Parse(fields[i].Substring(fields[i].Length - 1, 1)) - 1;
                            string cName = fields[i].Substring(0, fields[i].Length - 1);
                            switch (cName)
                            {
                                case "退款日期":
                                    if (b.refunds != null  && index < b.refunds.Count)
                                    {
                                        s += b.refunds[index].date.Replace(",", "，") + ",";
                                    }
                                    else
                                    {
                                        s += "-,";
                                    }
                                    break;
                                case "退款时间":
                                    if (b.refunds != null && index < b.refunds.Count)
                                    {
                                        s += b.refunds[index].time.Replace(",", "，") + ",";
                                    }
                                    else
                                    {
                                        s += "-,";
                                    }
                                    break;
                                case "退款单号":
                                    if (b.refunds != null && index < b.refunds.Count)
                                    {
                                        s += b.refunds[index].wepay_refund_id.Replace(",", "，") + ",";
                                    }
                                    else
                                    {
                                        s += "-,";
                                    }
                                    break;
                                case "退款金额":
                                    if (b.refunds != null && index < b.refunds.Count)
                                    {
                                        s += b.refunds[index].refund_amount.Replace(",", "，") + ",";
                                    }
                                    else
                                    {
                                        s += "-,";
                                    }
                                    break;
                                case "退款返回手续费":
                                    if (b.refunds != null && index < b.refunds.Count)
                                    {
                                        s += b.refunds[index].return_fee.Replace(",", "，") + ",";
                                    }
                                    else
                                    {
                                        s += "-,";
                                    }
                                    break;
                                case "退款实际出账":
                                    if (b.refunds != null && index < b.refunds.Count)
                                    {
                                        s += b.refunds[index].summary.Replace(",", "，") + ",";
                                    }
                                    else
                                    {
                                        s += "-,";
                                    }
                                    break;
                                case "退款方式":
                                    if (b.refunds != null && index < b.refunds.Count)
                                    {
                                        s += b.refunds[index].refund_type.Replace(",", "，") + ",";
                                    }
                                    else
                                    {
                                        s += "-,";
                                    }
                                    break;
                                case "退款操作员":
                                    if (b.refunds != null && index < b.refunds.Count)
                                    {
                                        s += b.refunds[index].oper.Replace(",", "，") + ",";
                                    }
                                    else
                                    {
                                        s += "-,";
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                        break;
                }
            }
            if (s.EndsWith(","))
            {
                s = s.Substring(0, s.Length - 1);

            }
            return s;
        }




        public WepayBillReportController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
		{
            _context = context;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
        }




        [NonAction]
        public async Task<MemberInfo> GetUnicUser(string openId)
        {
            MemberInfo m = new MemberInfo()
            {
                nick = "",
                real_name = "",
                gender = "",
                cell = "",
                oaOpenId = "",
                oaOldOpenId = "",
                miniOpenId = ""
            };
            var user = await Models.Users.UnicUser.GetUnicUser(openId, "snowmeet_mini", _context);
            //Models.Users.UnicUser user = (Models.Users.UnicUser)((OkObjectResult)().Result).Value;
            if (user == null)
            {
                user = await Models.Users.UnicUser.GetUnicUser(openId, "snowmeet_official_account_new", _context);
            }
            if (user == null)
            {
                user = await Models.Users.UnicUser.GetUnicUser(openId, "snowmeet_official_account", _context);
            }

            if (user != null)
            {
                UnicUser realUser = (UnicUser)(user.Value);
                if (realUser.miniAppUser != null)
                {
                    m.miniOpenId = realUser.miniAppUser.open_id;
                    m.real_name = realUser.miniAppUser.real_name.Trim();
                    m.cell = realUser.miniAppUser.cell_number.Trim();
                    m.gender = realUser.miniAppUser.gender;
                    m.nick = realUser.miniAppUser.nick;
                }
                if (realUser.officialAccountUser != null)
                {
                    m.oaOpenId = realUser.officialAccountUser.open_id.Trim();
                    if (m.real_name.Equals(""))
                    {
                        m.real_name = realUser.officialAccountUser.real_name.Trim();
                    }
                    if (m.cell.Equals(""))
                    {
                        m.cell = realUser.officialAccountUser.cell_number.Trim();
                    }
                    if (m.nick.Equals(""))
                    {
                        m.nick = realUser.officialAccountUser.nick;
                    }
                }
                if (realUser.officialAccountOpenIdOld != null)
                {
                    m.oaOldOpenId = realUser.officialAccountOpenIdOld.Trim();

                }
                if (m.cell.Trim().Equals("") || m.real_name.Equals(""))
                {
                    var bl = await _context.MaintainLives.Where(a => (a.open_id.Trim().Equals(m.miniOpenId) || a.open_id.Trim().Equals(m.oaOpenId)) && !a.open_id.Trim().Equals("")).AsNoTracking().ToListAsync();
                    if (bl != null && bl.Count > 0)
                    {
                        if (m.cell.Equals(""))
                        {
                            m.cell = bl[0].confirmed_cell;
                        }
                        if (m.real_name.Trim().Equals(""))
                        {
                            m.real_name = bl[0].confirmed_name;
                        }
                    }
                }
                if (m.cell.Trim().Equals("") || m.real_name.Equals(""))
                {
                    var bl = await _context.RentOrder.Where(a => (a.open_id.Trim().Equals(m.miniOpenId) || a.open_id.Trim().Equals(m.oaOpenId)) && !a.open_id.Trim().Equals("")).AsNoTracking().ToListAsync();
                    if (bl != null && bl.Count > 0)
                    {
                        if (m.cell.Equals(""))
                        {
                            m.cell = bl[0].cell_number;
                        }
                        if (m.real_name.Trim().Equals(""))
                        {
                            m.real_name = bl[0].real_name;
                        }
                    }
                }
                return m;
            }
            else
            {
                string cell = "";
                string name = "";
                var oL = await _context.OrderOnlines.Where(o => o.open_id.Trim().Equals(openId.Trim())).ToListAsync();
                if (oL != null || oL.Count > 0)
                {

                    cell = oL[0].cell_number.Trim();
                    name = oL[0].name.Trim();
                }
                return new MemberInfo()
                {
                    miniOpenId = "",
                    oaOldOpenId = openId.Trim(),
                    cell = cell,
                    real_name = name
                };
            }
            
        }



        [NonAction]
        public async Task<MemberInfo> GetMemberInfo(string openId)
        {

            //Models.Users.UnicUser user

            Models.Users.MiniAppUser mUser = await _context.MiniAppUsers.FindAsync(openId);
            if (mUser != null)
            {
                return new MemberInfo()
                {
                    cell = mUser.cell_number.Trim(),
                    real_name = mUser.real_name.Trim(),
                    nick = mUser.nick.Trim(),
                    gender = mUser.gender.Trim()
                };
            }
            else
            {
                Models.Users.OfficialAccoutUser oUser = await _context.officialAccoutUsers.FindAsync(openId.Trim());
                if (oUser != null)
                {
                    return new MemberInfo()
                    {
                        cell = oUser.cell_number.Trim(),
                        nick = oUser.nick.Trim(),
                        real_name = oUser.real_name.Trim(),
                        gender = oUser.gender.Trim()
                    };
                }
                else
                {
                    return new MemberInfo()
                    {
                        cell = "",
                        nick = "",
                        gender = "",
                        real_name = ""
                    };
                }
            }
        }
        

       
        [NonAction]
        public async Task<Balance[]> GetBalance(string mch_id)
        {
            var list = await _context.wepayTransaction

                .Where(

                t => //t.mch_id.Trim().Equals(mch_id.Trim())
                 true || t.id == 6899 || t.id == 6904 || t.id == 6901 || t.id == 6915 ||t.id == 6916
                 //t.mch_id.Trim().Equals("1615235183")

                //&& t.out_trade_no.Length > 5
                //&& t.out_trade_no.Trim().Equals("03013953235577216652")
                //&& t.trans_date.StartsWith("2023")

                )

                .OrderBy(t => t.trans_date)
                .AsNoTracking().ToListAsync();
            Balance[] bArr = new Balance[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                WepayTransaction l = list[i];
                Console.WriteLine(i.ToString() + "\t" + l.out_trade_no);
                Balance b = new Balance();
                b.id = i + 1;
                DateTime tDate = DateTime.Parse(l.trans_date);
                b.date = tDate.Year.ToString() + "-" + tDate.Month.ToString().PadLeft(2, '0') + "-" + tDate.Day.ToString().PadLeft(2, '0');
                b.time = tDate.Hour.ToString().PadLeft(2, '0') + ":" + tDate.Minute.ToString().PadLeft(2, '0') + ":" + tDate.Second.ToString().PadLeft(2, '0');
                b.month = tDate.Month.ToString();
                b.season = GetSeason(tDate);
                b.mch_id = l.mch_id.Trim();
                b.out_trade_no = l.out_trade_no.Trim();
                b.TransactId = l.wepay_order_id.Trim();
                b.fee_rate = l.fee_rate;
                b.refunds = new List<Refund>();
                //b.member = await GetMemberInfo(l.open_id.Trim());
                b.open_id = l.open_id;
                //b.trans_type = l.trans_type.Trim();
                switch (l.trans_status.Trim())
                {
                    case "REFUND":
                        b.income = "-";
                        b.fee = "-";
                        b.summary = "-";
                        b.trans_type = "退款";
                        b.refund_type = l.out_refund_no.Trim().Length < 10 ? "API" : "后台";
                        b.oper = "";//await GetRefundOper(l);
                        b.operOpenId = await GetRefundOpenId(l);
                        b.open_id = l.open_id.Trim();
                        DateTime rDate = DateTime.Parse(l.trans_date);
                        Refund r = new Refund()
                        {
                            date = rDate.Year.ToString() + "-" + rDate.Month.ToString().PadLeft(2, '0') + "-" + rDate.Day.ToString().PadLeft(2, '0'),
                            time = rDate.Hour.ToString().PadLeft(2, '0') + ":" + rDate.Minute.ToString().PadLeft(2, '0') + rDate.Second.ToString().PadLeft(2, '0'),
                            refund_type = b.refund_type.Trim(),
                            wepay_refund_id = l.wepay_refund_no,
                            out_refund_id = l.out_refund_no.Trim(),
                            refund_amount = l.refund_amount,
                            return_fee = l.fee,
                            status = l.refund_status,
                            summary = Math.Round(double.Parse(l.refund_amount) + double.Parse(l.fee), 2).ToString(),
                            oper = b.oper,
                            openId = l.open_id

                        };
                        for (int j = 0; j < bArr.Length; j++)
                        {
                            
                            if (bArr[j] != null && bArr[j].TransactId.Trim().Equals(l.wepay_order_id) && bArr[j].out_trade_no.Trim().Equals(l.out_trade_no.Trim()))
                            {
                                bArr[j].refunds.Add(r);
                                double totalRefund = 0;
                                double totalRefundFee = 0;
                                for (int k = 0; k < bArr[j].refunds.Count; k++)
                                {
                                    totalRefund += double.Parse(bArr[j].refunds[k].refund_amount);
                                    totalRefundFee += double.Parse(bArr[j].refunds[k].return_fee);
                                }
                                bArr[j].total_refund = Math.Round(totalRefund, 2).ToString();
                                bArr[j].total_refund_fee = Math.Round(totalRefundFee, 2).ToString();
                                bArr[j].total_refund_summary = Math.Round(double.Parse(bArr[j].total_refund) + double.Parse(bArr[j].total_refund_fee), 2).ToString();
                                bArr[j].total_summary = Math.Round(double.Parse(bArr[j].summary) - totalRefund - totalRefundFee, 2).ToString();
                                b.shop = bArr[j].shop;
                                b.business = bArr[j].business;
                                b.member = bArr[j].member;
                                break;
                            }
                  
                        }
                        b.refund_amount = l.refund_amount.Trim();
                        b.refund_fee = l.fee;
                        b.refund_summary = Math.Round(double.Parse(b.refund_amount) + double.Parse(b.refund_fee), 2).ToString();
                        //b.refund_type = "";
                        b.total_refund = "-";
                        b.total_refund_fee = "-";
                        b.total_summary = "-";
                        
                        
                        break;
                    default:
                        b.trans_type = "收入";
                        b.income = l.settled_amount.Trim();
                        b.fee = l.fee.Trim();
                        b.summary = Math.Round(double.Parse(b.income) - double.Parse(b.fee), 2).ToString();
                        b.refund_amount = "-";
                        b.refund_fee = "-";
                        b.refund_summary = "";
                        b.refund_type = "-";
                        OrderOnline order = await FindOrder(b.out_trade_no.Trim());
                        if (order != null)
                        {
                            /*
                            if (order.staff_open_id == null)
                            {
                                b.oper = "";
                            }
                            else
                            {
                                MemberInfo operMember = await GetMemberInfo(order.staff_open_id.Trim());
                                if (operMember != null)
                                {

                                    b.oper = operMember.real_name.Trim();
                                }
                                else
                                {
                                    b.oper = "";
                                }
                            }
                            */
                            
                            b.business = await GetBusiness(order);
                            b.shop = order.shop;
                            if (b.business != null && b.member != null)
                            {
                                if (b.member.real_name.Trim().Equals(""))
                                {
                                    b.member.real_name = b.business.name.Trim();
                                }
                                if (b.member.cell.Trim().Equals(""))
                                {
                                    b.member.cell = b.business.cell.Trim();
                                }
                            }

                            b.oper = "";// await GetOrderOper(order, b.business);
                            b.operOpenId = await GetOrderOperOpenId(order, b.business);

                        }
                        b.summary = Math.Round(double.Parse(b.income.Trim()) - double.Parse(b.fee), 2).ToString();
                        b.total_refund = "0";
                        b.total_refund_fee = "0";
                        b.total_summary = b.summary;
                        break;
                }

                bArr[i] = b;
                /*
                bool ret = await SaveToDb(b);
                if (!ret)
                {
                    throw new Exception("save db error line " + i.ToString());
                    //break;
                }
                */
            }
            return bArr;
        }

        [NonAction]
        public async Task<string> GetOrderOperOpenId(OrderOnline order, BusinessInfo business)
        {
            string openId = "";
            //string name = "";
            if (order.staff_open_id != null)
            {
                openId = order.staff_open_id.Trim();
            }
            else
            {
                if (business.id != null && !business.id.Equals("0"))
                {
                    int id = 0;
                    try
                    {
                        id = int.Parse(business.id);
                    }
                    catch
                    {
                        return "";
                    }
                    switch (business.type)
                    {
                        case "试滑":
                            Experience e = await _context.Experience.FindAsync(id);
                            openId = e.staff_open_id.Trim();
                            break;
                        case "服务":
                            MaintainLive m = await _context.MaintainLives.FindAsync(id);
                            openId = m.service_open_id.Trim();
                            break;
                        case "租赁":
                            Models.Rent.RentOrder r = await _context.RentOrder.FindAsync(id);
                            openId = r.staff_open_id.Trim();
                            break;
                        default:
                            break;
                    }


                }

            }
            return openId.Trim();
        }

        [NonAction]
        public async Task<string> GetOrderOper(OrderOnline order, BusinessInfo business)
        {
            string openId = await GetOrderOperOpenId(order, business);
            string name = (await GetMemberInfo(openId)).real_name.Trim();
            return name;
        }

        [NonAction]
        public async Task<string> GetRefundOpenId(WepayTransaction l)
        {
            string openId = "";
            var newList = await _context.OrderPaymentRefund.Where(o => o.refund_id.Trim().Equals(l.wepay_refund_no)).AsNoTracking().ToListAsync();
            if (newList != null && newList.Count > 0)
            {
                openId = newList[0].oper.Trim();
            }
            if (openId.Trim().Equals(""))
            {
                var oldList = await _context.WePayOrderRefund
                    .Where(o => o.status.Trim().Equals(l.wepay_refund_no) || o.wepay_out_trade_no.Trim().Equals(l.out_trade_no))
                    .AsNoTracking().ToListAsync();
                if (oldList != null && oldList.Count > 0)
                {
                    openId = oldList[0].oper_open_id.Trim();
                }
            }
            return openId;
        }

        [NonAction]
        public async Task<string> GetRefundOper(WepayTransaction l)
        {
            
            MemberInfo memberInfo = await GetMemberInfo(await GetRefundOpenId(l));
            if (memberInfo == null)
            {
                return "";
            }
            else
            {
                return memberInfo.real_name.Trim();
            }
        }

        [NonAction]
        public async Task<int> GetOrderIdOld(string outTradeNo)
        {
            var l = await _context.WepayOrders.FromSqlRaw(" select convert(int,order_product_id) as order_id, convert(varchar(50), order_out_trade_no) as out_trade_no,  "
                + " order_total_fee / 100 as amount, order_openid as open_id, '' as description, order_appid as appid , '' as notify, '' as nonce, '' as sign,  "
                + " '' as timestamp, 0 as state, 0 as mch_id, '' as prepay_id , order_appid as app_id "
                + " from weixin_payment_orders"
                + " where order_out_trade_no = '" + outTradeNo.Trim() + "' ").AsNoTracking().ToListAsync();
            if (l == null || l.Count == 0)
            {
                return 0;
            }
            else
            {
                return l[0].order_id;
            }
        }

        [NonAction]
        public async Task<int> GetOrderId(string outTradeNo)
        {
            var l = await _context.WepayOrders.Where(o => o.out_trade_no.Trim().Equals(outTradeNo.Trim())).AsNoTracking().ToListAsync();
            if (l == null || l.Count == 0)
            {
                return 0;
            }
            else
            {
                return l[0].order_id;
            }
        }




        [NonAction]
        public async Task<OrderOnline> FindOrder(string outTradeNo)
        {
            var orderList = await _context.OrderOnlines.Where(o => o.out_trade_no.Trim().Equals(outTradeNo.Trim())).ToListAsync();
            if (orderList != null && orderList.Count > 0)
            {
                return orderList[0];
            }
            int orderId = await GetOrderId(outTradeNo);
            if (orderId == 0)
            {
                orderId = await GetOrderIdOld(outTradeNo);
            }
            if (orderId == 0)
            {
                var l = await _context.OrderPayment.Where(o => o.out_trade_no.Trim().Equals(outTradeNo)).ToListAsync();
                if (l != null && l.Count > 0)
                {
                    orderId = l[0].order_id;
                }
            }
            if (orderId == 0)
            {
                try
                {
                    return await _context.OrderOnlines.FindAsync(int.Parse(outTradeNo));
                }
                catch
                {
                    return null;
                }
            }
            else
            {
                return await _context.OrderOnlines.FindAsync(orderId);
            }
           
        }

        [NonAction]
        public async Task<BusinessInfo> GetBusiness(OrderOnline order)
        {
            BusinessInfo bi = new BusinessInfo()
            {
                type = "未知业务",
                id = "0"
            };
            switch (order.type.Trim())
            {
                case "服务卡":
                    bi = await GetBusinessForCard(order);
                    break;
                case "押金":
                    bi = await GetDepositInfo(order);
                    break;
                case "服务":
                    bi = await GetMaintainInfo(order);
                    break;
                case "雪票":
                    bi = await GetSkiPassInfo(order);
                    break;
                case "店销现货":
                case "店销":
                    bi = await GetSaleInfo(order);
                    break;
                default:
                    bi.type = order.type.Trim();
                    bi.id = "0";
                    break;
            }
            return bi;
        }

        [NonAction]
        public async Task<BusinessInfo> GetSaleInfo(OrderOnline order)
        {
            BusinessInfo bi = new BusinessInfo()
            {
                type = "店销现货",
                id = order.id.ToString(),
                description = "",
                name = "",
                cell = ""
            };
            var mi7L = await _context.mi7Order.Where(m => m.order_id == order.id)
                .AsNoTracking().ToListAsync();
            string desc = "";
            for (int i = 0; i < mi7L.Count; i++)
            {
                if (mi7L[i].mi7_order_id != null)
                {
                    desc += mi7L[i].mi7_order_id + ",";
                }
            }
            if (desc.EndsWith(","))
            {
                desc = desc.Substring(0, desc.Length - 1);
            }
            bi.description = desc;
            return bi;
        }

        [NonAction]
        public async Task<BusinessInfo> GetMaintainInfo(OrderOnline order)
        {
            BusinessInfo bi = new BusinessInfo()
            {
                id = "",
                type = order.type.Trim()
            };
            var l = await _context.MaintainLives.Where(m => m.order_id == order.id).ToListAsync();
            string desc = "";
            if (l != null && l.Count > 0)
            {

                for (int i = 0; i < l.Count; i++)
                {
                    string subDesc = l[i].confirmed_brand + "~" + l[i].confirmed_serial + "~" + l[i].confirmed_scale
                        + "~" + (l[i].confirmed_edge == 1 ? "修刃" : "") + "~" + (l[i].confirmed_candle == 1 ? "打蜡" : "")
                        + "~" + l[i].confirmed_more.Trim();
                    desc += subDesc + "|";

                }
                desc = desc.Substring(0, desc.Length - 1);
                bi.type = "养护";
                bi.id = l[0].id.ToString();
                bi.name = l[0].confirmed_name.Trim();
                bi.cell = l[0].confirmed_cell.Trim();
                bi.description = desc;
                
            }
            return bi;
        }

        [NonAction]
        public async Task<BusinessInfo> GetSkiPassInfo(OrderOnline order)
        {
            BusinessInfo bi = new BusinessInfo()
            {
                id = "",
                type = order.type.Trim()
            };
            var l = await _context.OrderOnlineDetails.Where(o => o.OrderOnlineId == order.id).AsNoTracking().ToListAsync();
            if (l != null && l.Count > 0)
            {
                bi.id = order.code;
                bi.type = "雪票";
                bi.description = l[0].product_name.Trim();
                if (bi.description.Trim().Equals(""))
                {
                    Models.Product.Product p = await _context.Product.FindAsync(l[0].product_id);
                    if (p != null)
                    {
                        bi.description = p.name.Trim();
                    }
                }
            }
            return bi;
        }

        [NonAction]
        public async Task<BusinessInfo> GetDepositInfo(OrderOnline order)
        {
            BusinessInfo bi = new BusinessInfo()
            {
                type = order.type,
                id = ""
            };
            var expL = await _context.Experience
                .Where(e => e.guarantee_order_id == order.id).ToListAsync();
            if (expL != null && expL.Count > 0)
            {
                bi.type = "试滑";
                bi.id = expL[0].id.ToString();
                bi.cell = expL[0].cell_number.Trim();
                bi.description = expL[0].asset_name.Trim();

            }
            else
            {
                var rL = await _context.RentOrder.Where(r => r.order_id == order.id).AsNoTracking().ToListAsync();
                if (rL != null && rL.Count > 0)
                {
                    bi.type = "租赁";
                    bi.id = rL[0].id.ToString();
                    bi.name = rL[0].real_name.Trim();
                    bi.cell = rL[0].cell_number.Trim();
                    string desc = "";
                    var dtl = await _context.RentOrderDetail.Where(d => d.rent_list_id == rL[0].id).AsNoTracking().ToListAsync();
                    for (int i = 0; dtl != null && i < dtl.Count; i++)
                    {
                        desc += dtl[i].rent_item_class + "~" + dtl[i].rent_item_name + "~" + dtl[i].rent_item_code + "|";
                    }
                    desc = desc.Substring(0, desc.Length - 1);
                    bi.description = desc;
                }
            }
            return bi;
        }

        [NonAction]
        public async Task<BusinessInfo> GetBusinessForCard(OrderOnline order)
        {
            if (order.code.Trim().Equals(""))
            {
                return new BusinessInfo()
                {
                    id = "0",
                    type = order.type.Trim()
                };
            }
            else
            {
                var l = await _context.OrderOnlineDetails
                    .Where(d => d.OrderOnlineId == order.id).AsNoTracking().ToListAsync();
                if (l != null && l.Count > 0)
                {
                    return new BusinessInfo()
                    {
                        id = order.code.Trim(),
                        type = l[0].product_name.Trim()
                    };
                }
                else
                {
                    return new BusinessInfo()
                    {
                        id = order.code.Trim(),
                        type = "未知"
                    };
                }
            }
        }



        [NonAction]
        public string GetSeason(DateTime date)
        {
            int year = date.Year;
            if (date.Month >= 9)
            {
                return year.ToString() + "-" + (year + 1).ToString();
            }
            else
            {
                return (year - 1).ToString() + "-" + year.ToString();
            }
        }

        [HttpGet]
        public async Task<ActionResult<int>> ImportFiles(string directory)
        {
            int num = 0;
            string[] fileArr = Directory.GetFiles(directory);
            for (int i = 0; i < fileArr.Length; i++)
            {
                num += (int)((OkObjectResult)(await ImportTransData(fileArr[i].Trim())).Result).Value;
            }
            return Ok(num);
        }

        [HttpGet]
        public async Task<ActionResult<int>> ImportTransData(string path)
        {
            int num = 0;
            StreamReader sr = System.IO.File.OpenText(path);
            for (int i = 0; ; i++)
            {
                string line = await sr.ReadLineAsync();
                if (i == 0)
                {
                    continue;
                }
                if (line == null || !line.StartsWith("`"))
                {
                    break;
                }
                string[] lineArr = line.Split(',');
                try
                {
                    DateTime.Parse(lineArr[0].Replace("`", ""));
                }
                catch
                {
                    break;
                }
                WepayTransaction t = new WepayTransaction();
                for (int j = 0; j < lineArr.Length; j++)
                {
                    
                    lineArr[j] = lineArr[j].Trim().Replace("`", "");
                    switch (j)
                    {
                        case 0:
                            t.trans_date = lineArr[j];
                            break;
                        case 1:
                            t.official_account_id = lineArr[j];
                            break;
                        case 2:
                            t.mch_id = lineArr[j];
                            break;
                        case 3:
                            t.dated_mch_id = lineArr[j];
                            break;
                        case 4:
                            t.device_id = lineArr[j];
                            break;
                        case 5:
                            t.wepay_order_id = lineArr[j];
                            break;
                        case 6:
                            t.out_trade_no = lineArr[j];
                            break;
                        case 7:
                            t.open_id = lineArr[j];
                            break;
                        case 8:
                            t.trans_type = lineArr[j];
                            break;
                        case 9:
                            t.trans_status = lineArr[j];
                            break;
                        case 10:
                            t.trans_bank = lineArr[j];
                            break;
                        case 11:
                            t.currency = lineArr[j];
                            break;
                        case 12:
                            t.settled_amount = lineArr[j];
                            break;
                        case 13:
                            t.ticket_amount = lineArr[j];
                            break;
                        case 14:
                            t.wepay_refund_no = lineArr[j];
                            break;
                        case 15:
                            t.out_refund_no = lineArr[j];
                            break;
                        case 16:
                            t.refund_amount = lineArr[j];
                            break;
                        case 17:
                            t.ticket_refund_admount = lineArr[j];
                            break;
                        case 18:
                            t.refund_type = lineArr[j];
                            break;
                        case 19:
                            t.refund_status = lineArr[j];
                            break;
                        case 20:
                            t.good_name = lineArr[j];
                            break;
                        case 21:
                            t.good_data_package = lineArr[j];
                            break;
                        case 22:
                            t.fee = lineArr[j];
                            break;
                        case 23:
                            t.fee_rate = lineArr[j];
                            break;
                        case 24:
                            t.order_amount = lineArr[j];
                            break;
                        case 25:
                            t.request_refund_amount = lineArr[j];
                            break;
                        case 26:
                            t.fee_rate_memo = lineArr[j];
                            break;
                       
                       
                       
                        default:
                            break;
                    }

                }

                bool dup = true;
                if (t.trans_status.Trim().Equals("REFUND"))
                {
                    var list = await _context.wepayTransaction
                        .Where(p => p.trans_status.Equals("REFUND") && (p.out_refund_no.Equals(t.out_refund_no) || p.wepay_refund_no.Equals(t.wepay_refund_no)) )
                        .AsNoTracking().ToListAsync();
                    if (list == null || list.Count == 0)
                    {
                        dup = false;
                    }
                }
                else
                {
                    var list = await _context.wepayTransaction
                        .Where(p => !p.trans_status.Equals("REFUND") && (p.out_refund_no.Equals(t.out_trade_no) || p.wepay_order_id.Equals(t.wepay_order_id)))
                        .AsNoTracking().ToListAsync();
                    if (list == null || list.Count == 0)
                    {
                        dup = false;
                    }
                }

                
                if (!dup)
                {
                    await _context.wepayTransaction.AddAsync(t);
                    await _context.SaveChangesAsync();
                    num++;
                }
                else
                {
                    Console.WriteLine(line);
                }
                
            }
            return Ok(num);
        }

	}
}

