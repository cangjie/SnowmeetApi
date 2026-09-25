using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;

// 售价、分类都可不传（2026-09-25 起小程序只填名称和用料）：新建时售价 0、归入「未分类」；修改时不传则保持原值
public sealed record DishInput(int ShopId, int Id, string Name, decimal? SalePrice, int? CategoryId,
    string? CategoryName, bool Valid);
public sealed record DishRow(int ProductId, string Name, decimal SalePrice, int CategoryId, string CategoryName,
    int? SpecId, string? SpecName,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long? PublishedRecipeId, int? PublishedVersion,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long? DraftRecipeId);
public sealed record DishCategoryRow(int Id, string Name);
public sealed record DishList(IReadOnlyList<DishRow> Dishes, IReadOnlyList<DishCategoryRow> Categories);

/// <summary>Restaurant dishes are ordinary products under a valid 餐饮 category of the kitchen's shop.</summary>
public sealed class FnbDishService(ApplicationDBContext db)
{
    private const string BizType = "餐饮";
    // 菜品挂在 product 上，product 须属于一个餐饮分类；不选分类的菜品统一放这里
    public const string DefaultCategoryName = "未分类";

    public async Task<DishList> ListAsync(int shopId)
    {
        var categories = await db.category.AsNoTracking().Where(x => x.biz_type == BizType && x.valid == 1)
            .OrderBy(x => x.sort).ThenBy(x => x.id).Select(x => new DishCategoryRow(x.id, x.name)).ToListAsync();
        int[] categoryIds = categories.Select(x => x.Id).ToArray();
        var products = await db.product.AsNoTracking().Where(x => x.shop_id == shopId && x.valid == 1 && x.hidden == 0 &&
            x.category_id != null && categoryIds.Contains(x.category_id.Value)).OrderBy(x => x.sort).ThenBy(x => x.id).ToListAsync();
        int[] productIds = products.Select(x => x.id).ToArray();
        var specs = await db.fnbDishSpec.AsNoTracking().Where(x => x.shop_id == shopId && x.valid &&
            productIds.Contains(x.product_id)).ToListAsync();
        int[] specIds = specs.Select(x => x.id).ToArray();
        var recipes = await db.fnbRecipe.AsNoTracking().Where(x => x.shop_id == shopId && x.recipe_type == "dish" &&
            x.dish_spec_id != null && specIds.Contains(x.dish_spec_id.Value) && (x.status == "published" || x.status == "draft"))
            .ToListAsync();
        var names = categories.ToDictionary(x => x.Id, x => x.Name);
        var rows = products.Select(p =>
        {
            var spec = DefaultSpec(specs.Where(x => x.product_id == p.id).ToList());
            var published = spec == null ? null : recipes.Where(x => x.dish_spec_id == spec.id && x.status == "published")
                .OrderByDescending(x => x.version_no).FirstOrDefault();
            var draft = spec == null ? null : recipes.Where(x => x.dish_spec_id == spec.id && x.status == "draft")
                .OrderByDescending(x => x.version_no).FirstOrDefault();
            return new DishRow(p.id, p.name, (decimal)p.sale_price, p.category_id!.Value, names[p.category_id.Value],
                spec?.id, spec?.name, published?.id, published?.version_no, draft?.id);
        }).ToList();
        return new DishList(rows, categories);
    }

    public async Task<DishRow> SaveAsync(DishInput input)
    {
        string name = input.Name?.Trim() ?? "";
        if (input.ShopId <= 0 || name.Length == 0 || !FnbText.FitsChineseVarchar(name, 200) || input.SalePrice < 0 ||
            input.SalePrice != null && input.SalePrice != decimal.Round(input.SalePrice.Value, 2))
            throw new ArgumentException("菜品名称或售价无效");
        Product? product = null;
        if (input.Id != 0)
        {
            product = await db.product.AsTracking().FirstOrDefaultAsync(x => x.id == input.Id && x.shop_id == input.ShopId);
            if (product == null || !await db.category.AnyAsync(x => x.id == product.category_id && x.biz_type == BizType))
                throw new ArgumentException("菜品不存在或不属于本店");
        }
        // 先定分类（可能要新建分类并保存），再新建商品，避免把未填完的商品一并写库
        bool pickCategory = input.CategoryId != null || !string.IsNullOrWhiteSpace(input.CategoryName);
        var category = pickCategory ? await ResolveCategoryAsync(input.CategoryId, input.CategoryName)
            : product == null ? await ResolveCategoryAsync(null, DefaultCategoryName)
            : await db.category.AsNoTracking().FirstAsync(x => x.id == product.category_id);
        if (product == null)
        {
            product = new Product { shop_id = input.ShopId, type = BizType, valid = 1, hidden = 0, on_shelves = 1,
                create_date = DateTime.Now };
            db.product.Add(product);
        }
        else product.update_date = DateTime.Now;
        product.name = name;
        if (input.SalePrice != null || input.Id == 0) product.sale_price = (double)(input.SalePrice ?? 0m);
        product.category_id = category.id;
        product.valid = input.Valid ? 1 : 0;
        await db.SaveChangesAsync();

        var specs = await db.fnbDishSpec.AsTracking().Where(x => x.shop_id == input.ShopId && x.product_id == product.id && x.valid).ToListAsync();
        var spec = DefaultSpec(specs);
        if (spec == null && input.Valid)
        {
            spec = new FnbDishSpec { shop_id = input.ShopId, product_id = product.id, spec_code = "default", name = "标准份",
                is_default = true, valid = true, created_at = DateTime.UtcNow };
            db.fnbDishSpec.Add(spec);
            await db.SaveChangesAsync();
        }
        return new DishRow(product.id, product.name, (decimal)product.sale_price, category.id, category.name, spec?.id, spec?.name,
            null, null, null);
    }

    private async Task<Category> ResolveCategoryAsync(int? categoryId, string? categoryName)
    {
        if (categoryId != null)
            return await db.category.AsNoTracking().FirstOrDefaultAsync(x => x.id == categoryId && x.biz_type == BizType && x.valid == 1)
                ?? throw new ArgumentException("须选择有效的餐饮分类");
        string name = categoryName?.Trim() ?? "";
        if (name.Length == 0 || !FnbText.FitsChineseVarchar(name, 50)) throw new ArgumentException("须选择或填写餐饮分类");
        var existing = await db.category.AsNoTracking().FirstOrDefaultAsync(x => x.biz_type == BizType && x.valid == 1 && x.name == name);
        if (existing != null) return existing;
        var created = new Category { biz_type = BizType, name = name, valid = 1, hide = 0, create_date = DateTime.Now };
        db.category.Add(created);
        await db.SaveChangesAsync();
        return created;
    }

    private static FnbDishSpec? DefaultSpec(List<FnbDishSpec> specs) =>
        specs.FirstOrDefault(x => x.is_default) ?? (specs.Count == 1 ? specs[0] : null);
}
