using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClashXW.Models
{
    public record ProxyNode(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("now")] string? Now,
        [property: JsonPropertyName("all")] IReadOnlyList<string>? All,
        [property: JsonPropertyName("udp")] bool? Udp,
        [property: JsonPropertyName("delay")] int? Delay
    );

    public record ProxiesResponse(
        [property: JsonPropertyName("proxies")] Dictionary<string, ProxyNode> Proxies
    );
}
