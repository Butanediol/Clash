using System.Text.Json.Serialization;

namespace ClashXW.Models
{
    public record ClashConfig(
        [property: JsonPropertyName("port")] int? Port,
        [property: JsonPropertyName("socks-port")] int? SocksPort,
        [property: JsonPropertyName("mixed-port")] int? MixedPort,
        [property: JsonPropertyName("mode")] string Mode,
        [property: JsonPropertyName("log-level")] string? LogLevel,
        [property: JsonPropertyName("ipv6")] bool? IPv6,
        [property: JsonPropertyName("tun")] TunConfig? Tun
    );

    public record TunConfig(
        [property: JsonPropertyName("enable")] bool Enable,
        [property: JsonPropertyName("stack")] string? Stack,
        [property: JsonPropertyName("dns-hijack")] string[]? DnsHijack,
        [property: JsonPropertyName("auto-route")] bool? AutoRoute,
        [property: JsonPropertyName("auto-detect-interface")] bool? AutoDetectInterface
    );

    public record ModeUpdateRequest(
        [property: JsonPropertyName("mode")] string Mode
    );

    public record TunUpdateRequest(
        [property: JsonPropertyName("tun")] TunEnableRequest Tun
    );

    public record TunEnableRequest(
        [property: JsonPropertyName("enable")] bool Enable
    );

    public record ProxySelectionRequest(
        [property: JsonPropertyName("name")] string Name
    );

    public record ConfigReloadRequest(
        [property: JsonPropertyName("path")] string Path
    );
}
