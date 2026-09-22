using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;

public sealed record ReceiptInput(
    int ShopId, Guid RequestId, int ItemId, string BatchNo, string StockForm, string StorageType,
    string? StorageLocation, decimal Quantity, string InputUnitCode, decimal UnitPrice,
    DateOnly? ProductionDate, int? ShelfLifeValue, string? ShelfLifeUnit, DateOnly ExpireDate,
    int WarnDays, IReadOnlyList<int> ImageIds, decimal? PackSize, string? PackUnitName,
    string? OpenStorageType, int? OpenShelfLifeDays, string ExpirySource, string? ExpiryNote,
    int? ShelfLifeRuleId = null);

public sealed record ReceiptPlan(decimal InputToBase, decimal BaseQuantity, decimal Amount, DateOnly? CalculatedExpiry);

public static class FnbReceiptRules
{
    public static ReceiptPlan Plan(ReceiptInput input, FnbMaterialItem item, FnbUnit inputUnit, FnbUnit baseUnit)
    {
        if (input.ShopId <= 0 || input.ItemId != item.id || input.RequestId == Guid.Empty)
            throw new ArgumentException("门店、食材或请求号无效");
        if (string.IsNullOrWhiteSpace(input.BatchNo) || !FnbText.FitsChineseVarchar(input.BatchNo.Trim(), 50) ||
            input.Quantity <= 0 || input.Quantity != decimal.Round(input.Quantity, 6) ||
            input.UnitPrice < 0 || input.UnitPrice != decimal.Round(input.UnitPrice, 6) ||
            input.WarnDays < 0 || input.WarnDays > 3650)
            throw new ArgumentException("批号、数量、单价或预警天数无效");
        if (!item.valid || !inputUnit.valid || !baseUnit.valid ||
            item.base_unit_code != baseUnit.code || inputUnit.dimension != baseUnit.dimension ||
            baseUnit.factor_to_base != 1 || inputUnit.factor_to_base <= 0)
            throw new ArgumentException("食材或计量单位不匹配");
        if (input.StockForm is not ("bulk" or "sealed") || input.StorageType is not ("ambient" or "chilled" or "frozen") ||
            input.ExpirySource is not ("manual" or "package" or "category" or "estimated"))
            throw new ArgumentException("形态、储存方式或效期来源无效");
        if (input.ExpirySource == "estimated" && string.IsNullOrWhiteSpace(input.ExpiryNote))
            throw new ArgumentException("估算效期须填写依据");
        if (!FnbText.FitsChineseVarchar(input.StorageLocation, 200) ||
            !FnbText.FitsChineseVarchar(input.PackUnitName, 40) ||
            !FnbText.FitsChineseVarchar(input.ExpiryNote, 1000))
            throw new ArgumentException("文本超长或包含数据库无法保存的字符");
        if (input.ProductionDate > input.ExpireDate || input.ExpireDate == default ||
            (input.ShelfLifeValue.HasValue != (input.ShelfLifeUnit != null)))
            throw new ArgumentException("生产日期或保质期无效");
        DateOnly? calculatedExpiry = null;
        if (input.ShelfLifeValue.HasValue)
        {
            if (input.ProductionDate == null) throw new ArgumentException("推算效期须有生产日期");
            calculatedExpiry = FnbInventoryRules.CalculateExpiry(input.ProductionDate.Value, input.ShelfLifeValue.Value, input.ShelfLifeUnit!);
        }
        if (input.ImageIds == null || input.ImageIds.Count == 0 || input.ImageIds.Any(id => id <= 0) ||
            input.ImageIds.Distinct().Count() != input.ImageIds.Count || string.Join(",", input.ImageIds).Length > 500)
            throw new ArgumentException("至少上传一张有效且不重复的批次照片");

        decimal factor;
        if (input.StockForm == "sealed")
        {
            if (input.Quantity != decimal.Truncate(input.Quantity) || input.PackSize is null or <= 0 ||
                string.IsNullOrWhiteSpace(input.PackUnitName) || input.PackUnitName.Trim().Length > 20 ||
                input.OpenStorageType is not ("ambient" or "chilled" or "frozen") || input.OpenShelfLifeDays is null or < 0)
                throw new ArgumentException("未开封批次须提供整数件数、净含量和开封后效期");
            factor = input.PackSize.Value;
        }
        else
        {
            if (input.PackSize != null || input.PackUnitName != null || input.OpenStorageType != null || input.OpenShelfLifeDays != null)
                throw new ArgumentException("散装批次不能设置包装或开封参数");
            factor = inputUnit.factor_to_base;
        }
        decimal baseQuantity;
        decimal amount;
        try
        {
            baseQuantity = decimal.Round(checked(input.Quantity * factor), 6);
            amount = decimal.Round(checked(input.Quantity * input.UnitPrice), 6, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException) { throw new ArgumentException("数量或金额超出数据库范围"); }
        if (baseQuantity <= 0 || baseQuantity >= 1_000_000_000_000m || amount >= 10_000_000_000_000m ||
            factor != decimal.Round(factor, 6))
            throw new ArgumentException("数量、换算比例或金额超出数据库精度");
        return new ReceiptPlan(factor, baseQuantity, amount, calculatedExpiry);
    }
}
