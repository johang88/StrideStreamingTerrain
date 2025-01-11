using Stride.Core;
using Stride.Core.Reflection;
using System.Reflection;

namespace StrideTerrain;
internal class Module
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // Make sure that this assembly is registered
        AssemblyRegistry.Register(typeof(Module).GetTypeInfo().Assembly, AssemblyCommonCategories.Assets);
    }
}