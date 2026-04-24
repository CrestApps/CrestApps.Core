namespace CrestApps.Core.AI.Models;

public class InitializingAIProviderConnectionContext
{
    public Dictionary<string, object> Values { get; } = [];

    public AIProviderConnection Connection { get; }

    public InitializingAIProviderConnectionContext(AIProviderConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Connection = connection;
    }
}
