namespace CrestApps.Core.Models;

public sealed class PageResult<T>
{
    public int Count { get; set; }

    public IReadOnlyCollection<T> Entries { get; set; }
}
