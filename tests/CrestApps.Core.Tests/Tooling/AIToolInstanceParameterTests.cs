using System.Net;
using System.Security.Claims;
using System.Text.Json;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.Tooling.Instances;
using CrestApps.Core.AI.Tooling.Parameters;
using CrestApps.Core.Startup.Shared.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace CrestApps.Core.Tests.Tooling;

public sealed class AIToolInstanceParameterTests
{
    [Fact]
    public void SchemaBuilder_DeclaresModelParametersOnly()
    {
        var parameters = new List<AIToolInstanceParameter>
        {
            new()
            {
                Name = "orderId",
                Description = "The order to look up.",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Model,
                Required = true,
                Binding = "Query:order_id",
            },
            new()
            {
                Name = "tenantId",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Fixed,
                DefaultValue = "acme",
                Binding = "Query:tenant",
            },
            new()
            {
                Name = "userId",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Context,
                ContextKey = AIToolParameterContextKeys.UserId,
                Binding = "Header:X-User",
            },
        };

        var schema = AIToolParameterSchemaBuilder.Merge(null, parameters);
        var properties = schema.GetProperty("properties");

        Assert.True(properties.TryGetProperty("orderId", out _));

        // A parameter the model can see is a parameter the model will try to fill, which would defeat the
        // point of pinning or injecting the value.
        Assert.False(properties.TryGetProperty("tenantId", out _));
        Assert.False(properties.TryGetProperty("userId", out _));

        var required = schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(["orderId"], required);
    }

