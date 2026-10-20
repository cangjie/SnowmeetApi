#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using SnowmeetApi.Services.Qisemi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SnowmeetApi.Controllers;

public sealed class QisemiSessionFilter(QisemiSessionContext session) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        session.SessionKey = context.HttpContext.Request.Headers["SessionKey"].ToString();
        session.SessionType = context.HttpContext.Request.Headers["SessionType"].ToString();
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
        await next();
    }
}

public sealed class QisemiSessionSwagger : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(QisemiController)) return;
        operation.Parameters ??= [];
        foreach (string name in new[] { "SessionKey", "SessionType" })
            operation.Parameters.Add(new OpenApiParameter { Name = name, In = ParameterLocation.Header, Required = false,
                Description = "预留会话请求头，本期可省略，暂不进行身份校验", Schema = new OpenApiSchema { Type = "string" } });
    }
}

[ApiController]
[Route("api/Qisemi")]
[ServiceFilter(typeof(QisemiSessionFilter))]
public sealed class QisemiController(IQisemiReadService service, ILogger<QisemiController> logger) : ControllerBase
{
    private async Task<IActionResult> Read(string operation, QisemiQuery q, long? id = null, string? key = null)
    {
        if (id <= 0) return BadRequest(new { code = 1, message = "id 必须为正整数" });
        try { return Ok(new { code = 0, message = "", data = await service.ReadAsync(operation, q, id, key, HttpContext.RequestAborted) }); }
        catch (QisemiReadException ex) { return StatusCode(ex.Status, new { code = ex.Status == 409 ? 4 : 1, message = ex.Message }); }
        catch (Exception ex) when (ex is ArgumentException or ValidationException) { return BadRequest(new { code = 1, message = ex.Message }); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogError(ex, "Qisemi read failed: {Operation}", operation); return StatusCode(503, new { code = 5, message = "七色米数据暂时无法读取，请稍后重试" }); }
    }
    [HttpGet("Sources")] public Task<IActionResult> Sources([FromQuery] QisemiQuery q) => Read("sources", q);
    [HttpGet("Snapshot")] public Task<IActionResult> Snapshot([FromQuery] QisemiQuery q) => Read("snapshot", q);
    [HttpGet("Modules")] public Task<IActionResult> Modules([FromQuery] QisemiQuery q) => Read("modules", q);
    [HttpGet("Warehouses")] public Task<IActionResult> Warehouses([FromQuery] QisemiQuery q) => Read("warehouses", q);
    [HttpGet("TopCategories")] public Task<IActionResult> TopCategories([FromQuery] QisemiQuery q) => Read("topCategories", q);
    [HttpGet("Inventory")] public Task<IActionResult> Inventory([FromQuery] QisemiQuery q) => Read("inventory", q);
    [HttpGet("Styles/{id:long}")] public Task<IActionResult> Style(long id, [FromQuery] QisemiQuery q) => Read("style", q, id);
    [HttpGet("Skus/{id:long}")] public Task<IActionResult> Sku(long id, [FromQuery] QisemiQuery q) => Read("sku", q, id);
    [HttpGet("Documents")] public Task<IActionResult> Documents([FromQuery] QisemiQuery q) => Read("documents", q);
    [HttpGet("Documents/{id:long}")] public Task<IActionResult> Document(long id, [FromQuery] QisemiQuery q) => Read("document", q, id);
    [HttpGet("Documents/{id:long}/Sections/{section}")] public Task<IActionResult> Section(long id, string section, [FromQuery] QisemiQuery q) => Read("documentSection", q, id, section);
    [HttpGet("Versions/{id:long}")] public Task<IActionResult> Version(long id, [FromQuery] QisemiQuery q) => Read("version", q, id);
    [HttpGet("Versions")] public Task<IActionResult> Versions([FromQuery] QisemiQuery q) => Read("versions", q);
    [HttpGet("Archives/{module}")] public Task<IActionResult> Archives(string module, [FromQuery] QisemiQuery q) => Read("archives", q, key: module);
    [HttpGet("Archives/{module}/{id:long}")] public Task<IActionResult> Archive(string module, long id, [FromQuery] QisemiQuery q) => Read("archives", q, id, module);
    [HttpGet("Records")] public Task<IActionResult> Records([FromQuery] QisemiQuery q) => Read("records", q);
    [HttpGet("Modules/{module}/Records")] public Task<IActionResult> ModuleRecords(string module, [FromQuery] QisemiQuery q) => Read("records", q, key: module);
    [HttpGet("Records/{id:long}")] public Task<IActionResult> Record(long id, [FromQuery] QisemiQuery q) => Read("record", q, id);
    [HttpGet("StockMovements")] public Task<IActionResult> Movements([FromQuery] QisemiQuery q) => Read("movements", q);
    [HttpGet("SalesLines")] public Task<IActionResult> SalesLines([FromQuery] QisemiQuery q) => Read("salesLines", q);
    [HttpGet("StockTotals")] public Task<IActionResult> StockTotals([FromQuery] QisemiQuery q) => Read("stockTotals", q);
    [HttpGet("WarehouseStocks")] public Task<IActionResult> WarehouseStocks([FromQuery] QisemiQuery q) => Read("warehouseStocks", q);
    [HttpGet("Files")] public Task<IActionResult> Files([FromQuery] QisemiQuery q) => Read("files", q);
    [HttpGet("ExportSheets")] public Task<IActionResult> Sheets([FromQuery] QisemiQuery q) => Read("sheets", q);
    [HttpGet("ExportSheets/{id:long}/Rows")] public Task<IActionResult> SheetRows(long id, [FromQuery] QisemiQuery q) => Read("sheetRows", q, id);
    [HttpGet("Batches")] public Task<IActionResult> Batches([FromQuery] QisemiQuery q) => Read("batches", q);
    [HttpGet("DataIssues")] public Task<IActionResult> Issues([FromQuery] QisemiQuery q) => Read("issues", q);
    [HttpGet("FileReferences/{id:long}/Content")]
    public async Task<IActionResult> Content(long id, [FromQuery] QisemiQuery q)
    {
        if (id <= 0) return BadRequest(new { code = 1, message = "id 必须为正整数" });
        try
        {
            var file = await service.FileAsync(id, q, HttpContext.RequestAborted);
            if (file.Owner != null) Response.RegisterForDispose(file.Owner);
            return new FileStreamResult(file.Content, file.ContentType) { FileDownloadName = file.Inline ? null : file.Name, EnableRangeProcessing = false };
        }
        catch (QisemiReadException ex) { return StatusCode(ex.Status, new { code = ex.Status == 409 ? 4 : 1, message = ex.Message }); }
        catch (Exception ex) when (ex is ArgumentException or ValidationException) { return BadRequest(new { code = 1, message = ex.Message }); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogError(ex, "Qisemi file read failed"); return StatusCode(503, new { code = 5, message = "文件暂时无法读取，请稍后重试" }); }
    }
}
