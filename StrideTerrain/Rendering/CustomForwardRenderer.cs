using Stride.Core.Annotations;
using Stride.Core.Diagnostics;
using Stride.Core.Storage;
using Stride.Core;
using Stride.Graphics;
using Stride.Rendering.Compositing;
using Stride.Rendering.Images;
using Stride.Rendering.Lights;
using Stride.Rendering.Shadows;
using Stride.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Stride.Core.Mathematics;

namespace StrideTerrain.Rendering;

[Display("Forward renderer (custom)")]
public partial class CustomForwardRenderer : SceneRendererBase, ISharedRenderer
{
    private static readonly ProfilingKey CollectCoreKey = new ProfilingKey("ForwardRenderer.CollectCore");
    private static readonly ProfilingKey DrawCoreKey = new ProfilingKey("ForwardRenderer.DrawCore");

    public const PixelFormat DepthBufferFormat = PixelFormat.D32_Float_S8X24_UInt;

    private IShadowMapRenderer? shadowMapRenderer;
    private Texture? depthStencilROCached;

    private readonly Logger logger = GlobalLogger.GetLogger(nameof(ForwardRenderer));

    private readonly List<Texture?> currentRenderTargets = [];
    private Texture? currentDepthStencil;

    protected Texture? viewOutputTarget;
    protected Texture? viewDepthStencil;

    public ClearRenderer Clear { get; set; } = new ClearRenderer();

    public required RenderStage OpaqueRenderStage { get; set; }

    public required RenderStage TransparentRenderStage { get; set; }

    [MemberCollection(NotNullItems = true)]
    public List<RenderStage> ShadowMapRenderStages { get; } = [];

    public required RenderStage GBufferRenderStage { get; set; }

    public IPostProcessingEffects? PostEffects { get; set; }

    [DefaultValue(true)]
    public bool BindDepthAsResourceDuringTransparentRendering { get; set; } = true;

    [DefaultValue(true)]
    public bool BindOpaqueAsResourceDuringTransparentRendering { get; set; } = true;

    protected override void InitializeCore()
    {
        base.InitializeCore();

        shadowMapRenderer = Context.RenderSystem.RenderFeatures.OfType<MeshRenderFeature>().FirstOrDefault()?.RenderFeatures.OfType<ForwardLightingRenderFeature>().FirstOrDefault()?.ShadowMapRenderer;
    }

    protected virtual void CollectStages(RenderContext context)
    {
        if (OpaqueRenderStage != null)
        {
            OpaqueRenderStage.OutputValidator.BeginCustomValidation(context.RenderOutput.DepthStencilFormat, context.RenderOutput.MultisampleCount);
            ValidateOpaqueStageOutput(OpaqueRenderStage.OutputValidator, context);
            OpaqueRenderStage.OutputValidator.EndCustomValidation();
        }

        if (TransparentRenderStage != null)
        {
            TransparentRenderStage.OutputValidator.Validate(ref context.RenderOutput);
        }

        if (GBufferRenderStage != null)
        {
            GBufferRenderStage.Output = new(PixelFormat.None, context.RenderOutput.DepthStencilFormat);
        }
    }

    protected virtual void ValidateOpaqueStageOutput(RenderOutputValidator renderOutputValidator, RenderContext renderContext)
    {
        renderOutputValidator.Add<ColorTargetSemantic>(renderContext.RenderOutput.RenderTargetFormat0);

        if (PostEffects != null)
        {
            if (PostEffects.RequiresNormalBuffer)
            {
                renderOutputValidator.Add<NormalTargetSemantic>(Platform.Type == PlatformType.Android || Platform.Type == PlatformType.iOS
                    ? PixelFormat.R16G16B16A16_Float
                    : PixelFormat.R10G10B10A2_UNorm);
            }

            if (PostEffects.RequiresSpecularRoughnessBuffer)
            {
                renderOutputValidator.Add<SpecularColorRoughnessTargetSemantic>(PixelFormat.R8G8B8A8_UNorm);
            }

            if (PostEffects.RequiresVelocityBuffer)
            {
                renderOutputValidator.Add<VelocityTargetSemantic>(PixelFormat.R16G16_Float);
            }
        }
    }