    [Fact]
    public void SchemaBuilder_EmitsEnumAndDefaultHint()
    {
        var parameters = new List<AIToolInstanceParameter>
        {
            new()
            {
                Name = "status",
                Description = "The status filter.",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Model,
                AllowedValues = ["open", "closed"],
                DefaultValue = "open",
                Binding = "Query:status",
            },
        };

        var schema = AIToolParameterSchemaBuilder.Merge(null, parameters);
        var property = schema.GetProperty("properties").GetProperty("status");

        Assert.Equal(["open", "closed"], property.GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Contains("Defaults to open", property.GetProperty("description").GetString());
    }

    [Fact]
    public void SchemaBuilder_IsStrictEligibleOnlyWhenEveryArgumentIsARequiredScalar()
    {
        List<AIToolInstanceParameter> required =
        [
            new() { Name = "a", Fill = AIToolParameterFill.Model, Required = true, Type = AIToolParameterType.String },
        ];

        Assert.True(AIToolParameterSchemaBuilder.IsStrictEligible(false, required));

        // An open-ended source argument such as the HTTP body cannot be expressed under strict mode.
        Assert.False(AIToolParameterSchemaBuilder.IsStrictEligible(true, required));

        List<AIToolInstanceParameter> optional =
        [
            new() { Name = "a", Fill = AIToolParameterFill.Model, Required = false, Type = AIToolParameterType.String },
        ];

        Assert.False(AIToolParameterSchemaBuilder.IsStrictEligible(false, optional));
    }

    [Theory]
    [InlineData("3", AIToolParameterType.Integer, true)]
    [InlineData("3.5", AIToolParameterType.Integer, false)]
    [InlineData("true", AIToolParameterType.Boolean, true)]
    [InlineData("yes", AIToolParameterType.Boolean, false)]
    [InlineData("2.5", AIToolParameterType.Number, true)]
    public void ValueConverter_CoercesLooselyTypedModelInput(string raw, AIToolParameterType type, bool expected)
    {
        Assert.Equal(expected, AIToolParameterValueConverter.TryConvert(raw, type, out _));
    }

    [Fact]
    public void Binder_AppliesDefaultWhenTheModelOmitsAnOptionalParameter()
    {
        List<AIToolInstanceParameter> parameters =
        [
            new()
            {
                Name = "limit",
                Description = "How many rows.",
                Type = AIToolParameterType.Integer,
                Fill = AIToolParameterFill.Model,
                DefaultValue = 25,
                Binding = "Query:limit",
            },
        ];

        var resolution = AIToolParameterBinder.Resolve(parameters, [], null);

        Assert.True(resolution.Succeeded);
        Assert.Equal("25", Assert.Single(resolution.Parameters).StringValue);
    }

    [Fact]
    public void Binder_ReportsAMissingRequiredParameterInsteadOfDroppingIt()
    {
        List<AIToolInstanceParameter> parameters =
        [
            new()
            {
                Name = "orderId",
                Description = "The order.",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Model,
                Required = true,
                Binding = "Query:order_id",
            },
        ];

        var resolution = AIToolParameterBinder.Resolve(parameters, [], null);

        Assert.False(resolution.Succeeded);
        Assert.Contains("orderId", Assert.Single(resolution.Errors));
    }

    [Fact]
    public void Binder_RejectsAValueOutsideTheAllowedSet()
    {
        List<AIToolInstanceParameter> parameters =
        [
            new()
            {
                Name = "status",
                Description = "The status.",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Model,
                AllowedValues = ["open", "closed"],
                Binding = "Query:status",
            },
        ];

        var arguments = new AIFunctionArguments { ["status"] = "deleted" };
        var resolution = AIToolParameterBinder.Resolve(parameters, arguments, null);

        Assert.False(resolution.Succeeded);
        Assert.Contains("must be one of", Assert.Single(resolution.Errors));
    }

    [Fact]
    public void Binder_IgnoresAModelSuppliedValueForAContextParameter()
    {
        List<AIToolInstanceParameter> parameters =
        [
            new()
            {
                Name = "userId",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Context,
                ContextKey = AIToolParameterContextKeys.UserId,
                Binding = "Query:user_id",
            },
        ];

        // A prompt-injected model may still try to pass the value. It must never win over the context.
        var arguments = new AIFunctionArguments { ["userId"] = "somebody-else" };
        var resolution = AIToolParameterBinder.Resolve(parameters, arguments, BuildUserProvider("user-42"));

        Assert.True(resolution.Succeeded);
        Assert.Equal("user-42", Assert.Single(resolution.Parameters).StringValue);
    }

    [Fact]
    public void Binder_UnprotectsASecretFixedValue()
    {
        List<AIToolInstanceParameter> parameters =
        [
            new()
            {
                Name = "apiToken",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Fixed,
                DefaultValue = "protected-value",
                IsSecret = true,
                Binding = "Header:X-Token",
            },
        ];

        var resolution = AIToolParameterBinder.Resolve(parameters, [], null, _ => "clear-value");

        Assert.True(resolution.Succeeded);
        Assert.Equal("clear-value", Assert.Single(resolution.Parameters).StringValue);
    }

    [Fact]
    public void Validator_RejectsParametersOnASourceThatCannotPlaceThem()
    {
        List<AIToolInstanceParameter> parameters =
        [
            new() { Name = "anything", Fill = AIToolParameterFill.Model, Description = "x", Binding = "Query:anything" },
        ];

        var errors = AIToolParameterValidator.Validate(parameters, capabilities: null);

        Assert.Contains(errors, error => error.Error.Contains("does not support parameters"));
    }

    [Fact]
    public void Validator_RejectsReservedDuplicateAndMalformedNames()
    {
        var capabilities = HttpApiRequestParameterBindings.CreateCapabilities();

        List<AIToolInstanceParameter> parameters =
        [
            new() { Name = "query", Fill = AIToolParameterFill.Model, Description = "x", Binding = "Query:a" },
            new() { Name = "9bad", Fill = AIToolParameterFill.Model, Description = "x", Binding = "Query:b" },
            new() { Name = "dup", Fill = AIToolParameterFill.Model, Description = "x", Binding = "Query:c" },
            new() { Name = "dup", Fill = AIToolParameterFill.Model, Description = "x", Binding = "Query:d" },
        ];

        var errors = AIToolParameterValidator.Validate(parameters, capabilities);

        Assert.Contains(errors, error => error.Error.Contains("reserved"));
        Assert.Contains(errors, error => error.Error.Contains("not a valid parameter name"));
        Assert.Contains(errors, error => error.Error.Contains("unique"));
    }

    [Fact]
    public void Validator_RejectsAModelFilledHeader()
    {
        var capabilities = HttpApiRequestParameterBindings.CreateCapabilities();

        List<AIToolInstanceParameter> parameters =
        [
            new()
            {
                Name = "impersonate",
                Description = "x",
                Fill = AIToolParameterFill.Model,
                Binding = "Header:X-User",
            },
        ];

        var errors = AIToolParameterValidator.Validate(parameters, capabilities);

        Assert.Contains(errors, error => error.Error.Contains("cannot be placed"));
    }

    [Fact]
    public void Validator_RequiresADescriptionForModelFilledParameters()
    {
        var capabilities = HttpApiRequestParameterBindings.CreateCapabilities();

        List<AIToolInstanceParameter> parameters =
        [
            new() { Name = "orderId", Fill = AIToolParameterFill.Model, Binding = "Query:order_id" },
        ];

        var errors = AIToolParameterValidator.Validate(parameters, capabilities);

        Assert.Contains(errors, error => error.Error.Contains("needs a description"));
    }

    [Fact]
    public void Validator_RequiresAPathParameterToAlwaysProduceAValue()
    {
        var capabilities = HttpApiRequestParameterBindings.CreateCapabilities();

        List<AIToolInstanceParameter> parameters =
        [
            new()
            {
                Name = "orderId",
                Description = "The order.",
                Fill = AIToolParameterFill.Model,
                Required = false,
                Binding = "Path:orderId",
            },
        ];

        var errors = AIToolParameterValidator.Validate(parameters, capabilities);

        Assert.Contains(errors, error => error.Error.Contains("must be required"));
    }

    [Fact]
    public async Task HttpFunction_PlacesParametersIntoThePathQueryHeaderAndBody()
    {
        var handler = new ParameterCapturingHandler();
        var services = BuildServices(handler, "user-42");

        var instance = CreateInstance(
        [
            new()
            {
                Name = "orderId",
                Description = "The order to update.",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Model,
                Required = true,
                Binding = "Path:orderId",
            },
            new()
            {
                Name = "notify",
                Description = "Whether to notify the customer.",
                Type = AIToolParameterType.Boolean,
                Fill = AIToolParameterFill.Model,
                DefaultValue = false,
                Binding = "Query:notify",
            },
            new()
            {
                Name = "actingUser",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Context,
                ContextKey = AIToolParameterContextKeys.UserId,
                Binding = "Header:X-Acting-User",
            },
            new()
            {
                Name = "status",
                Description = "The new status.",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Model,
                Required = true,
                Binding = "Body:order.status",
            },
        ]);

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com/v1",
            PathTemplate = "orders/{orderId}",
            HttpMethod = "POST",
            AllowModelProvidedPath = false,
            AllowModelProvidedQuery = false,
            AllowModelProvidedBody = false,
        };

        var function = new HttpApiRequestToolFunction("update_order", "Updates an order.", settings, instance);

        var arguments = new AIFunctionArguments
        {
            ["orderId"] = "A-100",
            ["notify"] = true,
            ["status"] = "shipped",
            Services = services,
        };

        await function.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://api.example.com/v1/orders/A-100?notify=true", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("user-42", handler.LastRequest.Headers.GetValues("X-Acting-User").Single());
        Assert.Equal("{\"order\":{\"status\":\"shipped\"}}", handler.LastRequestBody);
    }

