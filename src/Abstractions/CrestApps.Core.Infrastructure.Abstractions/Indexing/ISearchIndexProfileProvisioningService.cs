using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.Infrastructure.Indexing;

public interface ISearchIndexProfileProvisioningService
{
    Task<ValidationResultDetails> CreateAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default);
}
