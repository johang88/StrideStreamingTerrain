using Stride.Core.Annotations;
using Stride.Core.Diagnostics;
using Stride.Engine;
using Stride.Games;
using Stride.Rendering;
using System.Collections.Generic;

namespace StrideTerrain.Rendering;

public class ProfilingKeyRenderProcessor : EntityProcessor<ProfilingKeyComponent, ProfilingKeyRenderData>, IEntityComponentRenderProcessor
{
    public VisibilityGroup VisibilityGroup { get; set; } = null!;

    private readonly Dictionary<RenderModel, ProfilingKey?> _modelToProfilingKeyMap = [];

    protected override ProfilingKeyRenderData GenerateComponentData([NotNull] Entity entity, [NotNull] ProfilingKeyComponent component)
        => new ProfilingKeyRenderData();

    public override void Update(GameTime time)
    {
        base.Update(time);

        var modelRenderProcessor = EntityManager.GetProcessor<ModelRenderProcessor>();
        if (modelRenderProcessor == null)
            return; // Just wait until it's available.

        foreach (var pair in ComponentDatas)
        {
            var component = pair.Key;
            var data = pair.Value;
            var modelComponent = component.Entity.Get<ModelComponent>();

            if (modelComponent == null)
            {
                RemoveRenderModel(data.RenderModel);
                data.RenderModel = null;

                continue;
            }

            if (modelRenderProcessor.RenderModels.TryGetValue(modelComponent, out var renderModel))
            {
                if (data.RenderModel != null && data.RenderModel != renderModel)
                {
                    RemoveRenderModel(data.RenderModel);
                }

                data.RenderModel = renderModel;
                _modelToProfilingKeyMap[data.RenderModel] = component.ProfilingKey;
            }
            else if (data.RenderModel != null)
            {
                RemoveRenderModel(data.RenderModel);
                _modelToProfilingKeyMap.Remove(data.RenderModel);
            }
        }
    }

    void RemoveRenderModel(RenderModel? renderModel)
    {
        if (renderModel != null)
        {
            _modelToProfilingKeyMap.Remove(renderModel);
        }
    }


    protected override void OnSystemAdd()
    {
        base.OnSystemAdd();

        VisibilityGroup.Tags.Set(ProfilingKeyRenderFeature.ModelToProfilingKeyMap, _modelToProfilingKeyMap);
    }

    protected override void OnSystemRemove()
    {
        base.OnSystemRemove();

        VisibilityGroup.Tags.Remove(ProfilingKeyRenderFeature.ModelToProfilingKeyMap);
    }
}

public class ProfilingKeyRenderData
{
    public RenderModel? RenderModel;
}