    [Fact]
    public async Task HttpFunction_EscapesAPathParameterSoItCannotTraverse()
    {
        var handler = new ParameterCapturingHandler();
        var services = BuildServices(handler, "user-1");

        var instance = CreateInstance(
        [
            new()
            {
                Name = "segment",
                Description = "The segment.",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Model,
                Required = true,
                Binding = "Path:segment",
            },
        ]);

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com/v1",
            PathTemplate = "orders/{segment}",
            AllowModelProvidedPath = false,
            AllowModelProvidedQuery = false,
            AllowModelProvidedBody = false,
        };

        var function = new HttpApiRequestToolFunction("get_order", "Gets an order.", settings, instance);

        var arguments = new AIFunctionArguments
        {
            ["segment"] = "../../admin/secrets",
            Services = services,
        };

        await function.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(
            "https://api.example.com/v1/orders/..%2F..%2Fadmin%2Fsecrets",
            handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task HttpFunction_ReturnsAnErrorTheModelCanActOnWhenARequiredParameterIsMissing()
    {
        var handler = new ParameterCapturingHandler();
        var services = BuildServices(handler, "user-1");

        var instance = CreateInstance(
        [
            new()
            {
                Name = "orderId",
                Description = "The order.",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Model,
                Required = true,
                Binding = "Query:order_id",
            },
        ]);

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com/v1",
            AllowModelProvidedPath = false,
            AllowModelProvidedQuery = false,
            AllowModelProvidedBody = false,
        };

        var function = new HttpApiRequestToolFunction("get_order", "Gets an order.", settings, instance);

        var result = await function.InvokeAsync(
            new AIFunctionArguments { Services = services },
            TestContext.Current.CancellationToken);

        Assert.Null(handler.LastRequest);

        using var document = JsonDocument.Parse(result!.ToString()!);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("orderId", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void HttpFunction_ClosesTheFreeFormArgumentForABoundPlacement()
    {
        var instance = CreateInstance(
        [
            new()
            {
                Name = "city",
                Description = "The city.",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Model,
                Required = true,
                Binding = "Query:city",
            },
        ]);

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com/v1",
            AllowModelProvidedPath = false,
            AllowModelProvidedQuery = true,
            AllowModelProvidedBody = false,
        };

        var function = new HttpApiRequestToolFunction("weather", "Gets the weather.", settings, instance);
        var properties = function.JsonSchema.GetProperty("properties");

        // The typed parameter replaces the untyped bag rather than sitting alongside it.
        Assert.True(properties.TryGetProperty("city", out _));
        Assert.False(properties.TryGetProperty("query", out _));
        Assert.True((bool)function.AdditionalProperties["Strict"]);
    }

    [Fact]
    public void HttpFunction_LeavesTheSchemaUnchangedWhenNoParametersAreDeclared()
    {
        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com/v1",
            AllowModelProvidedPath = true,
            AllowModelProvidedQuery = true,
            AllowModelProvidedBody = true,
        };

        var function = new HttpApiRequestToolFunction("legacy", "Existing instance.", settings);
        var properties = function.JsonSchema.GetProperty("properties");

        Assert.True(properties.TryGetProperty("path", out _));
        Assert.True(properties.TryGetProperty("query", out _));
        Assert.True(properties.TryGetProperty("body", out _));
        Assert.False((bool)function.AdditionalProperties["Strict"]);
    }