    protected virtual void CollectView(RenderContext context)
    {
        // Fill RenderStage formats and register render stages to main view
        if (OpaqueRenderStage != null)
        {
            context.RenderView.RenderStages.Add(OpaqueRenderStage);
        }

        if (TransparentRenderStage != null)
        {
            context.RenderView.RenderStages.Add(TransparentRenderStage);
        }

        if (GBufferRenderStage != null)
        {
            context.RenderView.RenderStages.Add(GBufferRenderStage);
        }
    }

    protected override unsafe void CollectCore(RenderContext context)
    {
        using var _ = Profiler.Begin(CollectCoreKey);

        var camera = context.GetCurrentCamera();

        if (context.RenderView == null)
            throw new NullReferenceException(nameof(context.RenderView) + " is null. Please make sure you have your camera correctly set.");

        // Setup pixel formats for RenderStage
        using (context.SaveRenderOutputAndRestore())
        {
            // Mark this view as requiring shadows
            shadowMapRenderer?.RenderViewsWithShadows.Add(context.RenderView);

            context.RenderOutput = new(PostEffects != null ? PixelFormat.R16G16B16A16_Float : context.RenderOutput.RenderTargetFormat0, DepthBufferFormat);

            CollectStages(context);

            //write params to view
            SceneCameraRenderer.UpdateCameraToRenderView(context, context.RenderView, camera);

            CollectView(context);

            PostEffects?.Collect(context);

            // Set depth format for shadow map render stages
            // TODO: This format should be acquired from the ShadowMapRenderer instead of being fixed here
            foreach (var shadowMapRenderStage in ShadowMapRenderStages)
            {
                if (shadowMapRenderStage != null)
                    shadowMapRenderStage.Output = new(PixelFormat.None, PixelFormat.D32_Float);
            }
        }

        PostEffects?.Collect(context);
    }

    protected virtual void DrawView(RenderContext context, RenderDrawContext drawContext, int eyeIndex, int eyeCount)
    {
        var renderSystem = context.RenderSystem;

        PrepareVRConstantBuffer(context, eyeIndex, eyeCount);

        // Z Prepass
        if (GBufferRenderStage != null)
        {
            using (drawContext.QueryManager.BeginProfile(Color.Green, CompositingProfilingKeys.GBuffer))
            using (drawContext.PushRenderTargetsAndRestore())
            {
                drawContext.CommandList.Clear(drawContext.CommandList.DepthStencilBuffer, DepthStencilClearOptions.DepthBuffer, 0);
                drawContext.CommandList.SetRenderTarget(drawContext.CommandList.DepthStencilBuffer, null);

                // Draw [main view | z-prepass stage]
                renderSystem.Draw(drawContext, context.RenderView, GBufferRenderStage);
            }
        }

        using (drawContext.PushRenderTargetsAndRestore())
        {
            // Draw [main view | main stage]
            if (OpaqueRenderStage != null)
            {
                using (drawContext.QueryManager.BeginProfile(Color.Green, CompositingProfilingKeys.Opaque))
                {
                    renderSystem.Draw(drawContext, context.RenderView, OpaqueRenderStage);
                }
            }

            Texture? depthStencilSRV = null;

            // Draw [main view | transparent stage]
            if (TransparentRenderStage != null)
            {
                // Some transparent shaders will require the depth as a shader resource - resolve it only once and set it here
                using (drawContext.QueryManager.BeginProfile(Color.Green, CompositingProfilingKeys.Transparent))
                using (drawContext.PushRenderTargetsAndRestore())
                {
                    if (depthStencilSRV == null)
                        depthStencilSRV = ResolveDepthAsSRV(drawContext);

                    var renderTargetSRV = ResolveRenderTargetAsSRV(drawContext);

                    renderSystem.Draw(drawContext, context.RenderView, TransparentRenderStage);

                    Context.Allocator.ReleaseReference(renderTargetSRV);
                }
            }

            var colorTargetIndex = OpaqueRenderStage?.OutputValidator.Find(typeof(ColorTargetSemantic)) ?? -1;
            if (colorTargetIndex == -1)
                return;

            // Resolve MSAA targets
            var renderTargets = currentRenderTargets;
            var depthStencil = currentDepthStencil;

            // Run post effects
            // Note: OpaqueRenderStage can't be null otherwise colorTargetIndex would be -1
            PostEffects?.Draw(drawContext, OpaqueRenderStage!.OutputValidator, CollectionsMarshal.AsSpan(renderTargets), depthStencil, viewOutputTarget);

            // Free the depth texture since we won't need it anymore
            if (depthStencilSRV != null)
            {
                drawContext.Resolver.ReleaseDepthStenctilAsShaderResource(depthStencilSRV);
            }
        }
    }

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        using var _ = Profiler.Begin(DrawCoreKey);

