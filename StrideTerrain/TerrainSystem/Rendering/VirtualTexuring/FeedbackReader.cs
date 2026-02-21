using Stride.Graphics;
using System;
using System.Collections.Generic;

namespace StrideTerrain.TerrainSystem.Rendering.VirtualTexuring;

/// <summary>
/// Manages GPU→CPU feedback readback with multi-frame latency hiding.
/// Uses triple-buffered staging to avoid GPU stalls.
/// </summary>
public class FeedbackReader : IDisposable
{
    private readonly GraphicsDevice _device;

    // Triple-buffered staging textures for async readback
    private readonly Texture[] _stagingBuffers = new Texture[3];
    private int _currentFrame;
    private int _readbackFrame; // always 2 frames behind _currentFrame
    
    private Texture _feedbackRenderTarget;

    private readonly int _feedbackWidth;
    private readonly int _feedbackHeight;

    public FeedbackReader(GraphicsDevice device, int screenWidth, int screenHeight, Texture feedbackRenderTarget)
    {
        _device = device;
        _feedbackWidth = screenWidth / 4;
        _feedbackHeight = screenHeight / 4;

        _feedbackRenderTarget = feedbackRenderTarget;

        // CPU-readable staging buffers (triple-buffered)
        for (int i = 0; i < 3; i++)
        {
            _stagingBuffers[i] = Texture.New2D(device,
                _feedbackWidth, _feedbackHeight,
                PixelFormat.R8G8B8A8_UNorm,
                TextureFlags.None,
                usage: GraphicsResourceUsage.Staging);
        }

        _currentFrame = 0;
        _readbackFrame = -2; // no readback available yet
    }

    /// <summary>
    /// Called after feedback pass render. Initiates async copy to staging.
    /// </summary>
    public void SubmitReadback(CommandList commandList)
    {
        int stagingIdx = _currentFrame % 3;

        // Async copy GPU feedback texture → staging buffer
        commandList.Copy(_feedbackRenderTarget, _stagingBuffers[stagingIdx]);

        _currentFrame++;
    }

    /// <summary>
    /// Reads back feedback data from 2 frames ago (latency hiding).
    /// Returns parsed tile requests, or null if not ready.
    /// </summary>
    public List<TileRequest> ReadFeedback(CommandList commandList)
    {
        _readbackFrame = _currentFrame - 3; // read 3 frames back (safe latency)

        if (_readbackFrame < 0)
            return []; // not enough frames elapsed yet

        int stagingIdx = _readbackFrame % 3;
        var staging = _stagingBuffers[stagingIdx];

        // Map staging buffer for CPU read
        var mappedResource = commandList.MapSubresource(staging, 0, MapMode.Read);

        var requests = new List<TileRequest>(1024);
        var seen = new HashSet<long>(); // dedup key

        var dataBox = mappedResource.DataBox;

        unsafe
        {
            byte* ptr = (byte*)dataBox.DataPointer;
            int rowPitch = dataBox.RowPitch;

            // Downsample further: only read every 4th pixel for speed
            for (int y = 0; y < _feedbackHeight; y += 2)
            {
                byte* row = ptr + y * rowPitch;
                for (int x = 0; x < _feedbackWidth; x += 2)
                {
                    int offset = x * 4; // RGBA8 = 4 bytes/pixel
                    byte r = row[offset + 0];
                    byte g = row[offset + 1];
                    byte b = row[offset + 2];
                    byte a = row[offset + 3]; // mip level

                    // Decode tile index
                    int tileX = r | ((g & 0x0F) << 8);
                    int tileY = ((g >> 4) & 0x0F) | (b << 4);
                    int mipLevel = a;

                    if (mipLevel >= VTConstants.MipCount)
                        continue; // invalid

                    // Deduplication key
                    long key = ((long)mipLevel << 32) | ((long)tileY << 16) | (long)tileX;
                    if (seen.Add(key))
                    {
                        requests.Add(new TileRequest
                        {
                            TileX = tileX,
                            TileY = tileY,
                            MipLevel = mipLevel
                        });
                    }
                }
            }
        }

        commandList.UnmapSubresource(mappedResource);

        return requests;
    }

    public void Dispose()
    {
        _feedbackRenderTarget?.Dispose();
        foreach (var buffer in _stagingBuffers)
        {
            buffer?.Dispose();
        }
    }
}