    [Fact]
    public void EditorRows_KeepAStoredSecretWhenTheValueIsLeftBlank()
    {
        List<AIToolInstanceParameter> stored =
        [
            new()
            {
                Name = "apiToken",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Fixed,
                DefaultValue = "already-protected",
                IsSecret = true,
                Binding = "Header:X-Token",
            },
        ];

        var rows = AIToolInstanceParameterViewModel.FromParameters(stored);

        // The stored secret is never rendered back into the editor.
        Assert.Null(Assert.Single(rows).FixedValue);
        Assert.True(rows[0].HasStoredSecret);

        var roundTripped = AIToolInstanceParameterViewModel.ToParameters(rows, stored);

        Assert.Equal("already-protected", Assert.Single(roundTripped).DefaultValue);
    }

    [Fact]
    public void EditorRows_ProtectANewlyEnteredSecret()
    {
        List<AIToolInstanceParameterViewModel> rows =
        [
            new()
            {
                Name = "apiToken",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Fixed,
                FixedValue = "brand-new",
                IsSecret = true,
                BindingTarget = "Header",
                BindingName = "X-Token",
            },
        ];

        var parameters = AIToolInstanceParameterViewModel.ToParameters(rows, existing: null, protect: value => "protected:" + value);

        Assert.Equal("protected:brand-new", Assert.Single(parameters).DefaultValue);
        Assert.Equal("Header:X-Token", parameters[0].Binding);
    }

