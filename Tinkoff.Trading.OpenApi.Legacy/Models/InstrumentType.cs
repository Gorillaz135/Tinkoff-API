using System.Text.Json.Serialization;

namespace Tinkoff.Trading.OpenApi.Legacy.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum InstrumentType
    {
        Stock,
        Currency,
        Bond,
        Etf
    }
}