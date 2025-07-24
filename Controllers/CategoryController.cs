using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class CategoryController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;
        public CategoryController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
        }
        private async Task<ApiResult<object?>> CheckStaff(int level,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null || staff.title_level < level)
            {
                return new ApiResult<object?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                };
            }
            return new ApiResult<object?>()
            {
                code = 0,
                message = "",
                data = staff
            };
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<Category>>> AddNewCategory(string name, string bizType,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            ApiResult<object?> checkStaffResult = await CheckStaff(100, sessionKey, sessionType);
            if (checkStaffResult != null && checkStaffResult.code == 1)
            {
                return Ok(checkStaffResult);
            }
            Staff staff = (Staff)checkStaffResult.data;
            bool isDup = false;
            if (bizType.Trim().Equals("餐饮"))
            {
                List<Category> cl = await _db.category
                    .Where(c => c.biz_type.Equals(bizType.Trim()) && c.name.Trim().Equals(name.Trim()) && c.valid == 1)
                    .AsNoTracking().ToListAsync();
                if (cl != null && cl.Count > 0)
                {
                    isDup = true;
                }
            }
            if (isDup)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "名称重复",
                    data = null
                });
            }
            string? code = null;
            if (bizType.Equals("餐饮"))
            {
                code = null;
            }
            List<Category> sameLevelCategories = await _db.category.Where(c => c.biz_type.Trim().Equals(bizType)
                && (code == null || c.code.Length == code.Length)).ToListAsync();
            for (int i = 0; sameLevelCategories != null && i < sameLevelCategories.Count; i++)
            {
                Category c = sameLevelCategories[i];
                c.sort = c.sort + 100;
                _db.category.Entry(c).State = EntityState.Modified;
            }
            Category category = new Category()
            {
                id = 0,
                biz_type = bizType.Trim(),
                code = null,
                name = name.Trim(),
                valid = 1,
                hide = 0,
                sort = 0
            };
            CoreDataModLog log = new CoreDataModLog()
            {
                id = 0,
                table_name = "category",
                field_name = null,
                key_value = 0,
                scene = "添加新分类",
                member_id = null,
                staff_id = staff.id,
                prev_value = null,
                current_value = name,
                trace_id = 0,
                is_manual = 1,
                manual_memo = bizType.Trim()
            };
            await _db.category.AddAsync(category);
            await _db.coreDataModLog.AddAsync(log);
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<Category>()
            {
                code = 0,
                message = "",
                data = category
            });
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<Category>>> UpdateCategory([FromBody] Category category, [FromQuery] string scene,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            scene = Util.UrlDecode(scene);
            ApiResult<object?> checkStaffResult = await CheckStaff(100, sessionKey, sessionType);
            if (checkStaffResult != null && checkStaffResult.code == 1)
            {
                return Ok(checkStaffResult);
            }
            Staff staff = (Staff)checkStaffResult.data;
            Category? ori = await _db.category.FindAsync(category.id);
            _db.category.Entry(ori).State = EntityState.Detached;
            await _db.SaveChangesAsync();
            List<CoreDataModLog> logList = Util.GetUpdateDifferenceLog<Category>(ori, category, null, staff.id, scene);
            category.update_date = DateTime.Now;
            _db.category.Entry(category).State = EntityState.Modified;
            for (int i = 0; i < logList.Count; i++)
            {
                await _db.coreDataModLog.AddAsync(logList[i]);
            }
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<Category>()
            {
                code = 0,
                message = "",
                data = category
            });
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<Category>>>> GetSingleLevelCategory(string bizType)
        {
            List<Category> cateList = await _db.category.Where(c => c.biz_type.Trim().Equals(bizType) && c.valid == 1)
                .Include(c => c.properties.OrderBy(c => c.sort)).ThenInclude(p => p.options.OrderBy(p => p.sort))
                .AsNoTracking().OrderByDescending(c => c.sort).ThenBy(c => c.id).ToListAsync();
            return Ok(new ApiResult<List<Category>>()
            {
                code = 0,
                message = "",
                data = cateList
            });
        }
        [NonAction]
        public async Task<List<ProductImage>> UpdateProductImages(int productId, List<ProductImage> productImages, int? staffId)
        {
            List<ProductImage> oriProductImages = await _db.productImage
                .Where(i => i.product_id == productId && i.valid == 1).ToListAsync();
            for (int i = 0; i < productImages.Count; i++)
            {
                ProductImage pi = productImages[i];
                if (pi.id == 0)
                {
                    pi.product_id = productId;
                    pi.create_date = DateTime.Now;
                    await _db.productImage.AddAsync(pi);
                }
                else
                {
                    ProductImage oriImage = oriProductImages.Where(pi => pi.id == productImages[i].id).FirstOrDefault();
                    if (oriImage != null)
                    {
                        List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<ProductImage>(oriImage, productImages[i], null, staffId, "修改商品图片");
                        for (int j = 0; j < logs.Count; j++)
                        {
                            await _db.coreDataModLog.AddAsync(logs[j]);
                        }
                        if (logs.Count == 0)
                        {
                            continue; // No changes, skip update
                        }
                        oriImage.title = productImages[i].title;
                        oriImage.content = productImages[i].content;
                        oriImage.sort = productImages[i].sort;
                        oriImage.is_head = productImages[i].is_head;
                        oriImage.valid = productImages[i].valid;
                        oriImage.update_date = DateTime.Now;
                        oriImage.upload_id = productImages[i].upload_id;
                        oriImage.image_url = productImages[i].image_url;
                        _db.productImage.Entry(oriImage).State = EntityState.Modified;
                    }
                    else
                    {
                        productImages[i].id = 0;
                        await _db.productImage.AddAsync(productImages[i]);
                    }
                }
            }
            for (int i = 0; i < oriProductImages.Count; i++)
            {
                ProductImage newImage = productImages.Where(pi => pi.id == oriProductImages[i].id).FirstOrDefault();
                if (newImage == null)
                {
                    oriProductImages[i].valid = 0; // Mark as invalid
                    oriProductImages[i].update_date = DateTime.Now;
                    CoreDataModLog log = new CoreDataModLog()
                    {
                        id = 0,
                        table_name = "product_image",
                        field_name = "valid",
                        key_value = oriProductImages[i].id,
                        scene = "删除商品图片",
                        member_id = null,
                        staff_id = staffId,
                        prev_value = "1",
                        current_value = "0",
                        trace_id = 0,
                        is_manual = 1,
                        manual_memo = "更新商品图片"
                    };
                    await _db.coreDataModLog.AddAsync(log);
                    _db.productImage.Entry(oriProductImages[i]).State = EntityState.Modified;
                }
            }
            await _db.SaveChangesAsync();
            return await _db.productImage.Where(i => i.product_id == productId && i.valid == 1)
                .OrderBy(i => i.sort).ThenByDescending(i => i.id).ToListAsync();
        }
        [NonAction]
        public async Task<List<ProductProperty>> UpdateProductProperties(int productId, List<ProductProperty> productProperties, int? staffId)
        {
            List<ProductProperty> oriProductProperties = await _db.productProperty
                .Where(p => p.product_id == productId && p.valid == 1).ToListAsync();
            for (int i = 0; i < productProperties.Count; i++)
            {
                ProductProperty pp = productProperties[i];
                if (pp.id == 0)
                {
                    await _db.productProperty.AddAsync(pp);
                }
                else
                {
                    ProductProperty? oriProperty = oriProductProperties.Where(p => p.id == pp.id).FirstOrDefault();
                    if (oriProperty != null)
                    {
                        List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<ProductProperty>(oriProperty, pp, null, staffId, "修改商品属性");
                        for (int j = 0; j < logs.Count; j++)
                        {
                            await _db.coreDataModLog.AddAsync(logs[j]);
                        }
                        if (logs.Count == 0)
                        {
                            continue; // No changes, skip update
                        }
                        oriProperty.text_value = pp.text_value;
                        oriProperty.option_id = pp.option_id;
                        oriProperty.update_date = DateTime.Now;
                        oriProperty.valid = pp.valid;
                        oriProperty.category_property_id = pp.category_property_id;
                        oriProperty.product_id = pp.product_id;
                        _db.productProperty.Entry(oriProperty).State = EntityState.Modified;
                    }
                }
            }
            for (int i = 0; i < oriProductProperties.Count; i++)
            {
                ProductProperty? pp = productProperties.Where(p => p.id == oriProductProperties[i].id).FirstOrDefault();
                if (pp == null)
                {
                    oriProductProperties[i].valid = 0; // Mark as invalid
                    oriProductProperties[i].update_date = DateTime.Now;
                    CoreDataModLog log = new CoreDataModLog()
                    {
                        id = 0,
                        table_name = "product_property",
                        field_name = "valid",
                        key_value = oriProductProperties[i].id,
                        scene = "删除商品属性",
                        member_id = null,
                        staff_id = staffId,
                        prev_value = "1",
                        current_value = "0",
                        trace_id = 0,
                        is_manual = 1,
                        manual_memo = "更新商品属性"
                    };
                    await _db.coreDataModLog.AddAsync(log);
                    _db.productProperty.Entry(oriProductProperties[i]).State = EntityState.Modified;
                }
            }
            await _db.SaveChangesAsync();
            return await _db.productProperty.Where(p => p.product_id == productId && p.valid == 1)
                .Include(p => p.categoryProperty).ThenInclude(cp => cp.options)
                .OrderBy(p => p.categoryProperty.sort).ThenByDescending(p => p.id).ToListAsync();
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<Product>>> ModProduct([FromBody] Product product,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            ApiResult<object?> checkStaffResult = await CheckStaff(100, sessionKey, sessionType);
            if (checkStaffResult != null && checkStaffResult.code == 1)
            {
                return Ok(checkStaffResult);
            }
            Staff staff = (Staff)checkStaffResult.data;
            Product oriProduct = await GetProduct(product.id);
            product.images = await UpdateProductImages(product.id, product.images, staff.id);
            product.properties = await UpdateProductProperties(product.id, product.properties, staff.id);
            if (oriProduct == null)
            {
                return Ok(new ApiResult<Category>()
                {
                    code = 1,
                    message = "商品不存在",
                    data = null
                });
            }
            List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<Product>(oriProduct, product, null, ((Staff)checkStaffResult.data).id, "修改商品");

            _db.product.Entry(oriProduct).State = EntityState.Detached;
            _db.product.Entry(product).State = EntityState.Modified;
            for (int i = 0; i < logs.Count; i++)
            {
                await _db.coreDataModLog.AddAsync(logs[i]);
            }
            product.update_date = DateTime.Now;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<Product>()
            {
                code = 0,
                message = "",
                data = product
            });
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<Product>>> AddProduct([FromBody] Product product,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            ApiResult<object?> checkStaffResult = await CheckStaff(100, sessionKey, sessionType);
            if (checkStaffResult != null && checkStaffResult.code == 1)
            {
                return Ok(checkStaffResult);
            }
            Staff staff = (Staff)checkStaffResult.data;
            List<Product> curPList = await _db.product.Where(p => p.category_id == product.category_id)
                .OrderByDescending(p => p.sort).ToListAsync();
            int? maxSort = 100;
            if (curPList != null && curPList.Count > 0)
            {
                maxSort = curPList[0].sort;
            }
            if (maxSort == null || maxSort == 0)
            {
                maxSort = 100;
            }
            else
            {
                maxSort = maxSort + 100;
            }
            product.sort = (int)maxSort;
            for (int i = 0; i < product.images.Count; i++)
            {
                product.images[i].create_date = DateTime.Now;
            }
            await _db.product.AddAsync(product);

            await _db.SaveChangesAsync();
            CoreDataModLog log = new CoreDataModLog()
            {
                id = 0,
                table_name = "product",
                field_name = null,
                key_value = product.id,
                scene = "添加新商品",
                member_id = null,
                staff_id = staff.id,
                prev_value = null,
                current_value = null,
                trace_id = 0,
                is_manual = 1,
                manual_memo = "新增饮品菜品"
            };
            await _db.coreDataModLog.AddAsync(log);
            await _db.SaveChangesAsync();
            if (product.stock_num != null)
            { 
                await UpdateProductStock(product.id, (int)product.stock_num, "新增商品", staff.id, null);
            }
            return Ok(new ApiResult<Product>()
                {
                    code = 0,
                    message = "",
                    data = product
                });
        }
        [NonAction]
        public async Task<List<Product>> GetCategoryProducts(int categoryId)
        {
            return await _db.product.Where(p => p.category_id == categoryId && p.valid == 1)
                .Include(p => p.images.OrderBy(i => i.sort))
                .Include(t => t.properties.Where(p => p.valid == 1))
                    .ThenInclude(p => p.categoryProperty).ThenInclude(c => c.options).OrderBy(t => t.sort)
                .AsNoTracking().ToListAsync();

        }
        [NonAction]
        public async Task<Product> GetProduct(int productId)
        {
            Product product = await _db.product.FindAsync(productId);
            await _db.product.Entry(product).Collection(p => p.images).LoadAsync();
            await _db.product.Entry(product).Collection(p => p.properties).LoadAsync();
            await _db.product.Entry(product).Collection(p => p.stocks).LoadAsync();
            await _db.product.Entry(product).Reference(p => p.category).LoadAsync();
            await _db.category.Entry(product.category).Collection(c => c.properties).LoadAsync();
            for (int i = 0; i < product.properties.Count; i++)
            {
                ProductProperty pp = product.properties[i];
                await _db.Entry(pp).Reference(p => p.categoryProperty).LoadAsync();
                if (pp.categoryProperty != null)
                {
                    await _db.Entry(pp.categoryProperty).Collection(c => c.options).LoadAsync();
                }
            }
            for (int i = 0; i < product.images.Count; i++)
            {
                ProductImage pi = product.images[i];
                await _db.Entry(pi).Reference(p => p.uploadFile).LoadAsync();
            }
            for (int i = 0; i < product.category.properties.Count; i++)
            {
                CategoryProperty cp = product.category.properties[i];
                await _db.Entry(cp).Collection(c => c.options).LoadAsync();
                for (int j = 0; j < cp.options.Count; j++)
                {
                    CategoryPropertyOption cpo = cp.options[j];
                    ProductProperty? pp = product.availableProperties
                        .Where(p => p.category_property_id == cp.id && p.option_id == cpo.id).FirstOrDefault();
                    if (pp != null)
                    {
                        cpo.is_checked = true;
                    }
                    else
                    {
                        cpo.is_checked = false;
                    }
                }
            }
            if (product.stocks.Count > 0)
            {
                product.stocks = product.stocks.OrderByDescending(s => s.id).ToList();
            }
            return product;
        }
        [HttpGet("{productId}")]
        public async Task<ActionResult<ApiResult<Product>>> GetProduct(int productId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Product product = await GetProduct(productId);
            if (product == null)
            {
                return Ok(new ApiResult<Product>()
                {
                    code = 1,
                    message = "商品不存在",
                    data = null
                });
            }
            return Ok(new ApiResult<Product>()
            {
                code = 0,
                message = "",
                data = product
            });
        }
        [HttpGet("{categoryId}")]
        public async Task<ActionResult<ApiResult<List<Product>>>> GetCategoryProducts(int categoryId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            List<Product> productList = await GetCategoryProducts(categoryId);
            return Ok(new ApiResult<List<Product>>()
            {
                code = 0,
                message = "",
                data = productList
            });
        }
        [NonAction]
        public async Task<List<ProductStock>> UpdateProductStock(int productId, int delta, string scene, int? staffId, int? orderId)
        {
            Product product = await _db.product.FindAsync(productId);
            if (product == null)
            {
                return new List<ProductStock>();
            }
            List<ProductStock> stockList = await _db.productStock
                .Where(s => s.product_id == productId).OrderByDescending(s => s.id)
                .AsNoTracking().ToListAsync();
            int prevStock = 0;
            if (stockList != null && stockList.Count > 0)
            {
                prevStock = stockList[0].sum;
            }
            product.stock_num = prevStock + delta;
            ProductStock stock = new ProductStock()
            {
                id = 0,
                product_id = productId,
                delta = delta,
                sum = prevStock + delta,
                memo = scene,
                staff_id = staffId,
                order_id = orderId,
                create_date = DateTime.Now
            };
            _db.product.Entry(product).State = EntityState.Modified;
            await _db.productStock.AddAsync(stock);
            await _db.SaveChangesAsync();
            return await _db.productStock
                .Where(s => s.product_id == productId).OrderByDescending(s => s.id)
                .AsNoTracking().ToListAsync();
        }
    }
}