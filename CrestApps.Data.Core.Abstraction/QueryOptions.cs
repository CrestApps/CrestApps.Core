namespace CrestApps.Data.Core.Abstraction
{
    public class QueryOptions
    {
        public bool IsTrackable { get; set; }

        // Here we can add more configuration to configure the query

        public QueryOptions()
        {
            IsTrackable = true;
        }
    }
}

