namespace Ponte.Management.Api.Application;

public sealed class ManagementOptions
{
    public const string SectionName = "Management";

    /// <summary>Somente desenvolvimento: aceita http:// e hosts locais (echo-receiver do compose).</summary>
    public bool AllowInsecureEndpointUrls { get; set; }

    public int MaxEndpointsPerTenant { get; set; } = 50;
}
