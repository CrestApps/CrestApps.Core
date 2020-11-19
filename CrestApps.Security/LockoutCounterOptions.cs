using System;
using System.Collections.Generic;
using System.Linq;

namespace CrestApps.Security
{
    public class LockoutCounterOptions
    {
        //
        // Summary:
        //     Gets or sets the System.TimeSpan a user is locked out for when a lockout occurs based on total attempts
        //
        // Value:
        //     The System.TimeSpan a user is locked out for when a lockout occurs.
        public Dictionary<int, TimeSpan> LockoutTimeSpan { get; private set; }

        public bool EnableLockoutCounter => LockoutTimeSpan.Count > 0;

        public LockoutCounterOptions()
        {
            LockoutTimeSpan = new Dictionary<int, TimeSpan>();
        }

        public LockoutCounterOptions Add(int count, TimeSpan span)
        {
            if (count >= 0 && !LockoutTimeSpan.ContainsKey(count))
            {
                LockoutTimeSpan.Add(count, span);
            }

            return this;
        }

        public TimeSpan? GetBest(int count)
        {
            // Get the values where Key is less than or equal to count
            // Order by the total count
            // get the last record which would be the best one to use
            var sorted = Get();
            var bestValue = sorted.Where(x => x.Key <= count)
                                  .Select(x => x.Value)
                                  .LastOrDefault();

            return bestValue;
        }

        public Dictionary<int, TimeSpan> Get()
        {
            return LockoutTimeSpan.OrderBy(x => x.Key)
                                  .ToDictionary(x => x.Key, x => x.Value);
        }
    }
}
