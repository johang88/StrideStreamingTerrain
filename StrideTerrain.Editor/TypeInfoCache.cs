using Stride.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace StrideTerrain.Editor;

public static class TypeInfoCache
{
    public static Dictionary<string, List<TypeInfo>> LayerTypesByCategory;
    public static Dictionary<Type, TypeInfo> LayerTypesByType;

    public static List<TypeInfo> BlendModes;
    public static Dictionary<Type, TypeInfo> BlendModesByType;

    static TypeInfoCache()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var layerTypes = GetTypeInfos(assembly, typeof(ITerrainLayerType));

        LayerTypesByCategory = layerTypes
            .GroupBy(x => x.Display?.Category ?? "")
            .ToDictionary(x => x.Key, x => x.ToList());

        LayerTypesByType = layerTypes.ToDictionary(x => x.Type);

        BlendModes = GetTypeInfos(assembly, typeof(IBlendMode));
        BlendModesByType = BlendModes.ToDictionary(x => x.Type);

        static List<TypeInfo> GetTypeInfos(Assembly assembly, Type interfaceType)
        {
            var layerTypes = assembly.GetTypes()
                .Where(t => interfaceType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .Select(t =>
                {
                    var display = t.GetCustomAttribute<DisplayAttribute>();
                    return new TypeInfo(t, display);
                })
                .ToList();

            return layerTypes;
        }
    }

    public static string GetLayerName(ITerrainLayerType terrainLayerType)
    {
        var typeInfo = LayerTypesByType[terrainLayerType.GetType()];
        return typeInfo.Display?.Name ?? typeInfo.Type.Name;
    }

    public static string GetBlendModeName(IBlendMode blendMode)
    {
        var typeInfo = BlendModesByType[blendMode.GetType()];
        return typeInfo.Display?.Name ?? typeInfo.Type.Name;
    }
}
