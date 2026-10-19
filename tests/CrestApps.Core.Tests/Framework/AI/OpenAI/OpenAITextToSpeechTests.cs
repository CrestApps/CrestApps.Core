#pragma warning disable MEAI001
using System.Net;
using System.Net.Sockets;
using System.Text;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI;
using CrestApps.Core.AI.OpenAI.Services;
using CrestApps.Core.AI.Speech;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Framework.AI.OpenAI;

/// <summary>
/// The OpenAI provider speaks, so a cascaded realtime deployment can use an OpenAI model as its speaking leg.
/// </summary>
/// <remarks>
/// The provider had no text-to-speech client, so a cascade whose speaking leg was <c>gpt-4o-mini-tts</c> failed as the
/// session opened: "The provider 'OpenAI' does not support text-to-speech" (CrestApps.Core#263).
/// </remarks>
public sealed class OpenAITextToSpeechTests
{
    private static ServiceProvider EmptyServices()
        => new ServiceCollection().BuildServiceProvider();

    private static AIProviderConnectionEntry Connection(params (string Key, string Value)[] values)
        => new(values.ToDictionary(pair => pair.Key, object (pair) => pair.Value));

    [Fact]
    public async Task GetSpeechVoicesAsync_ListsEveryVoiceOfTheCurrentModels()
    {
        // Arrange
        var provider = new OpenAIClientProvider(EmptyServices());

        // Act
        var voices = await provider.GetSpeechVoicesAsync(Connection(("ApiKey", "sk-test-key")), "gpt-4o-mini-tts");

        // Assert
        Assert.Equal(
            ["alloy", "ash", "ballad", "cedar", "coral", "echo", "fable", "marin", "nova", "onyx", "sage", "shimmer", "verse"],
            voices.Select(voice => voice.Id));
    }

    [Theory]
    [InlineData("tts-1")]
    [InlineData("tts-1-hd")]
    public async Task GetSpeechVoicesAsync_ListsTheSmallerSetOfTheFirstModels(string model)
    {
        // Arrange
        var provider = new OpenAIClientProvider(EmptyServices());

        // Act
        var voices = await provider.GetSpeechVoicesAsync(Connection(("ApiKey", "sk-test-key")), model);

        // Assert
        Assert.Equal(
            ["alloy", "ash", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer"],
            voices.Select(voice => voice.Id));
    }

    [Fact]
    public async Task GetTextToSpeechClientAsync_WithoutADeploymentName_Throws()
    {
        // Arrange
        var provider = new OpenAIClientProvider(EmptyServices());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await provider.GetTextToSpeechClientAsync(Connection(("ApiKey", "sk-test-key"))));
    }

