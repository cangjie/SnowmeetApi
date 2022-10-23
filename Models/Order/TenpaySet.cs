using System;
namespace SnowmeetApi.Models.Order
{
    public class TenpaySet
    {
        public string nonce { get; set; }
        public string prepay_id { get; set; }
        public string sign { get; set; }
        public string timeStamp { get; set; }
    }
}