        var viewport = drawContext.CommandList.Viewport;

        using (drawContext.PushRenderTargetsAndRestore())
        {
            // Render Shadow maps
            shadowMapRenderer?.Draw(drawContext);

            PrepareRenderTargets(drawContext, new Size2((int)viewport.Width, (int)viewport.Height));

            using (drawContext.PushRenderTargetsAndRestore())
            {
                drawContext.CommandList.SetRenderTargets(currentDepthStencil, CollectionsMarshal.AsSpan(currentRenderTargets)[..currentRenderTargets.Count]);

                //Clear?.Draw(drawContext);

                for (var index = 0; index < drawContext.CommandList.RenderTargetCount; index++)
                {
                    var renderTarget = drawContext.CommandList.RenderTargets[index];
                    drawContext.CommandList.Clear(renderTarget, Color4.Black);
                }

                DrawView(context, drawContext, 0, 1);
            }
        }

        // Clear intermediate results
        currentRenderTargets.Clear();
        currentDepthStencil = null;
    }

    private void CopyOrScaleTexture(RenderDrawContext drawContext, Texture input, Texture output)
    {
        drawContext.CommandList.Copy(input, output);
    }

    private Texture? ResolveDepthAsSRV(RenderDrawContext context)
    {
        if (!BindDepthAsResourceDuringTransparentRendering)
            return null;

        var depthStencil = context.CommandList.DepthStencilBuffer;
        var depthStencilSRV = context.Resolver.ResolveDepthStencil(context.CommandList.DepthStencilBuffer);

        var renderView = context.RenderContext.RenderView;

        foreach (var renderFeature in context.RenderContext.RenderSystem.RenderFeatures)
        {
            if (renderFeature is RootRenderFeature rootRenderFeature)
            {
                rootRenderFeature.BindPerViewShaderResource("Depth", renderView, depthStencilSRV);
            }
        }

        context.CommandList.SetRenderTargets(null, context.CommandList.RenderTargets);

        var depthStencilROCached = context.Resolver.GetDepthStencilAsRenderTarget(depthStencil, this.depthStencilROCached);
        if (depthStencilROCached != this.depthStencilROCached)
        {
            // Dispose cached view
            this.depthStencilROCached?.Dispose();
            this.depthStencilROCached = depthStencilROCached;
        }
        context.CommandList.SetRenderTargets(depthStencilROCached, context.CommandList.RenderTargets);

        return depthStencilSRV;
    }

    private Texture? ResolveRenderTargetAsSRV(RenderDrawContext drawContext)
    {
        if (!BindOpaqueAsResourceDuringTransparentRendering)
            return null;

        // Create temporary texture and blit active render target to it
        var renderTarget = drawContext.CommandList.RenderTargets[0];
        var renderTargetTexture = Context.Allocator.GetTemporaryTexture2D(renderTarget.Description);

        drawContext.CommandList.Copy(renderTarget, renderTargetTexture);

        // Bind texture as srv in PerView.Opaque
        var renderView = drawContext.RenderContext.RenderView;
        foreach (var renderFeature in drawContext.RenderContext.RenderSystem.RenderFeatures)
        {
            if (renderFeature is RootRenderFeature rootRenderFeature)
            {
                rootRenderFeature.BindPerViewShaderResource("Opaque", renderView, renderTargetTexture);
            }
        }

        return renderTargetTexture;
    }

    private void PrepareRenderTargets(RenderDrawContext drawContext, Texture outputRenderTarget, Texture outputDepthStencil)
    {
        if (OpaqueRenderStage == null)
            return;

        var renderTargets = OpaqueRenderStage.OutputValidator.RenderTargets;

        if (currentRenderTargets.Count < renderTargets.Count)
        {
            currentRenderTargets.EnsureCapacity(renderTargets.Count);
            while (currentRenderTargets.Count != renderTargets.Count)
                currentRenderTargets.Add(null);
        }
        else if (currentRenderTargets.Count > renderTargets.Count)
        {
            currentRenderTargets.RemoveRange(renderTargets.Count, currentRenderTargets.Count - renderTargets.Count);
        }

        for (int index = 0; index < renderTargets.Count; index++)
        {
            if (renderTargets[index].Semantic is ColorTargetSemantic && PostEffects == null)
            {
                currentRenderTargets[index] = outputRenderTarget;
            }
            else
            {
                var description = renderTargets[index];
                var textureDescription = TextureDescription.New2D(outputRenderTarget.Width, outputRenderTarget.Height, 1, description.Format, TextureFlags.RenderTarget | TextureFlags.ShaderResource, 1, GraphicsResourceUsage.Default);
                currentRenderTargets[index] = PushScopedResource(drawContext.GraphicsContext.Allocator.GetTemporaryTexture2D(textureDescription));
            }

            drawContext.CommandList.ResourceBarrierTransition(currentRenderTargets[index], GraphicsResourceState.RenderTarget);
        }

        // Prepare depth buffer
        currentDepthStencil = outputDepthStencil;
        drawContext.CommandList.ResourceBarrierTransition(currentDepthStencil, GraphicsResourceState.DepthWrite);
    }

    /// <summary>
    /// Prepares targets per frame, caching and handling MSAA etc.
    /// </summary>
    /// <param name="drawContext">The current draw context</param>
    /// <param name="renderTargetsSize">The render target size</param>
    protected virtual void PrepareRenderTargets(RenderDrawContext drawContext, Size2 renderTargetsSize)
    {
        viewOutputTarget = drawContext.CommandList.RenderTarget;
        if (drawContext.CommandList.RenderTargetCount == 0)
            viewOutputTarget = null;
        viewDepthStencil = drawContext.CommandList.DepthStencilBuffer;

        // Create output if needed
        if (viewOutputTarget == null || viewOutputTarget.MultisampleCount != MultisampleCount.None)
        {
            viewOutputTarget = PushScopedResource(drawContext.GraphicsContext.Allocator.GetTemporaryTexture2D(
                TextureDescription.New2D(renderTargetsSize.Width, renderTargetsSize.Height, 1, PixelFormat.R8G8B8A8_UNorm_SRgb,
                    TextureFlags.ShaderResource | TextureFlags.RenderTarget)));
        }

        // Create depth if needed
        if (viewDepthStencil == null || viewDepthStencil.MultisampleCount != MultisampleCount.None)
        {
            viewDepthStencil = PushScopedResource(drawContext.GraphicsContext.Allocator.GetTemporaryTexture2D(
                TextureDescription.New2D(renderTargetsSize.Width, renderTargetsSize.Height, 1, DepthBufferFormat,
                    TextureFlags.ShaderResource | TextureFlags.DepthStencil)));
        }

        PrepareRenderTargets(drawContext, viewOutputTarget, viewDepthStencil);
    }

    protected override void Destroy()
    {
        PostEffects?.Dispose();
        depthStencilROCached?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerViewVR
    {
        public int EyeIndex;
        public int EyeCount;
    }

    private unsafe void PrepareVRConstantBuffer(RenderContext context, int eyeIndex, int eyeCount)
    {
        foreach (var renderFeature in context.RenderSystem.RenderFeatures)
        {
            if (!(renderFeature is RootEffectRenderFeature))
                continue;

            var renderView = context.RenderView;
            var logicalKey = ((RootEffectRenderFeature)renderFeature).CreateViewLogicalGroup("GlobalVR");
            var viewFeature = renderView.Features[renderFeature.Index];

            foreach (var viewLayout in viewFeature.Layouts)
            {
                var resourceGroup = viewLayout.Entries[renderView.Index].Resources;

                var logicalGroup = viewLayout.GetLogicalGroup(logicalKey);
                if (logicalGroup.Hash == ObjectId.Empty)
                    continue;

                var mappedCB = (PerViewVR*)(resourceGroup.ConstantBuffer.Data + logicalGroup.ConstantBufferOffset);
                mappedCB->EyeIndex = eyeIndex;
                mappedCB->EyeCount = eyeCount;
            }
        }
    }
}
