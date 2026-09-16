using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Services;

public sealed class DefaultImageAnalysisServiceTests
{
    /// <summary>
    /// Verifies that the request overload renders the template it was asked for, sends the caption and the
    /// surrounding text along with the image, and parses the structured answer back.
    /// </summary>
    [Fact]
    public async Task DefaultImageAnalysisService_RequestOverload_RendersFigureTemplateAndSendsImage()
    {
        var deployment = new AIDeployment
        {
            ItemId = "deployment-vision",
            Name = "gpt-vision",
        };

        deployment.Put(new AIDeploymentMetadata
        {
            Features = [AIDeploymentFeatureNames.ImageInput],
        });

        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager
            .Setup(manager => manager.ResolveSlotAsync(
                AIDeploymentSlotNames.Vision,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(deployment);

        List<ChatMessage> captured = null;

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<ChatMessage> messages, ChatOptions _, CancellationToken _) => captured = messages.ToList())
            .ReturnsAsync(new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                """
                {
                  "caption": "A scatter plot",
                  "description": "Specific heating demand against the model.",
                  "ocr_text": "y = 1,0892x",
                  "detected_entities": "x axis, y axis"
                }
                """)));

        var clientFactory = new Mock<IAIClientFactory>();
        clientFactory
            .Setup(factory => factory.CreateChatClientAsync(It.IsAny<AIDeployment>()))
            .ReturnsAsync(chatClient.Object);

        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(service => service.RenderAsync(
                AITemplateIds.FigureTranscription,
                It.IsAny<IDictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("TRANSCRIBE THE FIGURE");

        var service = new DefaultImageAnalysisService(
            deploymentManager.Object,
            clientFactory.Object,
            templateService.Object,
            NullLogger<DefaultImageAnalysisService>.Instance);

        var result = await service.AnalyzeAsync(
            new ImageAnalysisRequest
            {
                Content = new byte[] { 1, 2, 3, 4 },
                ContentType = "image/png",
                FileName = "report.pdf-p4-1",
                Caption = "Figure 1. Measured against the model.",
                Context = "The trend line in Figure 1 shows the seasonal drop.",
                Language = "hu-HU",
                TemplateId = AITemplateIds.FigureTranscription,
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("Specific heating demand against the model.", result.Description);
        Assert.Equal("y = 1,0892x", result.OcrText);

        Assert.NotNull(captured);
        Assert.Equal(2, captured.Count);
        Assert.Equal(ChatRole.System, captured[0].Role);
        Assert.Equal("TRANSCRIBE THE FIGURE", captured[0].Text);

        var userText = Assert.IsType<TextContent>(captured[1].Contents[0]);

        Assert.Contains("Figure 1. Measured against the model.", userText.Text, StringComparison.Ordinal);
        Assert.Contains("The trend line in Figure 1 shows the seasonal drop.", userText.Text, StringComparison.Ordinal);
        Assert.Contains("hu-HU", userText.Text, StringComparison.Ordinal);

        var data = Assert.IsType<DataContent>(captured[1].Contents[1]);

        Assert.Equal("image/png", data.MediaType);
    }
}
