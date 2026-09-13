using System;
using System.Collections.Generic;
using SnowmeetApi.Models.AdminAssistant;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 下发给小程序的条件与上下文的线上形状。
    ///
    /// 服务端内部用一个四域共用的超集 AdminAssistantQueryState 搬运条件，但**下发时必须按域裁剪**：
    /// 小程序和 reqai 都是按域严格校验、拒绝本域没有的字段，超集多带的那几个键会让整条响应
    /// 被判非法 —— 表现是「暂时无法获得回答」，而服务端日志里一切正常。
    /// </summary>
    public static class AdminAssistantWire
    {
        public static Dictionary<string, object?> State(AdminAssistantQueryState state, AdminAssistantDomain domain)
        {
            Dictionary<string, object?> shaped = new(StringComparer.Ordinal);
            foreach (string field in domain.fields)
            {
                object? value = state.Read(field);
                shaped[field] = value is DateTime date ? date.ToString("yyyy-MM-dd") : value;
            }
            return shaped;
        }

        public static Dictionary<string, object?> Context(AdminAssistantContext? context)
        {
            Dictionary<string, object?> wire = new(StringComparer.Ordinal)
            {
                ["active_query_type"] = context?.active_query_type
            };
            foreach (AdminAssistantDomain domain in AdminAssistantDomains.All.Values)
            {
                AdminAssistantQueryState? state = context?.Get(domain.contextKey);
                wire[domain.contextKey] = state == null ? null : State(state, domain);
            }
            return wire;
        }
    }
}
