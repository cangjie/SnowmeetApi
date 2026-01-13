using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using System.IO;
using NPOI.XSSF.UserModel;
using System;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Formula.Functions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;
//using NPOI.SS.UserModel;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class ExcelController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private IConfiguration _config;
        public ExcelController(ApplicationDBContext context, IConfiguration config)
        {
            _db = context;
            _config = config;
        }

        [HttpPost]
        public async Task<ActionResult> UploadMi7Sheet(IFormFile sheetFile)
        {
            MemoryStream stream = new MemoryStream();
            await sheetFile.CopyToAsync(stream);
            stream.Position = 0;
            using (var workbook = new HSSFWorkbook((Stream)stream))
            {
                var sheet = workbook.GetSheetAt(0);
                List<List<string?>> sheetList = ConvertSheetToArray(sheet);
                await SaveMi7OrderSheet(sheetList);
                var sheetDetail = workbook.GetSheetAt(1);
                List<List<string?>> sheetDetailList = ConvertSheetToArray(sheetDetail);
                await SaveMi7OrderDetailSheet(sheetDetailList);
                //var cell = sheet.GetRow(0).GetCell(0);
                //Console.Write(cell.StringCellValue.Trim());
            }
            return Ok();
        }
        [NonAction]
        public List<List<string?>> ConvertSheetToArray(ISheet sheet)
        {
            List<List<string?>> result = new List<List<string>>();
            int rowIndex = 1;
            var row = sheet.GetRow(rowIndex);
            for (; row != null;)
            {
                List<string?> record = new List<string?>();
                int colIndex = 0;
                var cell = row.GetCell(colIndex);
                for (; cell != null;)
                {
                    switch (cell.CellType.ToString().Trim())
                    {
                        case "Numeric":
                            record.Add(cell.NumericCellValue.ToString().Trim());
                            break;
                        default:
                            record.Add(cell.StringCellValue.ToString().Trim());
                            break;
                    }
                    colIndex++;
                    cell = row.GetCell(colIndex);
                }
                result.Add(record);
                rowIndex++;
                row = sheet.GetRow(rowIndex);
            }
            return result;
        }
        [NonAction]
        public async Task SaveMi7OrderSheet(List<List<string?>> sheet)
        {
            for (int i = 0; i < sheet.Count; i++)
            {
                List<string?> row = sheet[i];
                string mi7Code = row[6].Trim();
                bool exists = false;
                Mi7ExportedSaleList ori = await _db.mi7ExportedSaleList.Where(m => m.mi7_code == mi7Code).AsNoTracking().FirstOrDefaultAsync();

                if (ori != null)
                {
                    exists = true;
                }
                Mi7ExportedSaleList l = new Mi7ExportedSaleList();
                for (int j = 0; j < row.Count; j++)
                {

                    switch (j)
                    {
                        case 0:
                            l.biz_date = DateTime.Parse(row[j].ToString());
                            break;
                        case 1:
                            l.deliver_method = row[j].ToString();
                            break;
                        case 2:
                            l.deliver_date = row[j].ToString().Trim() == "" ? null : DateTime.Parse(row[j]);
                            break;
                        case 3:
                            l.customer_contact = row[j].ToString();
                            break;
                        case 4:
                            l.contact_cell = row[j].ToString();
                            break;
                        case 5:
                            l.deliver_addr = row[j].ToString();
                            break;
                        case 6:
                            l.mi7_code = mi7Code;
                            break;
                        case 7:
                            l.shop = row[j].ToString();
                            break;
                        case 8:
                            l.associate_order_code = row[j].ToString();
                            break;
                        case 9:
                            l.customer_code = row[j].ToString();
                            break;
                        case 10:
                            l.customer_name = row[j].ToString();
                            break;
                        case 11:
                            l.customer_class = row[j].ToString();
                            break;
                        case 12:
                            l.product_name = row[j].ToString();
                            break;
                        case 13:
                            try
                            {
                                l.product_count = int.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.product_count = null;
                            }
                            break;
                        case 14:
                            try
                            {
                                l.order_discount = double.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.order_discount = null;
                            }
                            break;
                        case 15:
                            try
                            {
                                l.discount_amount = double.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.discount_amount = null;
                            }
                            break;
                        case 16:
                            try
                            {
                                l.others_charge = double.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.others_charge = null;
                            }
                            break;
                        case 17:
                            try
                            {
                                l.total_amount = double.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.total_amount = null;
                            }
                            break;
                        case 18:
                            try
                            {
                                l.take_off_amount = double.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.take_off_amount = null;
                            }
                            break;
                        case 19:
                            try
                            {
                                l.real_charge = double.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.real_charge = null;
                            }
                            break;
                        case 20:
                            try
                            {
                                l.gross_profit = double.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.gross_profit = null;
                            }
                            break;
                        case 21:
                            l.pay_mehtod = row[j].ToString();
                            break;
                        case 22:
                            l.recept_staff = row[j].ToString();
                            break;
                        case 23:
                            l.creater_staff = row[j].ToString();
                            break;
                        case 24:
                            l.charge_type = row[j].ToString();
                            break;
                        case 25:
                            l.store = row[j].ToString();
                            break;
                        case 26:
                            l.memo = row[j].ToString();
                            break;
                        case 27:
                            l.status = row[j].ToString();
                            break;
                        case 28:
                            l.invoice_status = row[j].ToString();
                            break;
                        case 29:
                            l.invoice_no = row[j].ToString();
                            break;
                        case 30:
                            l.inner_memo = row[j].ToString();
                            break;
                        case 31:
                            l.deliver_staff = row[j].ToString();
                            break;
                        case 32:
                            l.edited_status = row[j].ToString();
                            break;
                        case 33:
                            l.express_company = row[j].ToString();
                            break;
                        case 34:
                            l.way_bill_no = row[j].ToString();
                            break;
                        default:
                            break;
                    }

                }
                if (exists)
                {
                    l.create_date = ori.create_date;
                    l.update_date = DateTime.Now;
                    _db.mi7ExportedSaleList.Entry(l).State = EntityState.Modified;
                }
                else
                {
                    l.create_date = DateTime.Now;
                    await _db.mi7ExportedSaleList.AddAsync(l);
                }

            }
            await _db.SaveChangesAsync();
        }
        [NonAction]
        public async Task SaveMi7OrderDetailSheet(List<List<string?>> sheet)
        {
            for (int i = 0; i < sheet.Count; i++)
            {
                List<string?> row = sheet[i];
                string mi7Code = row[6].Trim();
                string productCode = row[12].Trim();
                string productName = row[13].Trim();
                bool exists = false;
                Mi7ExportedSaleDetail ori = await _db.mi7ExportedSaleDetail.Where(m => m.mi7_code == mi7Code && m.product_code == productCode && m.product_name == productName).AsNoTracking().FirstOrDefaultAsync();

                if (ori != null)
                {
                    exists = true;
                }
                Mi7ExportedSaleDetail l = new Mi7ExportedSaleDetail();
                for (int j = 0; j < row.Count; j++)
                {
                    switch (j)
                    {
                        case 0:
                            l.biz_date = DateTime.Parse(row[j].ToString());
                            break;
                        case 1:
                            l.deliver_method = row[j].ToString();
                            break;
                        case 2:
                            l.deliver_date = row[j].ToString().Trim() == "" ? null : DateTime.Parse(row[j]);
                            break;
                        case 3:
                            l.customer_contact = row[j].ToString();
                            break;
                        case 4:
                            l.contact_cell = row[j].ToString();
                            break;
                        case 5:
                            l.deliver_addr = row[j].ToString();
                            break;
                        case 6:
                            l.mi7_code = mi7Code;
                            break;
                        case 7:
                            l.shop = row[j].ToString();
                            break;
                        case 8:
                            l.associate_order_code = row[j].ToString();
                            break;
                        case 9:
                            l.customer_code = row[j].ToString();
                            break;
                        case 10:
                            l.customer_name = row[j].ToString();
                            break;
                        case 11:
                            l.customer_class = row[j].ToString();
                            break;
                        case 12:
                            l.product_code = row[j].ToString();
                            break;
                        case 13:
                            l.product_name = row[j].ToString();
                            break;
                        case 14:
                            l.product_category = row[j].ToString();
                            break;
                        case 15:
                            l.product_scale = row[j].ToString();
                            break;
                        case 16:
                            l.product_property = row[j].ToString();
                            break;
                        case 17:
                            l.product_unit = row[j].ToString();
                            break;
                        case 18:
                            l.product_barcode = row[j].ToString();
                            break;
                        case 19:
                            l.store = row[j].ToString();
                            break;
                        case 20:
                            try
                            {
                                l.count = int.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.count = null;
                            }
                            break;
                        case 21:
                            try
                            {
                                l.unit_price = double.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.unit_price = null;
                            }
                            break;
                        case 22:
                            try
                            {
                                l.unit_discount = double.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.unit_discount = null;
                            }
                            break;
                        case 23:
                            try
                            {
                                l.real_unit_price = double.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.real_unit_price = null;
                            }
                            break;
                        case 24:
                            try
                            {
                                l.total_amount = double.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.total_amount = null;
                            }
                            break;
                        case 25:
                            try
                            {
                                l.cost = double.Parse(row[j].ToString());
                            }
                            catch
                            {
                                l.cost = null;
                            }
                            break;
                        case 26:
                            l.weight = row[j].ToString();
                            break;
                        case 27:
                            l.cubage = row[j].ToString();
                            break;
                        case 28:
                            l.memo = row[j].ToString();
                            break;
                        case 29:
                            l.recept_staff = row[j].ToString();
                            break;
                        case 30:
                            l.creater_staff = row[j].ToString();
                            break;
                        case 31:
                            l.inner_memo = row[j].ToString();
                            break;
                        case 32:
                            l.express_company = row[j].ToString();
                            break;
                        case 33:
                            l.way_bill_no = row[j].ToString();
                            break;
                        default:
                            break;
                    }
                }
                if (exists)
                {
                    l.id = ori.id;
                    l.create_date = ori.create_date;
                    l.update_date = DateTime.Now;
                    _db.mi7ExportedSaleDetail.Entry(l).State = EntityState.Modified;
                }
                else
                {
                    l.id = 0;
                    l.create_date = DateTime.Now;
                    await _db.mi7ExportedSaleDetail.AddAsync(l);
                }
            }
            await _db.SaveChangesAsync();
        }

        [HttpGet]
        public void TestCreateExcel()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("Sheet1");
            sheet.CreateRow(0).CreateCell(0).SetCellValue("This is a test");
            sheet.CreateRow(1).CreateCell(0).SetCellValue("test to merge");
            sheet.CreateRow(2).CreateCell(0).SetCellValue("get merge value");
            sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(0, 1, 0, 0));
            Console.Write(sheet.GetRow(0).GetCell(0).StringCellValue);
            Console.Write(sheet.GetRow(1).GetCell(0).StringCellValue);

            using (var file = System.IO.File.Create("sample.xlsx"))
            {
                workbook.Write(file);
            }
        }
        [HttpGet]
        public void TestReadExcel()
        {
            //NPOIMemoryStream stream = new NPOIMemoryStream();
            MemoryStream stream = new MemoryStream();
            using (var file = System.IO.File.OpenRead("sale.xls"))
            {
                file.CopyTo(stream);
                stream.Position = 0;
                using (var workbook = new HSSFWorkbook((Stream)stream))
                {
                    var sheet = workbook.GetSheetAt(0);
                    var cell = sheet.GetRow(0).GetCell(0);
                    Console.Write(cell.StringCellValue.Trim());
                }

            }
        }
        [HttpGet]
        public string GetCellFormat()
        {
            MemoryStream stream = new MemoryStream();
            using (var file = System.IO.File.OpenRead("mi7.xlsx"))
            {
                file.CopyTo(stream);
                stream.Position = 0;
                using (var workbook = new XSSFWorkbook((Stream)stream))
                {
                    var sheet = workbook.GetSheetAt(0);
                    ICell cell = sheet.GetRow(2).GetCell(12);
                    //cell.CellType.
                    Console.Write(cell.StringCellValue.Trim());
                }

            }
            return "";
        }


    }
}