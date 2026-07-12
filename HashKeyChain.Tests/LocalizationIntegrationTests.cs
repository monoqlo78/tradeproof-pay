using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HashKeyChain.Tests;

/// <summary>
/// End-to-end tests over the real request pipeline: default culture, cookie
/// switching, culture endpoint (cookie persistence, fallback, open-redirect
/// protection) and the localized client-resource endpoint.
/// </summary>
public class LocalizationIntegrationTests : IClassFixture<TestAppFactory>
{
    private const string CultureCookieName = ".AspNetCore.Culture";

    private readonly TestAppFactory _factory;

    public LocalizationIntegrationTests(TestAppFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Default_culture_renders_japanese()
    {
        var client = CreateClient();
        var html = await client.GetStringAsync("/");

        Assert.Contains("<html lang=\"ja\"", html);
    }

    [Fact]
    public async Task Cookie_switches_ui_to_english()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", $"{CultureCookieName}=c=en-US|uic=en-US");

        var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("<html lang=\"en\"", html);
        Assert.Contains("Hello, world!", html);
    }

    [Fact]
    public async Task SetCulture_persists_cookie_and_redirects_to_local_url()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/Culture/Set?culture=en-US&redirectUri=%2Fweather");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/weather", response.Headers.Location?.OriginalString);

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("en-US", setCookie);
    }

    [Fact]
    public async Task SetCulture_blocks_open_redirect()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/Culture/Set?culture=en-US&redirectUri=https%3A%2F%2Fevil.com");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SetCulture_falls_back_to_japanese_for_unsupported_culture()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/Culture/Set?culture=fr-FR&redirectUri=%2F");

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("ja-JP", setCookie);
    }

    [Fact]
    public async Task ClientResources_endpoint_is_localized()
    {
        var client = CreateClient();

        var jaJson = await client.GetStringAsync("/api/clientresources");
        Assert.Contains("ウォレットを接続しました。", jaJson);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/clientresources");
        request.Headers.Add("Cookie", $"{CultureCookieName}=c=en-US|uic=en-US");
        var enResponse = await client.SendAsync(request);
        var enJson = await enResponse.Content.ReadAsStringAsync();

        Assert.Contains("Wallet connected.", enJson);
        // The client keys themselves must never be translated.
        Assert.Contains("walletConnected", enJson);
    }
}
