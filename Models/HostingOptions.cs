namespace WebGallery.Models;

public sealed class HostingOptions
{
    public bool UseHttpsRedirection { get; set; } = true;
}

public sealed class ReverseProxyOptions
{
    public bool Enabled { get; set; }
    public int ForwardLimit { get; set; } = 1;
    public string[] KnownProxies { get; set; } = [];
    public string[] KnownNetworks { get; set; } = [];
    public string[] AllowedHosts { get; set; } = [];
}
