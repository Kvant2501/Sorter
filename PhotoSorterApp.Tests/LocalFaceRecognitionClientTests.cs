using NUnit.Framework;
using PhotoSorterApp.Services;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoSorterApp.Tests;

[TestFixture]
public class LocalFaceRecognitionClientTests
{
    [Test]
    public async Task AnalyzeAsync_ParsesFacesAndTags()
    {
        var handler = new StubHttpMessageHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            StringAssert.Contains("imagePath", body);
            StringAssert.Contains("photo.jpg", body);

            var json = JsonSerializer.Serialize(new
            {
                model = "insightface",
                faces = new[]
                {
                    new
                    {
                        x = 10,
                        y = 20,
                        width = 50,
                        height = 60,
                        confidence = 0.95,
                        embedding = new[] {0.1f, 0.2f},
                        tags = new[] {"portrait", "indoor"}
                    }
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:5272") };
        var client = new LocalFaceRecognitionClient(http);

        var result = await client.AnalyzeAsync("C:/photos/photo.jpg");

        Assert.That(result.ModelName, Is.EqualTo("insightface"));
        Assert.That(result.Faces.Count, Is.EqualTo(1));
        Assert.That(result.Faces[0].Embedding.Length, Is.EqualTo(2));
        CollectionAssert.Contains(result.Faces[0].SuggestedTags, "portrait");
    }

    [Test]
    public async Task AnalyzeBatchAsync_FallsBackToAnalyze_WhenBatchEndpointMissing()
    {
        var calls = new List<string>();

        var handler = new StubHttpMessageHandler(async request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);

            if (request.RequestUri!.AbsolutePath.EndsWith("/analyze-batch"))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var body = await request.Content!.ReadAsStringAsync();
            var image = body.Contains("a.jpg") ? "a" : "b";
            var json = JsonSerializer.Serialize(new { model = "fallback", faces = new object[0] });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost:5272") };
        var client = new LocalFaceRecognitionClient(http);

        var result = await client.AnalyzeBatchAsync(new[] { "C:/photos/a.jpg", "C:/photos/b.jpg" });

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(calls[0], Is.EqualTo("/analyze-batch"));
        Assert.That(calls.Count, Is.EqualTo(3));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
