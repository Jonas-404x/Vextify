using System.Net;
using System.Net.Http;

namespace Vextify.Core;

/// <summary>One shared HttpClient for everything Vextify downloads (GitHub API, raw files, release zips).</summary>
public static class Net
{
    public static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Vextify");   // generic client name only
        return c;
    }
}
