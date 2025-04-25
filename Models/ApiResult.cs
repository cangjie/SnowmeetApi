namespace SnowmeetApi.Models
{
    public class ApiResult<T>
    {
        public int code { get; set; } = 0;
        public string message { get; set; } = "";
        public T? data { get; set; }
    }
}