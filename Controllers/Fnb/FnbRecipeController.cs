using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Controllers.Fnb;

[ApiController]
[Route("api/[controller]/[action]")]
public sealed class FnbRecipeController(ApplicationDBContext db) : ControllerBase
{
    private readonly FnbAccess _access = new(db);
    private static ApiResult<object> Result(int code, string message, object? data = null) => new() { code = code, message = message, data = data };
    private async Task<int> Permission(string? key, int shopId, bool manager = false)
    {
        var staff = await _access.ResolveAsync(key);
        return staff == null ? 2 : FnbAccess.CanAccess(staff, shopId, manager) ? 0 : 3;
    }

    [HttpGet]
    public async Task<ApiResult<object>> ListDishSpecs(string sessionKey, int shopId, int? productId = null)
    {
        int p = await Permission(sessionKey, shopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        var q = db.fnbDishSpec.AsNoTracking().Where(x => x.shop_id == shopId);
        if (productId.HasValue) q = q.Where(x => x.product_id == productId.Value);
        return Result(0, "", await q.OrderBy(x => x.product_id).ThenBy(x => x.id).ToListAsync());
    }

    public sealed record DishSpecInput(int ShopId, int Id, int ProductId, string SpecCode, string Name,
        decimal? SalePrice, int? LegacyProductId, bool IsDefault, bool Valid);

    [HttpPost]
    public async Task<ApiResult<object>> SaveDishSpec([FromQuery] string sessionKey, [FromBody] DishSpecInput input)
    {
        int p = await Permission(sessionKey, input.ShopId, true);
        if (p != 0) return Result(p, "会话失效或需要门店管理权限");
        if (string.IsNullOrWhiteSpace(input.SpecCode) || !FnbText.FitsChineseVarchar(input.SpecCode, 64) ||
            string.IsNullOrWhiteSpace(input.Name) || !FnbText.FitsChineseVarchar(input.Name, 100) || input.SalePrice < 0)
            return Result(1, "规格资料无效");
        var product = await db.product.AsNoTracking().FirstOrDefaultAsync(x => x.id == input.ProductId && x.shop_id == input.ShopId && x.valid == 1);
        if (product == null || product.category_id == null || !await db.category.AnyAsync(x => x.id == product.category_id && x.biz_type == "餐饮" && x.valid == 1))
            return Result(1, "商品必须是本店有效餐饮菜品");
        if (input.LegacyProductId != null && !await db.product.AnyAsync(x => x.id == input.LegacyProductId && x.shop_id == input.ShopId))
            return Result(1, "旧规格商品不属于本店");
        var row = input.Id == 0 ? new FnbDishSpec { created_at = DateTime.UtcNow } : await db.fnbDishSpec.AsTracking().FirstOrDefaultAsync(x => x.id == input.Id && x.shop_id == input.ShopId);
        if (row == null) return Result(1, "规格不存在");
        if (input.Id != 0 && row.product_id != input.ProductId) return Result(1, "规格所属菜品不可修改");
        if (await db.fnbDishSpec.AnyAsync(x => x.id != input.Id && x.product_id == input.ProductId && x.spec_code == input.SpecCode.Trim()))
            return Result(1, "规格编码已存在");
        row.shop_id = input.ShopId; row.product_id = input.ProductId; row.spec_code = input.SpecCode.Trim();
        row.name = input.Name.Trim(); row.sale_price = input.SalePrice; row.legacy_product_id = input.LegacyProductId;
        row.is_default = input.IsDefault; row.valid = input.Valid; row.updated_at = DateTime.UtcNow;
        if (input.Id == 0) db.fnbDishSpec.Add(row);
        await db.SaveChangesAsync();
        return Result(0, "", row);
    }

    [HttpGet]
    public async Task<ApiResult<object>> ListRecipes(string sessionKey, int shopId, int? dishSpecId = null, int? outputItemId = null)
    {
        int p = await Permission(sessionKey, shopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        var q = db.fnbRecipe.AsNoTracking().Where(x => x.shop_id == shopId);
        if (dishSpecId.HasValue) q = q.Where(x => x.dish_spec_id == dishSpecId);
        if (outputItemId.HasValue) q = q.Where(x => x.output_item_id == outputItemId);
        return Result(0, "", await q.OrderByDescending(x => x.id).Take(100).ToListAsync());
    }

    [HttpGet]
    public async Task<ApiResult<object>> GetRecipe(string sessionKey, int shopId, long recipeId)
    {
        int p = await Permission(sessionKey, shopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        var recipe = await db.fnbRecipe.AsNoTracking().FirstOrDefaultAsync(x => x.id == recipeId && x.shop_id == shopId);
        if (recipe == null) return Result(1, "配方不存在");
        var lines = await db.fnbRecipeLine.AsNoTracking().Where(x => x.recipe_id == recipeId).OrderBy(x => x.sort).ToListAsync();
        return Result(0, "", new { recipe, lines });
    }

    public sealed record RecipeLineInput(int ItemId, decimal Quantity, int Sort, string? Remark);
    public sealed record RecipeDraftInput(int ShopId, long Id, string RecipeType, int? DishSpecId, int? OutputItemId,
        decimal OutputQty, string? Remark, string? RowVersion, IReadOnlyList<RecipeLineInput> Lines);

    [HttpPost]
    public async Task<ApiResult<object>> SaveRecipeDraft([FromQuery] string sessionKey, [FromBody] RecipeDraftInput input)
    {
        var actor = await _access.ResolveActorAsync(sessionKey);
        if (actor == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, true)) return Result(3, "需要门店管理权限");
        if (input.RecipeType == "dish")
        {
            if (input.DishSpecId == null || input.OutputItemId != null || input.OutputQty != 1 ||
                !await db.fnbDishSpec.AnyAsync(x => x.id == input.DishSpecId && x.shop_id == input.ShopId && x.valid))
                return Result(1, "出餐配方目标无效");
        }
        else if (input.RecipeType == "prep")
        {
            if (input.DishSpecId != null || input.OutputItemId == null || input.OutputQty <= 0 ||
                !await db.fnbMaterialItem.AnyAsync(x => x.id == input.OutputItemId && x.item_type == "prepared" && x.valid))
                return Result(1, "半成品配方目标无效");
        }
        else return Result(1, "配方类型无效");
        if (input.Lines == null || input.Lines.Count == 0 || input.Lines.Any(x => x.Quantity <= 0 || x.Quantity != decimal.Round(x.Quantity, 6)
            || x.ItemId == input.OutputItemId || !FnbText.FitsChineseVarchar(x.Remark, 600)) ||
            input.Lines.Select(x => x.ItemId).Distinct().Count() != input.Lines.Count || !FnbText.FitsChineseVarchar(input.Remark, 1000))
            return Result(1, "配方用料无效");
        var itemIds = input.Lines.Select(x => x.ItemId).ToArray();
        if (await db.fnbMaterialItem.CountAsync(x => itemIds.Contains(x.id) && x.valid) != itemIds.Length)
            return Result(1, "存在无效食材");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        FnbRecipe recipe;
        if (input.Id == 0)
        {
            var q = db.fnbRecipe.Where(x => x.shop_id == input.ShopId && x.recipe_type == input.RecipeType);
            q = input.RecipeType == "dish" ? q.Where(x => x.dish_spec_id == input.DishSpecId) : q.Where(x => x.output_item_id == input.OutputItemId);
            int maxVersion = await q.MaxAsync(x => (int?)x.version_no) ?? 0;
            recipe = new FnbRecipe { shop_id = input.ShopId, recipe_type = input.RecipeType,
                dish_spec_id = input.DishSpecId, output_item_id = input.OutputItemId,
                version_no = maxVersion + 1, status = "draft", created_by_staff_id = actor.Staff.id,
                created_at = DateTime.UtcNow };
            db.fnbRecipe.Add(recipe);
        }
        else
        {
            recipe = await db.fnbRecipe.AsTracking().FirstOrDefaultAsync(x => x.id == input.Id && x.shop_id == input.ShopId);
            if (recipe == null || recipe.status != "draft" || recipe.recipe_type != input.RecipeType ||
                recipe.dish_spec_id != input.DishSpecId || recipe.output_item_id != input.OutputItemId) return Result(1, "只可修改同目标草稿配方");
            if (input.RowVersion == null || Convert.ToBase64String(recipe.row_version) != input.RowVersion) return Result(4, "配方已被修改，请刷新");
            var oldLines = await db.fnbRecipeLine.AsTracking().Where(x => x.recipe_id == recipe.id).ToListAsync();
            db.fnbRecipeLine.RemoveRange(oldLines);
        }
        recipe.output_qty = input.OutputQty; recipe.remark = input.Remark;
        await db.SaveChangesAsync();
        db.fnbRecipeLine.AddRange(input.Lines.Select(x => new FnbRecipeLine
        {
            recipe_id = recipe.id, item_id = x.ItemId, quantity = x.Quantity, sort = x.Sort, remark = x.Remark
        }));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result(0, "", new { id = recipe.id.ToString(), recipe.version_no, rowVersion = Convert.ToBase64String(recipe.row_version) });
    }

    public sealed record PublishInput(int ShopId, long RecipeId, string RowVersion);

    [HttpPost]
    public async Task<ApiResult<object>> PublishRecipe([FromQuery] string sessionKey, [FromBody] PublishInput input)
    {
        int p = await Permission(sessionKey, input.ShopId, true);
        if (p != 0) return Result(p, "会话失效或需要门店管理权限");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var recipe = await db.fnbRecipe.AsTracking().FirstOrDefaultAsync(x => x.id == input.RecipeId && x.shop_id == input.ShopId);
        if (recipe == null || recipe.status != "draft") return Result(1, "配方草稿不存在");
        if (Convert.ToBase64String(recipe.row_version) != input.RowVersion) return Result(4, "配方已被修改，请刷新");
        if (!await db.fnbRecipeLine.AnyAsync(x => x.recipe_id == recipe.id)) return Result(1, "空配方不能发布");
        var old = await db.fnbRecipe.AsTracking().Where(x => x.shop_id == input.ShopId && x.status == "published" && x.recipe_type == recipe.recipe_type &&
            (recipe.recipe_type == "dish" ? x.dish_spec_id == recipe.dish_spec_id : x.output_item_id == recipe.output_item_id)).ToListAsync();
        foreach (var row in old) row.status = "retired";
        await db.SaveChangesAsync();
        recipe.status = "published"; recipe.published_at = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result(0, "", new { id = recipe.id.ToString(), recipe.version_no });
    }
}