    [Fact]
    public void EditorRows_OmitTheBindingNameWhenItMatchesTheParameterName()
    {
        List<AIToolInstanceParameterViewModel> rows =
        [
            new()
            {
                Name = "status",
                Description = "The status.",
                Fill = AIToolParameterFill.Model,
                BindingTarget = "Query",
                BindingName = "status",
            },
        ];

        var parameters = AIToolInstanceParameterViewModel.ToParameters(rows, existing: null);

        Assert.Equal("Query", Assert.Single(parameters).Binding);
    }

    [Fact]
    public async Task Registry_MaterializesAToolWhoseSchemaCarriesTheDeclaredParameters()
    {
        // The registry is the path a configured instance actually takes to ChatOptions.Tools. A schema
        // that only holds together when the function is constructed by hand proves nothing about what the
        // model is shown.
        var instance = CreateInstance(
        [
            new()
            {
                Name = "orderId",
                Description = "The order to look up.",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Model,
                Required = true,
                Binding = "Query:order_id",
            },
            new()
            {
                Name = "actingUser",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Context,
                ContextKey = AIToolParameterContextKeys.UserId,
                Binding = "Header:X-Acting-User",
            },
        ]);

        instance.Put(new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com/v1",
            AllowModelProvidedPath = false,
            AllowModelProvidedQuery = false,
            AllowModelProvidedBody = false,
        });

        var services = BuildRegistryServices(instance);
        var registryProvider = new ToolInstanceRegistryProvider(
            services,
            services.GetRequiredService<ILogger<ToolInstanceRegistryProvider>>());

        var context = new AICompletionContext { ToolInstanceNames = [instance.Name] };
        var entries = await registryProvider.GetToolsAsync(context, TestContext.Current.CancellationToken);

        var tool = await Assert.Single(entries).CreateAsync(services);
        var function = Assert.IsAssignableFrom<AIFunction>(tool);
        var properties = function.JsonSchema.GetProperty("properties");

        Assert.True(properties.TryGetProperty("orderId", out var orderId));
        Assert.Equal("string", orderId.GetProperty("type").GetString());
        Assert.Equal("The order to look up.", orderId.GetProperty("description").GetString());

        Assert.Equal(
            ["orderId"],
            function.JsonSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray());

