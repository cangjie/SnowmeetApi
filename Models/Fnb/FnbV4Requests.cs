#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
namespace SnowmeetApi.Models.Fnb;

public sealed record FnbReceiptLine(int ItemId, int? SpecId, decimal Quantity, decimal Amount, string StorageType,
    DateTime? ProductionDate, DateTime? ExpireDate, int? AreaId = null, bool Sealed = false,
    decimal? PackSize = null, string? PackLabel = null, string? OpenStorage = null, int? OpenDays = null, int[]? PhotoIds = null);
public sealed record FnbReceiptRequest(int ShopId, Guid RequestId, FnbReceiptLine[] Lines, string? Remark = null);
public sealed record FnbDocumentRequest(int ShopId, Guid RequestId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long DocumentId);
public sealed record FnbBatchWriteRequest(int ShopId, Guid RequestId, int BatchId, decimal Quantity, string? Reason = null, bool ConfirmExpired = false);
public sealed record FnbLowRuleRequest(int ShopId, int ItemId, decimal? Ratio, decimal? Quantity);
public sealed record FnbOperationRequest(int ShopId, Guid RequestId, int BatchId, decimal InputQuantity, decimal ActualQuantity, int? AreaId = null);
public sealed record FnbCompleteOperationRequest(int ShopId, Guid RequestId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long OperationId);
public sealed record FnbIngredientLine(int ItemId, decimal Quantity);
public sealed record FnbPrepCreateRequest(int ShopId, int CategoryId, string Name, string UnitName, string BaseUnitCode, decimal OutputQuantity, FnbIngredientLine[]? Lines = null);
public sealed record FnbRecipeSaveRequest(int ShopId, int OwnerId, decimal OutputQuantity, FnbIngredientLine[] Lines);
public sealed record FnbPreparationRequest(int ShopId, Guid RequestId, int ItemId, decimal Batches, int? AreaId = null, DateTime? ExpireDate = null);
public sealed record FnbDishCreateRequest(int ShopId, string Name, string SpecName = "标准", decimal? SalePrice = null);
public sealed record FnbDishSpecRequest(int ShopId, int ProductId, string Name, decimal? SalePrice = null);
public sealed record FnbSpecDisableRequest(int ShopId, int SpecId);
public sealed record FnbOrderCreateLine(int SpecId, decimal Quantity);
public sealed record FnbOrderCreateRequest(int ShopId, Guid RequestId, FnbOrderCreateLine[] Lines, string? TableNo = null, string? Remark = null);
public sealed record FnbServeRequest(int ShopId, Guid RequestId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long OrderId, FnbIngredientLine[]? Lines = null);
public sealed record FnbSnapshotRequest(int ShopId, Guid RequestId);
public sealed record FnbCountLine(int ItemId, decimal Quantity, string? Remark = null);
public sealed record FnbCountRequest(int ShopId, Guid RequestId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long DocumentId, FnbCountLine[] Lines);
public sealed record FnbAreaSaveRequest(int ShopId, int Id, int? ParentId, string Name, string AreaType = "other", bool Valid = true, int Sort = 0);
public sealed record FnbIdRequest(int ShopId, int Id);
public sealed record FnbAreaImageRequest(int ShopId, int AreaId, int UploadId);
public sealed record FnbSupplySaveRequest(int ShopId, int Id, string Name, string SupplyType, int PackSize, string PackLabel, int? AreaId = null, string? Spec = null, bool Valid = true);
public sealed record FnbSupplyPostRequest(int ShopId, Guid RequestId, int SupplyId, string Type, decimal Quantity, string? Reason = null);
public sealed record FnbSupplyUndoRequest(int ShopId, Guid RequestId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long MovementId);
public sealed record FnbSupplyLowRequest(int ShopId, int SupplyId, decimal? Ratio, decimal? Quantity);
public sealed record FnbToolSaveRequest(int ShopId, int Id, string Name, int Quantity, string? Spec = null, int? AreaId = null, int? OwnerStaffId = null, bool DailyCheck = false, string? AssetNo = null, bool Valid = true);
public sealed record FnbToolChangeRequest(int ShopId, Guid RequestId, int ToolId, string? Status = null, int? AreaId = null, string? Remark = null);
public sealed record FnbCheckItemRequest(int ShopId, int Id, int AreaId, string Name, string Method = "yes_no", string Kind = "environment", bool Required = true,
    bool PhotoSuggested = false, string? Unit = null, decimal? Minimum = null, decimal? Maximum = null, int? ToolId = null, int? SupplyId = null, bool Valid = true);
public sealed record FnbCheckWriteRequest(int ShopId, Guid RequestId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long SheetId);
public sealed record FnbCheckValue(int ItemId, string? Result, decimal? Value = null, string? Reason = null, int? UploadId = null);
public sealed record FnbCheckDraftRequest(int ShopId, Guid RequestId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long SheetId, FnbCheckValue[] Lines);
public sealed record FnbCheckHandleRequest(int ShopId, Guid RequestId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long LineId, string Remark);
