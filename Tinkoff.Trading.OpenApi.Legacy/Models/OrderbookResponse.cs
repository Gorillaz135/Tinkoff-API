using System;
using System.Text.Json.Serialization;

namespace Tinkoff.Trading.OpenApi.Legacy.Models
{
    public class OrderbookResponse : StreamingResponse<OrderbookPayload>
    {
        public override string Event => "orderbook";

        [JsonConstructor]
        public OrderbookResponse(OrderbookPayload payload, DateTime time)
            : base(payload, time)
        {
        }
    }
}