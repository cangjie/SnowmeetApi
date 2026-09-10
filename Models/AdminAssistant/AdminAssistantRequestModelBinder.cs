using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SnowmeetApi.Models.AdminAssistant
{
    public sealed class AdminAssistantRequestModelBinder : IModelBinder
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            try
            {
                AdminAssistantRequest? request = await JsonSerializer.DeserializeAsync<AdminAssistantRequest>(
                    bindingContext.HttpContext.Request.Body, JsonOptions, bindingContext.HttpContext.RequestAborted);
                bindingContext.Result = ModelBindingResult.Success(request);
            }
            catch (JsonException)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
        }
    }
}
