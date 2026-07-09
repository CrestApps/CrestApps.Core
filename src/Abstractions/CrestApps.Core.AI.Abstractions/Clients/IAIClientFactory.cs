using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Clients;

/// <summary>
/// Defines a factory for creating AI clients from resolved <see cref="AIDeployment"/> instances.
/// Every Create method takes a fully resolved deployment that carries the client name,
/// optional connection reference, and model information.
/// </summary>
public interface IAIClientFactory
{
    /// <summary>
    /// Asynchronously creates an <see cref="IChatClient"/> from the given deployment.
    /// </summary>
    /// <param name="deployment">The AI deployment containing client, connection, and model information.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation, with the created <see cref="IChatClient"/>.
    /// </returns>
    ValueTask<IChatClient> CreateChatClientAsync(AIDeployment deployment);

    /// <summary>
    /// Asynchronously creates an <see cref="IChatClient"/> from the given deployment and applies optional pipeline configuration before building the final client.
    /// </summary>
    /// <param name="deployment">The AI deployment containing client, connection, and model information.</param>
    /// <param name="configurePipeline">An optional delegate that configures the chat-client builder pipeline before the client is built.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation, with the created <see cref="IChatClient"/>.
    /// </returns>
    ValueTask<IChatClient> CreateChatClientAsync(AIDeployment deployment, Action<ChatClientBuilder> configurePipeline);

    /// <summary>
    /// Asynchronously creates an <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> from the given deployment.
    /// </summary>
    /// <param name="deployment">The AI deployment containing client, connection, and model information.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation, with the created <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>.
    /// </returns>
    ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(AIDeployment deployment);

    /// <summary>
    /// Asynchronously creates an <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> from the given deployment and applies optional pipeline configuration before building the final generator.
    /// </summary>
    /// <param name="deployment">The AI deployment containing client, connection, and model information.</param>
    /// <param name="configurePipeline">An optional delegate that configures the embedding-generator builder pipeline before the generator is built.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation, with the created <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>.
    /// </returns>
    ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(AIDeployment deployment, Action<EmbeddingGeneratorBuilder<string, Embedding<float>>> configurePipeline);

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    /// <summary>
    /// Asynchronously creates an <see cref="IImageGenerator"/> from the given deployment.
    /// </summary>
    /// <param name="deployment">The AI deployment containing client, connection, and model information.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation, with the created <see cref="IImageGenerator"/>.
    /// </returns>
    ValueTask<IImageGenerator> CreateImageGeneratorAsync(AIDeployment deployment);

    /// <summary>
    /// Asynchronously creates an <see cref="IImageGenerator"/> from the given deployment and applies optional pipeline configuration before building the final generator.
    /// </summary>
    /// <param name="deployment">The AI deployment containing client, connection, and model information.</param>
    /// <param name="configurePipeline">An optional delegate that configures the image-generator builder pipeline before the generator is built.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation, with the created <see cref="IImageGenerator"/>.
    /// </returns>
    ValueTask<IImageGenerator> CreateImageGeneratorAsync(AIDeployment deployment, Action<ImageGeneratorBuilder> configurePipeline);
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    /// <summary>
    /// Asynchronously creates an <see cref="ISpeechToTextClient"/> from the given deployment.
    /// </summary>
    /// <param name="deployment">The AI deployment containing client, connection, and model information.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation, with the created <see cref="ISpeechToTextClient"/>.
    /// </returns>
    ValueTask<ISpeechToTextClient> CreateSpeechToTextClientAsync(AIDeployment deployment);

    /// <summary>
    /// Asynchronously creates an <see cref="ISpeechToTextClient"/> from the given deployment and applies optional pipeline configuration before building the final client.
    /// </summary>
    /// <param name="deployment">The AI deployment containing client, connection, and model information.</param>
    /// <param name="configurePipeline">An optional delegate that configures the speech-to-text builder pipeline before the client is built.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation, with the created <see cref="ISpeechToTextClient"/>.
    /// </returns>
    ValueTask<ISpeechToTextClient> CreateSpeechToTextClientAsync(AIDeployment deployment, Action<SpeechToTextClientBuilder> configurePipeline);
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    /// <summary>
    /// Asynchronously creates an <see cref="ITextToSpeechClient"/> from the given deployment.
    /// </summary>
    /// <param name="deployment">The AI deployment containing client, connection, and model information.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation, with the created <see cref="ITextToSpeechClient"/>.
    /// </returns>
    ValueTask<ITextToSpeechClient> CreateTextToSpeechClientAsync(AIDeployment deployment);

    /// <summary>
    /// Asynchronously creates an <see cref="ITextToSpeechClient"/> from the given deployment and applies optional pipeline configuration before building the final client.
    /// </summary>
    /// <param name="deployment">The AI deployment containing client, connection, and model information.</param>
    /// <param name="configurePipeline">An optional delegate that configures the text-to-speech builder pipeline before the client is built.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation, with the created <see cref="ITextToSpeechClient"/>.
    /// </returns>
    ValueTask<ITextToSpeechClient> CreateTextToSpeechClientAsync(AIDeployment deployment, Action<TextToSpeechClientBuilder> configurePipeline);
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
}
