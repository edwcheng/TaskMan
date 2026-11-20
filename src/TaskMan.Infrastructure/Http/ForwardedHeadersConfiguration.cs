using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace TaskMan.Infrastructure.Http;

public static class ForwardedHeadersConfiguration
{
    public static void Apply(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        var proxies = Split(configuration["ForwardedHeaders:KnownProxies"]);
        var networks = Split(configuration["ForwardedHeaders:KnownNetworks"]);

        if (proxies.Count == 0 && networks.Count == 0)
        {
            return;
        }

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in proxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownProxies contains an invalid IP address: '{proxy}'.");
            }

            options.KnownProxies.Add(address);
        }

        foreach (var network in networks)
        {
            if (!System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownNetworks contains an invalid network: '{network}'.");
            }

            options.KnownIPNetworks.Add(parsed);
        }
    }

    private static List<string> Split(string? value) =>
        value is null
            ? []
            : value
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
}