        // The context parameter is resolved server-side, so it must not be advertised to the model.
        Assert.False(properties.TryGetProperty("actingUser", out _));
    }

    [Fact]
    public async Task ChatClient_IsGivenTheParametersAndTheModelSuppliedValuesReachTheRequest()
    {
        // End to end over the real Microsoft.Extensions.AI tool-calling path: the tool definition handed
        // to the client must advertise the parameters, and the values the model then sends must arrive on
        // the outbound HTTP request. Either half failing makes the feature useless.
        var handler = new ParameterCapturingHandler();
        var services = BuildServices(handler, "user-42");

        var instance = CreateInstance(
        [
            new()
            {
                Name = "city",
                Description = "The city to look up.",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Model,
                Required = true,
                Binding = "Query:q",
            },
            new()
            {
                Name = "units",
                Description = "The unit system.",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Model,
                AllowedValues = ["metric", "imperial"],
                DefaultValue = "metric",
                Binding = "Query:units",
            },
            new()
            {
                Name = "actingUser",
                Type = AIToolParameterType.String,
                Fill = AIToolParameterFill.Context,
                ContextKey = AIToolParameterContextKeys.UserId,
                Binding = "Header:X-Acting-User",
            },
        ]);

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = "https://api.example.com/v1",
            AllowModelProvidedPath = false,
            AllowModelProvidedQuery = false,
            AllowModelProvidedBody = false,
        };

        var function = new HttpApiRequestToolFunction("get_weather", "Gets the weather.", settings, instance);

        // Stands in for the provider: records the tool definition it is given, then answers with a tool
        // call the way a model would.
        var model = new ToolDefinitionCapturingChatClient("get_weather", new Dictionary<string, object>
        {
            ["city"] = "Seattle",
        });

        // The service provider flows into AIFunctionArguments.Services, which is how the tool reaches the
        // HTTP client and the context resolvers at invocation time.
        using var client = new FunctionInvokingChatClient(model, loggerFactory: null, functionInvocationServices: services);

        var options = new ChatOptions
        {
            Tools = [function],
        };

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "What is the weather in Seattle?")],
            options,
            TestContext.Current.CancellationToken);

        // What the model was shown. The definition is sent on every round trip — the tool call and the
        // follow-up that carries its result — so every one of them must advertise the parameters.
        Assert.Equal(2, model.SeenTools.Count);

        var advertised = Assert.IsAssignableFrom<AIFunction>(model.SeenTools[0]);
        var properties = advertised.JsonSchema.GetProperty("properties");

        Assert.True(properties.TryGetProperty("city", out var city));
        Assert.Equal("The city to look up.", city.GetProperty("description").GetString());
        Assert.True(properties.TryGetProperty("units", out var units));
        Assert.Equal(
            ["metric", "imperial"],
            units.GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.False(properties.TryGetProperty("actingUser", out _));

        // What the call actually carried: the model's value, the omitted parameter's default, and the
        // context value the model never saw.
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(
            "https://api.example.com/v1?q=Seattle&units=metric",
            handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("user-42", handler.LastRequest.Headers.GetValues("X-Acting-User").Single());
    }

    private static ServiceProvider BuildRegistryServices(AIToolInstance instance)
    {
        var catalog = new Mock<INamedCatalog<AIToolInstance>>();
        catalog
            .Setup(c => c.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) =>
                string.Equals(instance.Name, name, StringComparison.OrdinalIgnoreCase) ? instance : null);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(catalog.Object);
        services.AddKeyedSingleton<IAIToolInstanceSource, HttpApiRequestToolInstanceSource>(HttpApiRequestToolConstants.SourceName);

        return services.BuildServiceProvider();
    }

    private sealed class ToolDefinitionCapturingChatClient : IChatClient
    {
        private readonly string _functionName;
        private readonly Dictionary<string, object> _arguments;
        private bool _called;

        public ToolDefinitionCapturingChatClient(string functionName, Dictionary<string, object> arguments)
        {
            _functionName = functionName;
            _arguments = arguments;
        }

        public List<AITool> SeenTools { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions options = null,
            CancellationToken cancellationToken = default)
        {
            if (options?.Tools is not null)
            {
                SeenTools.AddRange(options.Tools);
            }

            if (_called)
            {
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")));
            }

            _called = true;

            var call = new FunctionCallContent(Guid.NewGuid().ToString("N"), _functionName, _arguments);

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static AIToolInstance CreateInstance(List<AIToolInstanceParameter> parameters)
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-1",
            Source = HttpApiRequestToolConstants.SourceName,
            Name = "test_instance",
        };

        instance.Put(new AIToolInstanceParametersMetadata { Parameters = parameters });

        return instance;
    }

    private static ServiceProvider BuildUserProvider(string userId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test")),
            },
        });
        services.AddSingleton<IAIToolParameterContextResolver, DefaultAIToolParameterContextResolver>();

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildServices(HttpMessageHandler handler, string userId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpClientFactory>(new ParameterHttpClientFactory(handler));
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test")),
            },
        });
        services.AddSingleton<IAIToolParameterContextResolver, DefaultAIToolParameterContextResolver>();

        return services.BuildServiceProvider();
    }

    private sealed class ParameterHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public ParameterHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class ParameterCapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage LastRequest { get; private set; }

        public string LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            };
        }
    }
}
