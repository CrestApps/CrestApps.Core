using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CrestApps.Common.Helpers
{
    public class AssemblyHelpers
    {
        public static void LoadAllAssumblies()
        {
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies().ToList();
            var loadedPaths = loadedAssemblies.Select(a => a.Location).ToArray();

            var referencedPaths = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll");
            var toLoad = referencedPaths.Where(r => !loadedPaths.Contains(r, StringComparer.InvariantCultureIgnoreCase)).ToList();

            toLoad.ForEach(path =>
            {
                try
                {
                    // try loading all you can
                    loadedAssemblies.Add(AppDomain.CurrentDomain.Load(AssemblyName.GetAssemblyName(path)));
                }
                catch { }
            });
        }
    }
}
