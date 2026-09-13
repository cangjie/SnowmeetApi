using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models.AdminAssistant;
using Xunit;

namespace SnowmeetApi.Tests
{
    /// <summary>
    /// 上下文的往返闭环：服务端下发什么，小程序原样存下来再回传，服务端必须还认。
    ///
    /// 这条回路撑着「追问」——店员说「改成五月」时，小程序带回来的正是上一轮下发的上下文。
    /// 下发按域裁剪、入站按域白名单校验，两边一旦对不上，追问会直接变成请求参数不合法。
    /// </summary>
    public class AdminAssistantContextRoundTripTests
    {
        private static AdminAssistantQueryState April() => new()
        {
            start_date = new System.DateTime(2026, 4, 1),
            end_date = new System.DateTime(2026, 4, 30),
            shop = "万龙服务中心",
            is_test = false,
            rent_status = "未支付",
            use_card = true,
            has_retail = true,
            is_summer_care = true,
            retail_type = "养护卡类",
            cell_suffix = "7788"
        };

        private static async Task<AdminAssistantRequest?> Bind(string json)
        {
            DefaultHttpContext http = new();
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
            DefaultModelBindingContext binding = new()
            {
                ActionContext = new ActionContext { HttpContext = http },
                ModelState = new ModelStateDictionary()
            };
            await new AdminAssistantRequestModelBinder().BindModelAsync(binding);
            return binding.Result.Model as AdminAssistantRequest;
        }

        private static string Envelope(Dictionary<string, object?> context) => JsonSerializer.Serialize(new
        {
            version = "1",
            page_key = "pages/admin/care/care_order_list",
            question = "改成五月",
            conversation = System.Array.Empty<object>(),
            context
        });

        [Theory]
        [InlineData("rental_order.query")]
        [InlineData("care_order.query")]
        [InlineData("retail_order.query")]
        [InlineData("ski_pass.query")]
        public async Task 下发的上下文原样回传时仍被接受(string actionType)
        {
            AdminAssistantDomain domain = AdminAssistantDomains.Require(actionType);
            AdminAssistantContext saved = new() { active_query_type = domain.actionType };
            saved.Set(domain.contextKey, April());

            AdminAssistantRequest? bound = await Bind(Envelope(AdminAssistantWire.Context(saved)));

            Assert.NotNull(bound);
            Assert.Equal(domain.actionType, bound!.context.active_query_type);
            AdminAssistantQueryState? state = bound.context.Get(domain.contextKey);
            Assert.NotNull(state);
            Assert.Equal(new System.DateTime(2026, 4, 1), state!.start_date);
        }

        [Fact]
        public async Task 回传里夹带本域没有的条件会被拒绝()
        {
            // 伪造的上下文不能借道回传把别域条件塞进来。
            AdminAssistantDomain care = AdminAssistantDomains.Require("care_order.query");
            Dictionary<string, object?> context = AdminAssistantWire.Context(
                new AdminAssistantContext { active_query_type = care.actionType, care_order_query = April() });
            ((Dictionary<string, object?>)context["care_order_query"]!)["rent_status"] = "未支付";

            Assert.Null(await Bind(Envelope(context)));
        }

        [Fact]
        public async Task 回传里的未知业务域指针会被拒绝()
        {
            Dictionary<string, object?> context = AdminAssistantWire.Context(null);
            context["active_query_type"] = "payroll.query";

            Assert.Null(await Bind(Envelope(context)));
        }
    }
}
