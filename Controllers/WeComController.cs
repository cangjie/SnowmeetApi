using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SnowmeetApi;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using wechat_miniapp_base.Models;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class WeComController : ControllerBase
    {
        public class CellValue
        {
            public string text { get; set; } = "";
        }
        public class CellFormat
        {
            public bool bold { get; set; } = false;
            public TextColor color { get; set; } = new TextColor();
        }
        public class TextColor
        {
            public int red { get; set; } = 0;
            public int green { get; set; } = 0;
            public int blue { get; set; } = 0;
            public int alpha { get; set; } = 255;
        }
        public class Cell
        {
            public CellValue cell_value { get; set; }
            public CellFormat cell_format { get; set; }
        }
        public class Row
        {
            public List<Cell> values { get; set; }
        }
        public class GridData
        {
            public int start_row { get; set; } = 0;
            public int start_column { get; set; } = 0;
            public List<Row> rows { get; set; }
        }
        public class UpdateRangeRequest
        {
            public string sheet_id { get; set; }
            public GridData grid_data { get; set; }
        }
        public class UpdateOperation
        {
            public UpdateRangeRequest? update_range_request { get; set; } = null;
        }
        public class BatchUpdateRequest
        {
            public string docid { get; set; }
            public List<UpdateOperation> requests { get; set; }
        }
        public class WeComApiResponse
        {
            public int errcode { get; set; }
            public string errmsg { get; set; }
            public string? access_token { get; set; } = null;
            public FileList? file_list { get; set; } = null;
            public string? docid { get; set; } = null;
            public string? url { get; set; } = null;
            public List<SheetProperties>? properties { get; set; } = null;
        }
        public class FileItem
        {
            public string? fileid { get; set; } = null;
            public string? file_name { get; set; } = null;
            public string? spaceid { get; set; } = null;
            public string? fatherid { get; set; } = null;
            public int? file_size { get; set; } = null;
            public int? ctime { get; set; } = null;
            public int? mtime { get; set; } = null;
            public int? file_type { get; set; } = null;
            public int? file_status { get; set; } = null;
            public string? url { get; set; } = null;


        }
        public class FileList
        {
            public List<FileItem> item { get; set; } = new List<FileItem>();
        }
        public class SheetProperties
        {
            public string? sheet_id { get; set; } = null;
            public string? title { get; set; } = null;
            public int row_count { get; set; } = 0;
            public int column_count { get; set; } = 0;
        }
        public class Summary
        {
            public string batchId { get; set; }
            public string type { get; set; }
            public string? accountName { get; set; } = null;
            public string? mchId { get; set; }
            public int count { get; set; }
            public DateTime? startDate { get; set; } = null;
            public DateTime? endDate { get; set; } = null;
            public DateTime? dataStartDate {get; set;} = null;
            public DateTime? dataEndDate {get; set;} = null;
        }
        public ApplicationDBContext _db;
        public IConfiguration _config;
        public IHttpContextAccessor _http;
        public string cropid = "ww3a46c4555ae069f9";
        public string cropSecret = "vz9XOlLgtY63pYqqHix9HYcsSey4H3J7drQ4xUDa59M";
        public string spaceId = "s.ww3a46c4555ae069f9.767798597fsX";
        public string[] wepayBalanceFieldsArr = new string[] { "序号", "交易日期", "交易时间", "公众账号ID", "商户号", "特约商户号", "设备号", "微信订单号", "商户订单号", "用户标识", "交易类型", "付款银行", "货币种类", "应结订单金额", "代金券金额", "微信退款单号", "商户退款单号", "退款金额", "充值券退款金额", "退款状态", "商品名称", "商户数据包", "手续费", "费率", "订单金额", "申请退款金额", "费率备注", "导出批次", "导出日期", "导出时间" };
        public string[] wepayFundFieldsArr = new string[] { "序号", "记账日期", "记账时间", "商户号", "微信支付业务单号", "资金流水单号", "业务名称", "业务类型", "收支类型", "收支金额(元)", "账户结余(元)", "资金变更提交申请人", "备注", "业务凭证号", "导出批次", "导出日期", "导出时间" };
        public string wepayBalanceDirId = "s.ww3a46c4555ae069f9.767798597fsX_d.767972774CblA";
        public string wepayBalanceFileName = "【25-26】微信支付交易账单汇总";
        public string wepayDirId = "s.ww3a46c4555ae069f9.767798597fsX_d.767799479UX0M";
        public int[] mchIdArr = new int[] { 3, 5, 6, 7, 8, 9, 10, 11, 12, 15, 17 };
        //public int[] mchIdArr = new int[] { 12,15,17 };
        public string wepayFundDirId = "s.ww3a46c4555ae069f9.767798597fsX_d.7681092340bMJ";
        public string wepayFundFileName = "【25-26】微信支付资金账单汇总";
        public string aliFundDirId = "s.ww3a46c4555ae069f9.767798597fsX_d.767799545E4uh";
        public string aliFundFileName = "【25-26】支付宝资金账单";
        public string aliDocId = "dc8zAkVJdS6omNsO72YbEk9wWZtjd1NUvXF9NwtG7UaGHn60fgBYzxdCqDxMnlh0DD1eZM2M5_PxgPMer9CkLuAw";
        public string[] aliFields = new string[] { "序号", "账务流水号", "业务流水号", "商户订单号", "商品名称", "发生日期", "发生时间", "对方账号", "收入金额（+元）", "支出金额（-元）", "账户余额（元）", "交易渠道", "业务类型", "备注", "导出批次", "导出日期", "导出时间" };
        public string[] summaryFields = new string[] { "批次号", "账户名称", "商户号", "数据类型", "数据条数", "数据起始日期", "数据结束日期", "导出日期", "导出开始时间", "导出结束时间" };
        public string summarDocId = "";
        public string summaryDirId = "s.ww3a46c4555ae069f9.767798597fsX_d.767799247Qo73";
        public MiniAppHelperController _mH;
        public WeComController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
            _mH = new MiniAppHelperController(_db, _config);
        }
        [HttpGet]
        public async Task<ActionResult<List<Summary>>> GetSummary()
        {
            //List<Summary> sArr = new List<Summary>();
            List<Summary> wepayBalanceSummeryList = await _db.wepayBalance.Where(b => b.batch_id != null).GroupBy(b => new { b.batch_id, b.mch_id })
                .Select(s => new Summary()
                {
                    batchId = s.Key.batch_id,
                    mchId = s.Key.mch_id.ToString(),
                    count = s.Count(),
                }).ToListAsync();
            List<Summary> wepayFundSummeryList = await _db.wepayFlowBill.Where(b => b.batch_id != null).GroupBy(b => new { b.batch_id, b.mch_id })
                .Select(s => new Summary()
                {
                    batchId = s.Key.batch_id,
                    mchId = s.Key.mch_id.ToString(),
                    count = s.Count(),
                }).ToListAsync();
            List<Summary> aliSummeryList = await _db.aliDownloadFlowBill.Where(b => b.batch_id != null).GroupBy(b => new { b.batch_id })
                .Select(s => new Summary()
                {
                    batchId = s.Key.batch_id,
                    count = s.Count(),
                }).ToListAsync();
            List<Summary> sArr = new List<Summary>();
            for (int i = 0; i < wepayBalanceSummeryList.Count; i++)
            {
                Summary s = wepayBalanceSummeryList[i];
                s.type = "微信支付交易账单";
                sArr.Add(s);
            }
            for (int i = 0; i < wepayFundSummeryList.Count; i++)
            {
                Summary s = wepayFundSummeryList[i];
                s.type = "微信支付资金账单";
                sArr.Add(s);
            }
            for (int i = 0; i < aliSummeryList.Count; i++)
            {
                Summary s = aliSummeryList[i];
                s.type = "支付宝资金账单";
                sArr.Add(s);
            }
            List<WepayKey> keys = await _db.wepayKeys.AsNoTracking().ToListAsync();
            for (int i = 0; i < sArr.Count; i++)
            {
                if (sArr[i].type.StartsWith("微信"))
                {
                    WepayKey key = keys.Where(k => k.mch_id == sArr[i].mchId).FirstOrDefault();
                    if (key != null)
                    {
                        sArr[i].accountName = key.mch_name;
                    }
                }
                switch(sArr[i].type.Trim())
                {
                    case "微信支付交易账单":
                        List<WepayBalance> bArr = await _db.wepayBalance.Where(b => b.batch_id == sArr[i].batchId).AsNoTracking().OrderBy(b => b.trans_date).ToListAsync();
                        sArr[i].dataStartDate = bArr[0].trans_date.Date;
                        sArr[i].dataEndDate = bArr[bArr.Count - 1].trans_date.Date;
                        WebApiLog logStart = await _db.webApiLog.Where(l => l.id == bArr[0].wedoc_request_id).AsNoTracking().FirstOrDefaultAsync();
                        sArr[i].startDate = logStart.create_date;
                        WebApiLog logEnd = await _db.webApiLog.Where(l => l.id == bArr[bArr.Count - 1].wedoc_request_id).AsNoTracking().FirstOrDefaultAsync();
                        sArr[i].endDate = logEnd.create_date;
                        break;
                    case "微信支付资金账单":
                        List<WepayFlowBill> fArr = await _db.wepayFlowBill.Where(b => b.batch_id == sArr[i].batchId).AsNoTracking().OrderBy(b => b.bill_date_time).ToListAsync();
                        sArr[i].dataStartDate = fArr[0].bill_date_time.Date;
                        sArr[i].dataEndDate = fArr[fArr.Count - 1].bill_date_time.Date;
                        WebApiLog logStart1 = await _db.webApiLog.Where(l => l.id == fArr[0].wedoc_request_id).AsNoTracking().FirstOrDefaultAsync();
                        sArr[i].startDate = logStart1.create_date;
                        WebApiLog logEnd1 = await _db.webApiLog.Where(l => l.id == fArr[fArr.Count - 1].wedoc_request_id).AsNoTracking().FirstOrDefaultAsync();
                        sArr[i].endDate = logEnd1.create_date;
                        break;
                    default:
                        List<AliDownloadFlowBill> aArr = await _db.aliDownloadFlowBill.Where(b => b.batch_id == sArr[i].batchId).AsNoTracking().OrderBy(b => b.trans_date).ToListAsync();
                        sArr[i].dataStartDate = aArr[0].trans_date.Date;
                        sArr[i].dataEndDate = aArr[aArr.Count - 1].trans_date.Date;
                        WebApiLog logStarta = await _db.webApiLog.Where(l => l.id == aArr[0].wedoc_request_id).AsNoTracking().FirstOrDefaultAsync();
                        sArr[i].startDate = logStarta.create_date;
                        WebApiLog logEnda = await _db.webApiLog.Where(l => l.id == aArr[aArr.Count - 1].wedoc_request_id).AsNoTracking().FirstOrDefaultAsync();
                        sArr[i].endDate = logEnda.create_date;
                        break;
                }
            }
            return Ok(sArr.OrderBy(s => s.batchId).ToList());
        }
        [HttpGet]
        public async Task<ActionResult<int>> ExportAliDataAuto()
        {
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            string docId = aliDocId;
            string purpose = "支付宝";
            string token = await GetToken(batchId, purpose);
            List<SheetProperties> sheets = await GetSheetProperties(docId, token, batchId, purpose);
            await ExportAliData(sheets[0].sheet_id, docId, token, batchId, purpose, "更新数据");
            return Ok(0);
        }
        [HttpGet("{mchId}")]
        public async Task<ActionResult<int>> ExportWepayDataAuto(int mchId)
        {
            WepayKey key = await _db.wepayKeys.Where(k => k.id == mchId).AsNoTracking().FirstOrDefaultAsync();
            if (key == null)
            {
                return NotFound();
            }
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            string docId = key.doc_id;
            string purpose = "微信支付";
            string token = await GetToken(batchId, purpose);
            List<SheetProperties> sheets = await GetSheetProperties(docId, token, batchId, purpose);
            string? fundSheetId = null;
            string? balanceSheetId = null;
            for (int i = 0; i < sheets.Count; i++)
            {
                switch (sheets[i].title.Trim())
                {
                    case "资金账单":
                        fundSheetId = sheets[i].sheet_id;
                        break;
                    case "交易账单":
                        balanceSheetId = sheets[i].sheet_id;
                        break;
                    default:
                        break;
                }
            }
            if (balanceSheetId == null || fundSheetId == null)
            {
                return BadRequest();
            }
            await ExportWepayFundData(fundSheetId, docId, token, batchId, purpose, "更新数据", key.mch_id);
            await ExportWepayTransData(balanceSheetId, docId, token, batchId, purpose, "更新数据", key.mch_id);
            return Ok(0);
        }
        [HttpGet]
        public async Task<ActionResult<string>> CreateSummerySheetFile(string? token = null, string? batchId = null, string purpose = "导出记录", string memo = "")
        {
            if (batchId == null)
            {
                batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            }
            if (token == null)
            {
                token = await GetToken(batchId, purpose);
            }
            string fileName = "导出记录";
            string? fileId = await GetFileId(summaryDirId, fileName, batchId, token, purpose, memo);
            for (; fileId != null;)
            {
                await DeleteFile(fileId, token, purpose, memo, batchId);
                fileId = await GetFileId(summaryDirId, fileName, batchId, token, purpose, memo);
            }
            string docId = await CreateSheetDoc(summaryDirId, fileName, token, purpose, memo, batchId);
            string sheetName = "导出记录";
            List<SheetProperties> sheetProperties = await CreateSheet(docId, sheetName, 29, 5000, token, purpose, memo, batchId);
            string? sheetId = null;
            for (int j = 0; j < sheetProperties.Count; j++)
            {
                SheetProperties sp = sheetProperties[j];
                if (sp.title == "Sheet1")
                {
                    await DeleteSheet(docId, sp.sheet_id, token, purpose, memo, batchId);
                }
                if (sp.title == sheetName)
                {
                    sheetId = sp.sheet_id;
                }
            }
            if (sheetId == null)
            {
                return BadRequest();
            }
            await CreateTransTableTitle(sheetId, docId, token, batchId, purpose, memo, "summary");
            await FillBlank(2000, docId, token, purpose, memo, batchId, sheetId);
            return Ok(docId);
        }
        [HttpGet]
        public async Task<ActionResult<string>> CreateAliSheetFile(string? token = null, string? batchId = null, string purpose = "支付宝", string memo = "生成导出数据表格")
        {
            if (batchId == null)
            {
                batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            }
            if (token == null)
            {
                token = await GetToken(batchId, purpose);
            }
            string fileName = "【25-26】支付宝资金账单";
            string? fileId = await GetFileId(aliFundDirId, fileName, batchId, token, purpose, memo);
            for (; fileId != null;)
            {
                await DeleteFile(fileId, token, purpose, memo, batchId);
                fileId = await GetFileId(aliFundDirId, fileName, batchId, token, purpose, memo);
            }
            string docId = await CreateSheetDoc(aliFundDirId, fileName, token, purpose, memo, batchId);
            string sheetName = "交易账单";
            List<SheetProperties> sheetProperties = await CreateSheet(docId, sheetName, 29, 5000, token, purpose, memo, batchId);
            string? sheetId = null;
            for (int j = 0; j < sheetProperties.Count; j++)
            {
                SheetProperties sp = sheetProperties[j];
                if (sp.title == "Sheet1")
                {
                    await DeleteSheet(docId, sp.sheet_id, token, purpose, memo, batchId);
                }
                if (sp.title == sheetName)
                {
                    sheetId = sp.sheet_id;
                }
            }
            if (sheetId == null)
            {
                return BadRequest();
            }
            await CreateTransTableTitle(sheetId, docId, token, batchId, purpose, memo, "ali");
            await FillBlank(5000, docId, token, purpose, memo, batchId, sheetId);
            return Ok(docId);
        }
        [HttpGet("{mchId}")]
        public async Task<ActionResult<string>> CreateWepaySheetFile(int? mchId, string? token = null, string? batchId = null, string purpose = "微信支付", string memo = "分账户")
        {
            if (batchId == null)
            {
                batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            }
            if (token == null)
            {
                token = await GetToken(batchId, purpose);
            }
            WepayKey key = await _db.wepayKeys.Where(k => k.id == mchId).AsNoTracking().FirstOrDefaultAsync();
            string fileName = key.mch_id + "_" + key.mch_name;
            string? fileId = await GetFileId(wepayDirId, fileName, batchId, token, purpose, memo);
            for (; fileId != null;)
            {
                await DeleteFile(fileId, token, purpose, memo, batchId);
                fileId = await GetFileId(wepayDirId, fileName, batchId, token, purpose, memo);

            }
            string docId = await CreateSheetDoc(wepayDirId, fileName, token, purpose, memo, batchId);
            string? balanceSheetId = null;
            string? fundSheetId = null;
            await CreateSheet(docId, "交易账单", 29, 5000, token, purpose, memo, batchId);
            List<SheetProperties> sheetProperties = await GetSheetProperties(docId, token, batchId, purpose, memo);
            List<SheetProperties> sps = await CreateSheet(docId, "资金账单", 16, 5000, token, purpose, memo, batchId);
            for (int j = 0; j < sheetProperties.Count; j++)
            {
                SheetProperties sp = sheetProperties[j];
                if (sp.title == "Sheet1")
                {
                    await DeleteSheet(docId, sheetProperties[j].sheet_id, token, purpose, memo, batchId);
                }
            }

            for (int j = 0; j < sps.Count; j++)
            {
                switch (sps[j].title)
                {
                    case "资金账单":
                        fundSheetId = sps[j].sheet_id;
                        break;
                    case "交易账单":
                        balanceSheetId = sps[j].sheet_id;
                        break;
                    default:
                        break;
                }
            }
            if (balanceSheetId == null || fundSheetId == null)
            {
                return Ok(-2);
            }
            await CreateTransTableTitle(balanceSheetId, docId, token, batchId, purpose, memo);
            await CreateTransTableTitle(fundSheetId, docId, token, batchId, purpose, memo, "fund");
            await FillBlank(5000, docId, token, purpose, memo, batchId, balanceSheetId);
            await FillBlank(5000, docId, token, purpose, memo, batchId, fundSheetId);
            key.doc_id = docId;
            _db.wepayKeys.Entry(key).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(docId);
        }
        [NonAction]
        public async Task FillBlank(int maxLineCount, string docId, string token, string purpose, string memo, string batchId, string? sheetId = null)
        {
            string url = "https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/get_sheet_properties?access_token=" + token;
            string payload = "{ \"docid\": \"" + docId + "\" }  ";
            WebApiLog getFileLog = await _mH.PerformRequest(url, "", payload, "POST", "企业微信", purpose, memo, batchId);
            WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(getFileLog.response);
            if (res.errcode != 0)
            {
                return;
            }
            int rowCount = res.properties[0].row_count;
            int colCount = res.properties[0].column_count;
            int willFilledLines = maxLineCount - rowCount;
            BatchUpdateRequest batchUpdateRequest = new BatchUpdateRequest()
            {
                docid = docId,
                requests = new List<UpdateOperation>()
            };
            sheetId = sheetId == null ? sheetId = await GetSheetId(docId, token, batchId, "交易账单", "获取sheetid") : sheetId;
            UpdateRangeRequest updateRange = new UpdateRangeRequest()
            {
                sheet_id = sheetId,
                grid_data = new GridData()
            };
            UpdateOperation updateOperation = new UpdateOperation();
            updateOperation.update_range_request = updateRange;
            batchUpdateRequest.requests.Add(updateOperation);
            GridData gData = updateRange.grid_data;
            gData.start_column = 0;
            gData.start_row = rowCount + 1;
            gData.rows = new List<Row>();
            int nextStart = gData.start_row;
            for (int i = 0; i < willFilledLines; i++)
            {
                if (i % 200 == 0 && i > 0)
                {
                    string payloadRow = JsonConvert.SerializeObject(batchUpdateRequest);
                    Console.WriteLine("");
                    Console.WriteLine(payloadRow);
                    WebApiLog log = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payloadRow, "POST", "企业微信", purpose, memo, batchId);
                    //logs.Add(log);
                    gData.rows.Clear();
                    gData.start_row = nextStart;
                }
                nextStart++;
                Row row = new Row();
                row.values = new List<Cell>();
                for (int j = 0; j < colCount; j++)
                {
                    Cell cell = new Cell();
                    cell.cell_format = new CellFormat();
                    cell.cell_format.bold = false;
                    cell.cell_value = new CellValue();
                    cell.cell_value.text = "";
                    row.values.Add(cell);
                }
                gData.rows.Add(row);
            }
            string payloadLast = JsonConvert.SerializeObject(batchUpdateRequest);
            WebApiLog logFinal = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payloadLast, "POST", "企业微信", purpose, memo, batchId);
            //logs.Add(logFinal);
        }

        [NonAction]
        public async Task DeleteSheet(string docId, string sheetId, string token, string purpose, string memo = "", string? batchId = null)
        {
            string json = " { \"docid\": \"" + docId + "\", \"requests\": [ { \"delete_sheet_request\": { \"sheet_id\" : \"" + sheetId + "\" } }  ] }";
            await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", json, "POST", "企业微信", purpose, memo, batchId);
        }
        [NonAction]
        public async Task<List<SheetProperties>> CreateSheet(string docId, string sheetName, int colCount, int rowCount, string token, string purpose, string memo = "", string? batchId = null)
        {
            string json = " { \"docid\": \"" + docId + "\", \"requests\": [ { \"add_sheet_request\": { \"title\" : \"" + sheetName + "\", \"row_couont\": "
                + rowCount.ToString() + ", \"column_count\": " + colCount.ToString() + " } }  ] }";
            await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", json, "POST", "企业微信", purpose, memo, batchId);
            return await GetSheetProperties(docId, token, batchId, purpose, memo);

        }
        [NonAction]
        public async Task<string?> CreateSheetDoc(string fatherId, string fileName, string token, string purpose, string memo = "", string? batchId = null)
        {
            string url = "https://qyapi.weixin.qq.com/cgi-bin/wedoc/create_doc?access_token=" + token;
            string payload = "{	\"spaceid\": \"" + spaceId + "\" , \"fatherid\": \"" + fatherId + "\", \"doc_type\": 4, \"doc_name\": \"" + fileName
                + "\", \"admin_users\": [\"Snowmeet-CangJie_1\",  \"CuiYang\", \"ZhangKai\"] }";
            WebApiLog tokenLog = await _mH.PerformRequest(url, "", payload, "POST", "企业微信", purpose, memo, batchId);
            try
            {
                WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(tokenLog.response);
                if (res.errcode == 0)
                {
                    return res.docid;
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }
        [NonAction]
        public async Task<string?> GetToken(string batchId, string purpose)
        {
            string getTokenUrl = "https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpid=" + cropid + "&corpsecret=" + cropSecret;
            WebApiLog tokenLog = await _mH.PerformRequest(getTokenUrl, "", "", "GET", "企业微信", purpose, "获取Token", batchId);
            try
            {
                WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(tokenLog.response);
                if (res.errcode == 0)
                {
                    return res.access_token;
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }
        [NonAction]
        public async Task<List<SheetProperties>?> GetSheetProperties(string docId, string token, string batchId, string purpose = "", string memo = "")
        {
            string url = "https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/get_sheet_properties?access_token=" + token;
            string payload = "{ \"docid\": \"" + docId + "\" }";
            WebApiLog getFileLog = await _mH.PerformRequest(url, "", payload, "POST", "企业微信", purpose, memo, batchId);
            try
            {
                WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(getFileLog.response);
                return res.properties;
            }
            catch
            {
                return null;
            }
        }

        [NonAction]
        public async Task<string?> GetSheetId(string docId, string token, string batchId, string purpose = "", string memo = "")
        {
            string url = "https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/get_sheet_properties?access_token=" + token;
            string payload = "{ \"docid\": \"" + docId + "\" }";
            WebApiLog getFileLog = await _mH.PerformRequest(url, "", payload, "POST", "企业微信", purpose, memo, batchId);
            try
            {
                WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(getFileLog.response);
                return res.properties[0].sheet_id;
            }
            catch
            {
                return null;
            }
        }
        [NonAction]
        public async Task<string?> GetFileId(string fatherId, string fileName, string batchId, string token, string purpose = "", string memo = "")
        {
            string getUrl = "https://qyapi.weixin.qq.com/cgi-bin/wedrive/file_list?access_token=" + token;
            string payload = "{ \"spaceid\": \"" + spaceId + "\" , \"fatherid\": \"" + fatherId + "\" }";
            WebApiLog getFileLog = await _mH.PerformRequest(getUrl, "", payload, "POST", "企业微信", purpose, memo, batchId);
            WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(getFileLog.response);
            string fileId = null;
            for (int i = 0; res.file_list != null && res.file_list.item != null && fileId == null && i < res.file_list.item.Count; i++)
            {
                FileItem item = res.file_list.item[i];
                if (item.file_name == fileName)
                {
                    fileId = item.fileid;
                }
            }
            return fileId;
        }
        [NonAction]
        public async Task DeleteFile(string fileId, string token, string purpose, string memo = "", string? batchId = null)
        {
            string url = "https://qyapi.weixin.qq.com/cgi-bin/wedrive/file_delete?access_token=" + token;
            string payload = "{\"fileid\": [\"" + fileId + "\" ] }";
            await _mH.PerformRequest(url, "", payload, "POST", "企业微信", purpose, memo, batchId);
        }
        [NonAction]
        public async Task<bool> CheckNeedReCreateTransTable(string purpose = "交易账单")
        {
            //teTime nowDate = DateTime.Now.Date;
            string batchPrefix = DateTime.Now.ToString("yyyyMMdd");
            WebApiLog? lastLog = await _db.webApiLog.Where(l => l.source == "企业微信" && l.purpose == purpose && l.memo == "更新数据" && l.batch_id.StartsWith(batchPrefix))
                .OrderByDescending(l => l.create_date).AsNoTracking().FirstOrDefaultAsync();
            bool needRefresh = false;
            if (lastLog == null)
            {
                needRefresh = true;
            }
            else
            {
                string lastBatchId = lastLog.batch_id;
                List<WebApiLog> logs = await _db.webApiLog.Where(l => l.batch_id == lastBatchId).AsNoTracking().ToListAsync();
                bool withError = false;
                for (int i = 0; !withError && i < logs.Count; i++)
                {
                    try
                    {
                        string json = logs[i].response.Trim();
                        WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(json);
                        if (res.errcode != 0 || res.errmsg != "ok")
                        {
                            withError = true;
                        }
                    }
                    catch
                    {
                        withError = true;
                    }
                }
                if (withError)
                {
                    needRefresh = true;
                }
            }
            return needRefresh;
        }
        [NonAction]
        public async Task<WebApiLog> CreateTransTableTitle(string sheetId, string docId, string token, string batchId, string purpose, string memo, string type = "balance")
        {
            BatchUpdateRequest batchUpdateRequest = new BatchUpdateRequest()
            {
                docid = docId,
                requests = new List<UpdateOperation>()
            };
            UpdateRangeRequest updateRange = new UpdateRangeRequest()
            {
                sheet_id = sheetId,
                grid_data = new GridData()
            };
            UpdateOperation updateOperation = new UpdateOperation();
            updateOperation.update_range_request = updateRange;
            batchUpdateRequest.requests.Add(updateOperation);
            GridData gData = updateRange.grid_data;
            gData.start_column = 0;
            gData.start_row = 0;
            gData.rows = new List<Row>();
            Row row = new Row()
            {
                values = new List<Cell>()
            };
            string[] fields = wepayBalanceFieldsArr;
            switch (type)
            {
                case "fund":
                    fields = wepayFundFieldsArr;
                    break;
                case "ali":
                    fields = aliFields;
                    break;
                case "summary":
                    fields = summaryFields;
                    break;
                default:
                    break;
            }

            for (int i = 0; i < fields.Length; i++)
            {
                Cell cell = new Cell();
                CellFormat cf = new CellFormat()
                {
                    bold = true
                };
                CellValue cv = new CellValue()
                {
                    text = fields[i].Trim()
                };
                cell.cell_format = cf;
                cell.cell_value = cv;
                row.values.Add(cell);
            }
            gData.rows.Add(row);
            string json = JsonConvert.SerializeObject(batchUpdateRequest);
            Console.WriteLine(json);
            MiniAppHelperController _mHelper = new MiniAppHelperController(_db, _config);
            WebApiLog log = await _mHelper.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", json, "POST", "企业微信", purpose, memo, batchId);
            return log;
        }
        [NonAction]
        public async Task ExportAliData(string sheetId, string docId, string token, string batchId, string purpose, string memo)
        {
            DateTime startDate = DateTime.Parse("2025-10-15");
            List<AliDownloadFlowBill> bArr = await _db.aliDownloadFlowBill.Where(b => b.trans_date.Date >= startDate.Date && b.seq_id == null)
                .OrderBy(b => b.trans_date).ToListAsync();
            AliDownloadFlowBill last = await _db.aliDownloadFlowBill.Where(b => b.trans_date.Date >= startDate.Date && b.seq_id != null)
                .OrderByDescending(b => b.seq_id).AsNoTracking().FirstOrDefaultAsync();
            int seq = 1;
            if (last != null)
            {
                seq = ((int)last.seq_id) + 1;
            }
            BatchUpdateRequest batchUpdateRequest = new BatchUpdateRequest()
            {
                docid = docId,
                requests = new List<UpdateOperation>()
            };
            UpdateRangeRequest updateRange = new UpdateRangeRequest()
            {
                sheet_id = sheetId,
                grid_data = new GridData()
            };
            UpdateOperation updateOperation = new UpdateOperation();
            updateOperation.update_range_request = updateRange;
            batchUpdateRequest.requests.Add(updateOperation);
            GridData gData = updateRange.grid_data;
            gData.start_column = 0;
            gData.start_row = seq;
            gData.rows = new List<Row>();
            //gData.start_row = 1;
            int nextStart = seq;
            List<AliDownloadFlowBill> updateBatch = new List<AliDownloadFlowBill>();
            for (int i = 0; i < bArr.Count; i++)
            {
                if (i % 200 == 0 && i > 0)
                {
                    string payload = JsonConvert.SerializeObject(batchUpdateRequest);
                    WebApiLog log = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payload, "POST", "企业微信", purpose, memo, batchId);
                    gData.rows.Clear();
                    gData.start_row = nextStart;
                    try
                    {
                        WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(log.response.Trim());
                        if (res.errcode == 0)
                        {
                            for (int j = 0; j < updateBatch.Count; j++)
                            {
                                updateBatch[j].wedoc_request_id = log.id;
                                _db.aliDownloadFlowBill.Entry(updateBatch[j]).State = EntityState.Modified;
                            }
                            await _db.SaveChangesAsync();
                            updateBatch.Clear();
                            updateBatch = new List<AliDownloadFlowBill>();
                        }
                    }
                    catch
                    {
                        updateBatch.Clear();
                        updateBatch = new List<AliDownloadFlowBill>();
                    }
                }
                nextStart++;
                Row row = new Row();
                row.values = new List<Cell>();
                for (int j = 0; j < aliFields.Length; j++)
                {
                    Cell cell = new Cell();
                    cell.cell_format = new CellFormat();
                    cell.cell_format.bold = false;
                    cell.cell_value = new CellValue();
                    switch (aliFields[j].Trim())
                    {
                        case "序号":
                            cell.cell_value.text = seq.ToString();
                            bArr[i].seq_id = seq;
                            seq++;
                            break;
                        case "账务流水号":
                            cell.cell_value.text = bArr[i].id;
                            break;
                        case "发生日期":
                            DateTime transDate = DateTime.Parse(bArr[i].trans_date.ToString());
                            cell.cell_value.text = transDate.ToString("yyyy-MM-dd");
                            break;
                        case "发生时间":
                            DateTime transTime = DateTime.Parse(bArr[i].trans_date.ToString());
                            cell.cell_value.text = transTime.ToString("HH:mm:ss");
                            break;
                        case "业务流水号":
                            cell.cell_value.text = bArr[i].biz_num.ToString();
                            break;
                        case "商户订单号":
                            cell.cell_value.text = bArr[i].out_trade_num.ToString();

                            break;
                        case "商品名称":
                            cell.cell_value.text = bArr[i].prod_name.ToString();

                            break;
                        case "对方账号":
                            cell.cell_value.text = bArr[i].receiver_ali_account.ToString();

                            break;
                        case "收入金额（+元）":
                            cell.cell_value.text = bArr[i].income.ToString();

                            break;
                        case "支出金额（-元）":
                            cell.cell_value.text = bArr[i].outcome.ToString();

                            break;
                        case "账户余额（元）":
                            cell.cell_value.text = bArr[i].remainder.ToString();

                            break;
                        case "交易渠道":
                            cell.cell_value.text = bArr[i].trans_channel.ToString();

                            break;
                        case "业务类型":
                            cell.cell_value.text = bArr[i].biz_type.ToString();

                            break;
                        case "备注":
                            cell.cell_value.text = bArr[i].memo.ToString();

                            break;
                        case "导出批次":
                            cell.cell_value.text = batchId.Trim();
                            bArr[i].batch_id = batchId;
                            break;
                        case "导出日期":
                            cell.cell_value.text = DateTime.Now.ToShortDateString();
                            break;
                        case "导出时间":
                            cell.cell_value.text = DateTime.Now.ToShortTimeString();
                            break;
                        default:
                            break;
                    }
                    row.values.Add(cell);
                }
                gData.rows.Add(row);
                updateBatch.Add(bArr[i]);
            }
            string payloadLast = JsonConvert.SerializeObject(batchUpdateRequest);
            WebApiLog logFinal = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payloadLast, "POST", "企业微信", purpose, memo, batchId);
            for (int i = 0; i < updateBatch.Count; i++)
            {
                updateBatch[i].wedoc_request_id = logFinal.id;
                _db.aliDownloadFlowBill.Entry(updateBatch[i]).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
        }
        [NonAction]
        public async Task ExportWepayTransData(string sheetId, string docId, string token, string batchId, string purpose, string memo, string mchId)
        {
            DateTime startDate = DateTime.Parse("2025-10-15");
            List<WepayBalance> bArr = await _db.wepayBalance.Where(b => b.trans_date.Date >= startDate.Date && b.mch_id == mchId && b.seq_id == null)
                .OrderBy(b => b.trans_date).ToListAsync();
            WepayBalance last = await _db.wepayBalance.Where(b => b.trans_date.Date >= startDate.Date && b.mch_id == mchId && b.seq_id != null)
                .OrderByDescending(b => b.seq_id).AsNoTracking().FirstOrDefaultAsync();
            int seq = 1;
            if (last != null)
            {
                seq = ((int)last.seq_id) + 1;
            }
            BatchUpdateRequest batchUpdateRequest = new BatchUpdateRequest()
            {
                docid = docId,
                requests = new List<UpdateOperation>()
            };
            UpdateRangeRequest updateRange = new UpdateRangeRequest()
            {
                sheet_id = sheetId,
                grid_data = new GridData()
            };
            UpdateOperation updateOperation = new UpdateOperation();
            updateOperation.update_range_request = updateRange;
            batchUpdateRequest.requests.Add(updateOperation);
            GridData gData = updateRange.grid_data;
            gData.start_column = 0;
            gData.start_row = seq;
            gData.rows = new List<Row>();
            //gData.start_row = 1;
            int nextStart = seq;
            List<WepayBalance> updateBatch = new List<WepayBalance>();
            for (int i = 0; i < bArr.Count; i++)
            {
                if (i % 200 == 0 && i > 0)
                {
                    string payload = JsonConvert.SerializeObject(batchUpdateRequest);
                    WebApiLog log = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payload, "POST", "企业微信", purpose, memo, batchId);
                    gData.rows.Clear();
                    gData.start_row = nextStart;
                    try
                    {
                        WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(log.response.Trim());
                        if (res.errcode == 0)
                        {
                            for (int j = 0; j < updateBatch.Count; j++)
                            {
                                updateBatch[j].wedoc_request_id = log.id;
                                _db.wepayBalance.Entry(updateBatch[j]).State = EntityState.Modified;
                            }
                            await _db.SaveChangesAsync();
                            updateBatch.Clear();
                            updateBatch = new List<WepayBalance>();
                        }
                    }
                    catch
                    {
                        updateBatch.Clear();
                        updateBatch = new List<WepayBalance>();
                    }
                }
                nextStart++;
                Row row = new Row();
                row.values = new List<Cell>();
                for (int j = 0; j < wepayBalanceFieldsArr.Length; j++)
                {
                    Cell cell = new Cell();
                    cell.cell_format = new CellFormat();
                    cell.cell_format.bold = false;
                    if (bArr[i].refund_amount != 0)
                    {
                        cell.cell_format.color.red = 255;
                    }
                    cell.cell_value = new CellValue();
                    switch (wepayBalanceFieldsArr[j].Trim())
                    {
                        case "序号":
                            cell.cell_value.text = seq.ToString();
                            bArr[i].seq_id = seq;
                            seq++;
                            break;
                        case "交易日期":
                            DateTime transDate = DateTime.Parse(bArr[i].trans_date.ToString());
                            cell.cell_value.text = transDate.ToString("yyyy-MM-dd");
                            break;
                        case "交易时间":
                            DateTime transTime = DateTime.Parse(bArr[i].trans_date.ToString());
                            cell.cell_value.text = transTime.ToString("HH:mm:ss");
                            break;
                        case "公众账号ID":
                            cell.cell_value.text = bArr[i].app_id.ToString();
                            break;
                        case "商户号":
                            cell.cell_value.text = bArr[i].mch_id.ToString();
                            break;
                        case "特约商户号":
                            cell.cell_value.text = bArr[i].spc_mch_id.ToString();
                            break;
                        case "设备号":
                            cell.cell_value.text = bArr[i].device_id.ToString();
                            break;
                        case "微信订单号":
                            cell.cell_value.text = bArr[i].wepay_order_num.ToString();
                            break;
                        case "商户订单号":
                            cell.cell_value.text = bArr[i].out_trade_no.ToString();
                            break;
                        case "用户标识":
                            cell.cell_value.text = bArr[i].open_id.ToString();
                            break;
                        case "交易类型":
                            cell.cell_value.text = bArr[i].trans_type.ToString();
                            break;
                        case "交易状态":
                            cell.cell_value.text = bArr[i].pay_status.ToString();
                            break;
                        case "付款银行":
                            cell.cell_value.text = bArr[i].bank.ToString();
                            break;
                        case "货币种类":
                            cell.cell_value.text = bArr[i].currency.ToString();
                            break;
                        case "应结订单金额":
                            cell.cell_value.text = bArr[i].settle_amount.ToString();
                            break;
                        case "代金券金额":
                            cell.cell_value.text = bArr[i].coupon_amount.ToString();
                            break;
                        case "微信退款单号":
                            cell.cell_value.text = bArr[i].refund_no.ToString();
                            break;
                        case "商户退款单号":
                            cell.cell_value.text = bArr[i].out_refund_no.ToString();
                            break;
                        case "退款金额":
                            cell.cell_value.text = bArr[i].refund_amount.ToString();
                            break;
                        case "充值券退款金额":
                            cell.cell_value.text = bArr[i].coupon_refund_amount.ToString();
                            break;
                        case "退款状态":
                            cell.cell_value.text = bArr[i].refund_status.ToString();
                            break;
                        case "商品名称":
                            cell.cell_value.text = bArr[i].product_name.ToString();
                            break;
                        case "商户数据包":
                            cell.cell_value.text = bArr[i].product_package.ToString();
                            break;
                        case "手续费":
                            cell.cell_value.text = bArr[i].fee.ToString();
                            break;
                        case "费率":
                            cell.cell_value.text = bArr[i].fee_rate.ToString();
                            break;
                        case "订单金额":
                            cell.cell_value.text = bArr[i].order_amount.ToString();
                            break;
                        case "申请退款金额":
                            cell.cell_value.text = bArr[i].request_refund_amount.ToString();
                            break;
                        case "费率备注":
                            cell.cell_value.text = bArr[i].fee_rate_memo.ToString();
                            break;
                        case "导出批次":
                            cell.cell_value.text = batchId.Trim();
                            bArr[i].batch_id = batchId;
                            break;
                        case "导出日期":
                            cell.cell_value.text = DateTime.Now.ToShortDateString();
                            break;
                        case "导出时间":
                            cell.cell_value.text = DateTime.Now.ToShortTimeString();
                            break;
                        default:
                            break;
                    }
                    updateBatch.Add(bArr[i]);
                    row.values.Add(cell);
                }
                gData.rows.Add(row);
            }
            string payloadLast = JsonConvert.SerializeObject(batchUpdateRequest);
            WebApiLog logFinal = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payloadLast, "POST", "企业微信", purpose, memo, batchId);
            for (int i = 0; i < updateBatch.Count; i++)
            {
                updateBatch[i].wedoc_request_id = logFinal.id;
                _db.wepayBalance.Entry(updateBatch[i]).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
        }

        [NonAction]
        public async Task ExportWepayFundData(string sheetId, string docId, string token, string batchId, string purpose, string memo, string mchId)
        {
            DateTime startDate = DateTime.Parse("2025-10-15");
            List<WepayFlowBill> bArr = await _db.wepayFlowBill.Where(b => b.bill_date_time.Date >= startDate.Date && b.mch_id == mchId && b.seq_id == null)
                .OrderBy(b => b.bill_date_time).ToListAsync();
            WepayFlowBill last = await _db.wepayFlowBill.Where(b => b.bill_date_time.Date >= startDate.Date && b.mch_id == mchId && b.seq_id != null)
                .OrderByDescending(b => b.seq_id).AsNoTracking().FirstOrDefaultAsync();
            int seq = 1;
            if (last != null)
            {
                seq = ((int)last.seq_id) + 1;
            }
            BatchUpdateRequest batchUpdateRequest = new BatchUpdateRequest()
            {
                docid = docId,
                requests = new List<UpdateOperation>()
            };
            UpdateRangeRequest updateRange = new UpdateRangeRequest()
            {
                sheet_id = sheetId,
                grid_data = new GridData()
            };
            UpdateOperation updateOperation = new UpdateOperation();
            updateOperation.update_range_request = updateRange;
            batchUpdateRequest.requests.Add(updateOperation);
            GridData gData = updateRange.grid_data;
            gData.start_column = 0;
            gData.start_row = seq;
            gData.rows = new List<Row>();
            //gData.start_row = 1;
            int nextStart = seq;
            List<WepayFlowBill> updateBatch = new List<WepayFlowBill>();
            for (int i = 0; i < bArr.Count; i++)
            {
                if (i % 200 == 0 && i > 0)
                {
                    string payload = JsonConvert.SerializeObject(batchUpdateRequest);
                    WebApiLog log = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payload, "POST", "企业微信", purpose, memo, batchId);
                    gData.rows.Clear();
                    gData.start_row = nextStart;
                    try
                    {
                        WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(log.response.Trim());
                        if (res.errcode == 0)
                        {
                            for (int j = 0; j < updateBatch.Count; j++)
                            {
                                updateBatch[j].wedoc_request_id = log.id;
                                _db.wepayFlowBill.Entry(updateBatch[j]).State = EntityState.Modified;
                            }
                            await _db.SaveChangesAsync();
                            updateBatch.Clear();
                            updateBatch = new List<WepayFlowBill>();
                        }
                    }
                    catch
                    {
                        updateBatch.Clear();
                        updateBatch = new List<WepayFlowBill>();
                    }
                }
                nextStart++;
                Row row = new Row();
                row.values = new List<Cell>();
                for (int j = 0; j < wepayFundFieldsArr.Length; j++)
                {
                    Cell cell = new Cell();
                    cell.cell_format = new CellFormat();
                    cell.cell_format.bold = false;
                    cell.cell_value = new CellValue();
                    switch (wepayFundFieldsArr[j].Trim())
                    {
                        case "序号":
                            cell.cell_value.text = seq.ToString();
                            bArr[i].seq_id = seq;
                            seq++;
                            break;
                        case "商户号":
                            cell.cell_value.text = bArr[i].mch_id;
                            break;
                        case "记账日期":
                            DateTime transDate = DateTime.Parse(bArr[i].bill_date_time.ToString());
                            cell.cell_value.text = transDate.ToString("yyyy-MM-dd");
                            break;
                        case "记账时间":
                            DateTime transTime = DateTime.Parse(bArr[i].bill_date_time.ToString());
                            cell.cell_value.text = transTime.ToString("HH:mm:ss");
                            break;
                        case "微信支付业务单号":
                            cell.cell_value.text = bArr[i].biz_no.ToString();
                            break;
                        case "资金流水单号":
                            cell.cell_value.text = bArr[i].flow_no.ToString();
                            break;
                        case "业务名称":
                            cell.cell_value.text = bArr[i].biz_name.ToString();
                            break;
                        case "业务类型":
                            cell.cell_value.text = bArr[i].biz_type.ToString();
                            break;
                        case "收支类型":
                            cell.cell_value.text = bArr[i].bill_type.ToString();
                            break;
                        case "收支金额(元)":
                            cell.cell_value.text = bArr[i].amount.ToString();
                            break;
                        case "账户结余(元)":
                            cell.cell_value.text = bArr[i].surplus.ToString();
                            break;
                        case "资金变更提交申请人":
                            cell.cell_value.text = bArr[i].oper.ToString();
                            break;
                        case "备注":
                            cell.cell_value.text = bArr[i].memo.ToString();
                            break;
                        case "业务凭证号":
                            cell.cell_value.text = bArr[i].invoice_id.ToString();
                            break;
                        case "导出批次":
                            cell.cell_value.text = batchId.Trim();
                            bArr[i].batch_id = batchId;
                            break;
                        case "导出日期":
                            cell.cell_value.text = DateTime.Now.ToShortDateString();
                            break;
                        case "导出时间":
                            cell.cell_value.text = DateTime.Now.ToShortTimeString();
                            break;
                        default:
                            break;
                    }
                    row.values.Add(cell);
                }
                updateBatch.Add(bArr[i]);
                gData.rows.Add(row);
            }
            string payloadLast = JsonConvert.SerializeObject(batchUpdateRequest);
            WebApiLog logFinal = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payloadLast, "POST", "企业微信", purpose, memo, batchId);
            for (int i = 0; i < updateBatch.Count; i++)
            {
                updateBatch[i].wedoc_request_id = logFinal.id;
                _db.wepayFlowBill.Entry(updateBatch[i]).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
        }

    }
}