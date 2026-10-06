#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NPOI.XSSF.UserModel;
using SnowmeetApi.Models.Fnb;
namespace SnowmeetApi.Services.Fnb;

public sealed partial class FnbV4Service
{
    public async Task<object> Dashboard(DateTime? from, DateTime? to)
    {
        DateTime start = from?.Date ?? Today.AddDays(-((int)Today.DayOfWeek + 6) % 7), end = to?.Date ?? Today;
        if (start > end || (end - start).TotalDays > 366) throw new ArgumentException("报表日期范围须在 366 天以内");
        var movements = await (from m in db.fnbStockMovement join l in db.fnbStockDocumentLine on m.document_line_id equals l.id join d in db.fnbStockDocument on l.document_id equals d.id
            where d.shop_id == shopId && d.status == "posted" && d.business_date >= start && d.business_date <= end select new { m, d.document_type, d.business_date, d.document_no, d.id }).ToArrayAsync();
        var operations = await (from o in db.fnbStockOperation join d in db.fnbStockDocument on o.document_id equals d.id where d.shop_id == shopId && d.status == "posted" && d.business_date >= start && d.business_date <= end select new { o, d.business_date, d.document_no }).ToArrayAsync();
        var items = await db.fnbItem.ToArrayAsync(); var categories = await db.fnbCategory.ToArrayAsync();
        var ledger = new List<object>(); decimal lossCost = 0;
        foreach (var m in movements.Where(x => x.m.direction == -1 && x.document_type is "waste" or "destroy" or "stocktake"))
        {
            lossCost += m.m.amount; ledger.Add(new { sourceId = m.m.id.ToString(), sourceType = m.document_type, date = m.business_date, documentNo = m.document_no,
                itemId = m.m.item_id, name = items.Single(x => x.id == m.m.item_id).name, quantity = m.m.quantity, amount = m.m.amount });
        }
        foreach (var p in operations.Where(x => x.o.loss_base_qty > 0))
        {
            var inputs = movements.Where(x => x.id == p.o.document_id && x.m.direction == -1).ToArray(); decimal q = inputs.Sum(x => x.m.quantity);
            decimal amount = q == 0 ? 0 : Round(inputs.Sum(x => x.m.amount) * p.o.loss_base_qty / q); lossCost += amount;
            ledger.Add(new { sourceId = p.o.id.ToString(), sourceType = "op", date = p.business_date, documentNo = p.document_no, itemId = p.o.item_id,
                name = items.Single(x => x.id == p.o.item_id).name, quantity = p.o.loss_base_qty, amount });
        }
        var stocks = await Stock(); decimal cost = stocks.Sum(x => x.Amount), used = movements.Where(x => x.document_type == "serve" && x.m.direction == -1).Sum(x => x.m.amount);
        return new { from = start, to = end, lossAmount = Round(lossCost), serveCost = used,
            lossRate = lossCost + used == 0 ? 0 : Round(lossCost / (lossCost + used)), averageTurnoverDays = used == 0 ? (decimal?)null : Round(cost / (used / ((end - start).Days + 1))),
            inventoryCost = cost, costStructure = stocks.GroupBy(x => x.CategoryId).Select(g => new { categoryId = g.Key, categoryName = categories.Single(x => x.id == g.Key).name, amount = g.Sum(x => x.Amount) }).ToArray(),
            lossLedger = ledger, stocktakeGains = movements.Where(x => x.document_type == "stocktake" && x.m.direction == 1).Select(x => new { itemId = x.m.item_id, quantity = x.m.quantity, amount = x.m.amount, date = x.business_date }).ToArray() };
    }
    public sealed record ChainStep(string Operation, string Form, string Unit, decimal PerBase, string Storage, decimal StandardYield, decimal Hours);
    public sealed record ChainRow(int ProductId, string Dish, string Spec, int ItemId, string Ingredient, decimal RecipeQuantity, string BaseUnit,
        string PurchaseForm, string PurchaseUnit, decimal PurchaseQuantity, string PurchaseStorage, ChainStep[] Steps);
    public async Task<ChainRow[]> RecipeChain()
    {
        var specs = await db.fnbDishSpec.Where(x => x.shop_id == shopId && x.valid).ToArrayAsync(); var pids = specs.Select(x => x.product_id).ToArray();
        var products = await db.product.Where(x => pids.Contains(x.id) && x.shop_id == shopId && x.type == "餐饮" && x.valid == 1).ToArrayAsync();
        var recipes = await db.fnbRecipe.Where(x => x.shop_id == shopId && x.recipe_type == "dish" && x.status == "published").ToArrayAsync(); var rids = recipes.Select(x => x.id).ToArray();
        var lines = await db.fnbRecipeLine.Where(x => rids.Contains(x.recipe_id)).OrderBy(x => x.sort).ToArrayAsync();
        var items = await db.fnbItem.ToArrayAsync(); var forms = await db.fnbItemForm.Where(x => x.valid).OrderBy(x => x.seq).ToArrayAsync();
        var result = new List<ChainRow>();
        foreach (var spec in specs.Where(s => products.Any(p => p.id == s.product_id)))
        {
            var recipe = recipes.Where(x => x.dish_spec_id == spec.id).OrderByDescending(x => x.version_no).FirstOrDefault(); if (recipe == null) continue;
            foreach (var line in lines.Where(x => x.recipe_id == recipe.id))
            {
                var item = items.Single(x => x.id == line.item_id); var fs = forms.Where(x => x.item_id == item.id).ToArray(); if (fs.Length == 0) throw new ArgumentException("报表用料缺少形态链");
                decimal yield = fs.Skip(1).Aggregate(1m, (n, f) => n * f.in_op_yield!.Value);
                result.Add(new(spec.product_id, products.Single(x => x.id == spec.product_id).name, spec.name, item.id, item.name, line.quantity, item.base_unit_code,
                    fs[0].name, fs[0].unit_name, Round(line.quantity / fs[0].per_base / yield), fs[0].storage_type,
                    fs.Skip(1).Select(f => new ChainStep(f.in_op_name!, f.name, f.unit_name, f.per_base, f.storage_type, f.in_op_yield!.Value, f.in_op_hours!.Value)).ToArray()));
            }
        }
        return result.ToArray();
    }
    public static byte[] ExportRecipeChain(ChainRow[] rows)
    {
        using var book = new XSSFWorkbook(); var sheet = book.CreateSheet("出品与配方链路"); int steps = rows.Select(x => x.Steps.Length).DefaultIfEmpty(0).Max();
        var headers = new List<string> { "菜品", "规格", "用料", "每份基本量", "基本单位", "采购态", "采购单位", "每份采购量", "采购储存" };
        for (int s = 1; s <= steps; s++) headers.AddRange(new[] { "作业" + s, "形态" + s, "单位" + s, "每单位基本量" + s, "储存" + s, "标准出成率" + s, "耗时小时" + s });
        var head = sheet.CreateRow(0); for (int i = 0; i < headers.Count; i++) { head.CreateCell(i).SetCellValue(headers[i]); sheet.SetColumnWidth(i, 18 * 256); }
        for (int n = 0; n < rows.Length; n++)
        {
            var r = rows[n]; var cells = new List<object> { r.Dish, r.Spec, r.Ingredient, r.RecipeQuantity, r.BaseUnit, r.PurchaseForm, r.PurchaseUnit, r.PurchaseQuantity, r.PurchaseStorage };
            foreach (var s in r.Steps) cells.AddRange(new object[] { s.Operation, s.Form, s.Unit, s.PerBase, s.Storage, s.StandardYield, s.Hours });
            var row = sheet.CreateRow(n + 1); for (int c = 0; c < cells.Count; c++) { var cell = row.CreateCell(c); if (cells[c] is decimal value) cell.SetCellValue((double)value); else cell.SetCellValue(cells[c].ToString()); }
        }
        sheet.CreateFreezePane(3, 1); if (rows.Length > 0) sheet.SetAutoFilter(new NPOI.SS.Util.CellRangeAddress(0, rows.Length, 0, headers.Count - 1));
        using var stream = new MemoryStream(); book.Write(stream, true); return stream.ToArray();
    }
}
