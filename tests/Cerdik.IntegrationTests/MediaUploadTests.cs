using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cerdik.Application.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Cerdik.IntegrationTests;

[Collection("api")]
public class MediaUploadTests
{
    private readonly ApiFactory _factory;

    public MediaUploadTests(SeededApiFixture fixture) => _factory = fixture.Factory;

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest("admin@cerdik.my", "Admin!2345"));
        login.EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Admin_can_upload_and_then_list_media()
    {
        var client = await AdminClientAsync();

        // A 1x1 transparent PNG.
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(png);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "pixel.png");
        form.Add(new StringContent("A tiny test pixel"), "altText");

        var upload = await client.PostAsync("/admin/media", form);
        upload.StatusCode.Should().Be(HttpStatusCode.OK);

        var asset = await upload.Content.ReadFromJsonAsync<MediaAssetDto>(TestJson.Options);
        asset!.ContentType.Should().Be("image/png");
        asset.Url.Should().NotBeNullOrEmpty();
        asset.AltText.Should().Be("A tiny test pixel");

        var list = await client.GetFromJsonAsync<List<MediaAssetDto>>("/admin/media", TestJson.Options);
        list!.Should().Contain(m => m.Id == asset.Id);
    }

    [Fact]
    public async Task Upload_rejects_unsupported_type()
    {
        var client = await AdminClientAsync();

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[] { 1, 2, 3 });
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-msdownload");
        form.Add(fileContent, "file", "evil.exe");

        var upload = await client.PostAsync("/admin/media", form);
        upload.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
