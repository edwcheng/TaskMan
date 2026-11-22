using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using TaskMan.Infrastructure.Http;

namespace TaskMan.Tests.Http;

public sealed class ForwardedHeadersConfigurationTests
{
    [Fact]
    public void Defaults_forward_for_and_proto_and_compose_network()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersConfiguration.Apply(options, BuildConfiguration());

        Assert.Equal(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);

        // With no explicit config the Compose bridge network is trusted by default,
        // so X-Forwarded-From the reverse proxy is honoured (rate limiting sees the
        // real client IP).
        Assert.Single(options.KnownIPNetworks);
        Assert.Equal(
            IPAddress.Parse("172.16.0.0"),
            options.KnownIPNetworks.Single().BaseAddress);
        Assert.Equal(12, options.KnownIPNetworks.Single().PrefixLength);
        Assert.Empty(options.KnownProxies);
    }

    [Fact]
    public void Empty_string_values_are_treated_as_not_configured()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersConfiguration.Apply(
            options,
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownProxies"] = string.Empty,
                ["ForwardedHeaders:KnownNetworks"] = string.Empty,
            }));

        // Empty values split to nothing, so the default Compose network applies.
        Assert.Single(options.KnownIPNetworks);
        Assert.Equal(
            IPAddress.Parse("172.16.0.0"),
            options.KnownIPNetworks.Single().BaseAddress);
    }

    [Fact]
    public void Configured_proxies_and_networks_are_applied()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersConfiguration.Apply(
            options,
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownProxies"] = "10.0.0.5, 10.0.0.6",
                ["ForwardedHeaders:KnownNetworks"] = "10.0.0.0/8;192.168.0.0/16",
            }));

        Assert.Equal(
            new[]
            {
                IPAddress.Parse("10.0.0.5"),
                IPAddress.Parse("10.0.0.6"),
            },
            options.KnownProxies.OrderBy(x => x.ToString()));

        Assert.Equal(2, options.KnownIPNetworks.Count);
        Assert.Contains(
            options.KnownIPNetworks,
            x => x.BaseAddress.Equals(IPAddress.Parse("10.0.0.0"))
                && x.PrefixLength == 8);
        Assert.Contains(
            options.KnownIPNetworks,
            x => x.BaseAddress.Equals(IPAddress.Parse("192.168.0.0"))
                && x.PrefixLength == 16);
    }

    [Fact]
    public void Explicit_configuration_replaces_default_known_networks()
    {
        var options = new ForwardedHeadersOptions
        {
            KnownProxies = { IPAddress.Loopback },
        };

        options.KnownIPNetworks.Add(
            new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));

        ForwardedHeadersConfiguration.Apply(
            options,
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownProxies"] = "10.1.2.3",
                ["ForwardedHeaders:KnownNetworks"] = "10.0.0.0/8",
            }));

        Assert.Equal(
            new[] { IPAddress.Parse("10.1.2.3") },
            options.KnownProxies);
        Assert.Single(options.KnownIPNetworks);
        Assert.Equal(
            IPAddress.Parse("10.0.0.0"),
            options.KnownIPNetworks.Single().BaseAddress);
    }

    [Fact]
    public void Invalid_proxy_throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ForwardedHeadersConfiguration.Apply(
                new ForwardedHeadersOptions(),
                BuildConfiguration(new Dictionary<string, string?>
                {
                    ["ForwardedHeaders:KnownProxies"] = "not-an-ip",
                })));

        Assert.Contains("KnownProxies", exception.Message);
    }

    [Fact]
    public void Invalid_network_throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ForwardedHeadersConfiguration.Apply(
                new ForwardedHeadersOptions(),
                BuildConfiguration(new Dictionary<string, string?>
                {
                    ["ForwardedHeaders:KnownNetworks"] = "10.0.0.0/99",
                })));

        Assert.Contains("KnownNetworks", exception.Message);
    }

    private static IConfiguration BuildConfiguration(
        Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? [])
            .Build();
}