    [Theory]
    [InlineData("Pcm8000")]
    [InlineData("Pcm16000")]
    [InlineData("pcm48000")]
    public async Task RawPcmAtAnotherRate_IsRefused_RatherThanPlayedAtTheWrongSpeed(string format)
    {
        // Arrange
        // Refused before any request: the endpoint is a closed local port, so a regression fails here, not at OpenAI.
        var provider = new OpenAIClientProvider(EmptyServices());
        var connection = Connection(("ApiKey", "sk-test-key"), ("Endpoint", "http://127.0.0.1:9/v1"));
        var client = await provider.GetTextToSpeechClientAsync(connection, "gpt-4o-mini-tts");

        // Act
        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await client.GetAudioAsync("Hello.", new TextToSpeechOptions { AudioFormat = format }, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("24000 Hz", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACascadesRawPcm_IsAskedForAsOpenAIsPcm_AndComesBackAsRawPcm()
    {
        // Arrange
        // A cascaded realtime session asks its speaking leg for "Pcm24000", Azure Speech's name for raw PCM at the
        // session's rate. OpenAI names the same audio "pcm" and refuses a name it does not know. A loopback listener
        // stands in for OpenAI, keeps the request, and streams back one chunk of audio.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        byte[] samples = [1, 2, 3, 4];
        var request = AnswerOneSpeechRequestAsync(listener, samples, TestContext.Current.CancellationToken);

        var provider = new OpenAIClientProvider(EmptyServices());
        var connection = Connection(("ApiKey", "sk-test-key"), ("Endpoint", $"http://127.0.0.1:{port}/v1"));
        var client = await provider.GetTextToSpeechClientAsync(connection, "gpt-4o-mini-tts");
        var options = new TextToSpeechOptions { AudioFormat = "Pcm24000", VoiceId = "marin" };

        // Act
        List<DataContent> audio = [];

        await foreach (var update in client.GetStreamingAudioAsync("Καλησπέρα.", options, TestContext.Current.CancellationToken))
        {
            audio.AddRange(update.Contents.OfType<DataContent>());
        }

        var sent = await request;

        // Assert
        Assert.Contains("POST /v1/audio/speech", sent, StringComparison.Ordinal);
        Assert.Contains("\"response_format\":\"pcm\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"voice\":\"marin\"", sent, StringComparison.Ordinal);
        Assert.Contains("\"model\":\"gpt-4o-mini-tts\"", sent, StringComparison.Ordinal);
        var chunk = Assert.Single(audio);
        Assert.Equal("audio/l16", chunk.MediaType, ignoreCase: true);
        Assert.Equal(samples, chunk.Data.ToArray());
    }

    [Theory]
    [InlineData("gpt-4o-mini-tts", 1.2f, "\"speed\":1.2", true)]
    [InlineData("tts-1", 1.2f, "\"speed\":1.2", false)]
    [InlineData("tts-1-hd", 10f, "\"speed\":4", false)]
    [InlineData("gpt-4o-mini-tts", 0.1f, "\"speed\":0.25", true)]
    public async Task ASpeakingStyleAndSpeed_AreSentAsTheModelTakesThem(string model, float speed, string expectedSpeed, bool takesInstructions)
    {
        // Arrange
        // A cascade's pace and speaking style (CrestApps.Core#273). gpt-4o-mini-tts takes instructions on how to
        // speak; tts-1 and tts-1-hd take none, and OpenAI accepts speeds from 0.25 to 4 only.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var request = AnswerOneSpeechRequestAsync(listener, [1, 2, 3, 4], TestContext.Current.CancellationToken);

        var provider = new OpenAIClientProvider(EmptyServices());
        var connection = Connection(("ApiKey", "sk-test-key"), ("Endpoint", $"http://127.0.0.1:{port}/v1"));
        var client = await provider.GetTextToSpeechClientAsync(connection, model);
        var options = new TextToSpeechOptions
        {
            AudioFormat = "Pcm24000",
            Speed = speed,
            AdditionalProperties = new() { [TextToSpeechOptionsProperties.Instructions] = "Speak at a brisk phone pace." },
        };

        // Act
        await client.GetAudioAsync("Hello.", options, TestContext.Current.CancellationToken);
        var sent = await request;

        // Assert
        Assert.Contains(expectedSpeed, sent, StringComparison.Ordinal);
        Assert.Equal(takesInstructions, sent.Contains("\"instructions\":\"Speak at a brisk phone pace.\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// Reads one HTTP request from the listener and answers it with a speech stream of one audio chunk.
    /// </summary>
    private static async Task<string> AnswerOneSpeechRequestAsync(TcpListener listener, byte[] samples, CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptSocketAsync(cancellationToken);
        await using var stream = new NetworkStream(socket);

        var received = new List<byte>();
        var buffer = new byte[4096];
        var headerEnd = -1;
        var contentLength = 0;

        while (headerEnd < 0 || received.Count < headerEnd + contentLength)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                break;
            }

            received.AddRange(buffer.AsSpan(0, read).ToArray());

            if (headerEnd < 0)
            {
                var text = Encoding.UTF8.GetString([.. received]);
                var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);

                if (end >= 0)
                {
                    headerEnd = Encoding.UTF8.GetByteCount(text[..end]) + 4;
                    var lengthLine = text[..end].Split("\r\n").FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                    contentLength = lengthLine is null ? 0 : int.Parse(lengthLine["Content-Length:".Length..].Trim());
                }
            }
        }

        var sent = Encoding.UTF8.GetString([.. received]);
        var events =
            $"data: {{\"type\":\"speech.audio.delta\",\"audio\":\"{Convert.ToBase64String(samples)}\"}}\n\n" +
            "data: {\"type\":\"speech.audio.done\",\"usage\":{\"input_tokens\":3,\"output_tokens\":5,\"total_tokens\":8}}\n\n";
        var response = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n" + events;

        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), cancellationToken);
        await stream.FlushAsync(cancellationToken);
        socket.Shutdown(SocketShutdown.Send);

        return sent;
    }
}
