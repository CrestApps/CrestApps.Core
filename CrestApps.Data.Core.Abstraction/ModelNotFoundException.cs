using System;

namespace CrestApps.Data.Core.Abstraction
{
    [Serializable]
    public class ModelNotFoundException : Exception
    {
        public ModelNotFoundException()
            : base("The requested model does not exists")
        {

        }

        public ModelNotFoundException(string message)
            : base(message)
        {

        }
    }
}
