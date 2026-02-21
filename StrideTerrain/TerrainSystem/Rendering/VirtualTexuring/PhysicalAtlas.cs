using Stride.Core.Mathematics;
using Stride.Graphics;
using System;
using System.Collections.Generic;

namespace StrideTerrain.TerrainSystem.Rendering.VirtualTexuring;

public class PhysicalAtlas : IDisposable
{
    public const int TilesPerAxis = 30;
    public const int TotalSlots = TilesPerAxis * TilesPerAxis;

    // Atlas textures
    public Texture DiffuseAtlas { get; private set; }
    public Texture RoughnessAtlas { get; private set; }
    public Texture NormalAtlas { get; private set; }

    // Slot management
    private readonly Slot[] _slots = new Slot[TotalSlots];
    private readonly LinkedList<int> _lruList = [];
    private readonly Dictionary<long, int> _keyToSlot = [];
    private readonly Queue<int> _freeSlots = [];

    // Indirection texture (CPU-side mirror + GPU texture)
    private readonly byte[] _indirectionData;  // 512*512*9 layers * 4 bytes
    public Texture IndirectionTexture { get; private set; }
    private bool _indirectionDirty;

    private Texture _stagingTexture;

    public PhysicalAtlas(GraphicsDevice device)
    {
        DiffuseAtlas = Texture.New2D(device, 8192, 8192,
            PixelFormat.R8G8B8A8_UNorm_SRgb,
            TextureFlags.ShaderResource | TextureFlags.RenderTarget);

        NormalAtlas = Texture.New2D(device, 8192, 8192,
            PixelFormat.R16G16_Float,
            TextureFlags.ShaderResource | TextureFlags.RenderTarget);

        RoughnessAtlas = Texture.New2D(device, 8192, 8192,
            PixelFormat.R8_UNorm,
            TextureFlags.ShaderResource | TextureFlags.RenderTarget);

        // Indirection texture: 512x512, 9 array layers, RGBA8
        IndirectionTexture = Texture.New2D(device, 512, 512, PixelFormat.R8G8B8A8_UNorm, TextureFlags.ShaderResource, arraySize: VTConstants.MipCount, usage: GraphicsResourceUsage.Default);
        _stagingTexture = Texture.New2D(device, 512, 512, PixelFormat.R8G8B8A8_UNorm, TextureFlags.ShaderResource, usage: GraphicsResourceUsage.Dynamic);

        _indirectionData = new byte[512 * 512 * VTConstants.MipCount * 4];

        // Useful for debugging
        for (var i = 0; i < _indirectionData.Length; i += 4)
        {
            var mip = i / (512 * 512 * 4);
            _indirectionData[i] = (byte)(25 * mip);
            _indirectionData[i + 1] = 255;
            _indirectionData[i + 2] = 255;
            _indirectionData[i + 3] = 0;
        }

        // Initialize all slots as free
        for (int i = 0; i < TotalSlots; i++)
        {
            _slots[i] = new Slot { Index = i, State = SlotState.Free };
            _freeSlots.Enqueue(i);
        }
    }

    /// <summary>
    /// Allocate a physical slot for a tile. Returns slot index.
    /// Evicts LRU tile if no free slots available.
    /// </summary>
    public int AllocateSlot(long tileKey)
    {
        // Check if tile is already allocated
        if (_keyToSlot.TryGetValue(tileKey, out var slotIdx))
        {
            // Touch LRU
            TouchSlot(slotIdx);
            return slotIdx;
        }

        // Try free list first
        if (_freeSlots.Count > 0)
        {
            slotIdx = _freeSlots.Dequeue();
        }
        else
        {
            // Evict LRU (head of list = least recently used)
            var lruNode = _lruList.First;
            slotIdx = lruNode!.Value;
            _lruList.RemoveFirst();

            // Remove old mapping
            var oldSlot = _slots[slotIdx];
            if (oldSlot.TileKey != 0)
            {
                _keyToSlot.Remove(oldSlot.TileKey);
                InvalidateIndirection(oldSlot.TileKey);
            }
        }

        // Assign slot
        _slots[slotIdx].TileKey = tileKey;
        _slots[slotIdx].State = SlotState.Allocated;
        _slots[slotIdx].LRUNode = _lruList.AddLast(slotIdx);
        _keyToSlot[tileKey] = slotIdx;

        return slotIdx;
    }

    /// <summary>
    /// Get the atlas pixel region for a slot (includes border).
    /// </summary>
    public static Rectangle GetSlotRegion(int slotIdx)
    {
        int tx = slotIdx % TilesPerAxis;
        int ty = slotIdx / TilesPerAxis;
        return new Rectangle(
            tx * VTConstants.TileSizePadded,
            ty * VTConstants.TileSizePadded,
            VTConstants.TileSizePadded,
            VTConstants.TileSizePadded
        );
    }

    /// <summary>
    /// Mark a slot as rendered and update the indirection texture.
    /// </summary>
    public void MarkResident(int slotIdx, int tileX, int tileY, int mipLevel)
    {
        _slots[slotIdx].State = SlotState.Resident;

        int physX = slotIdx % TilesPerAxis;
        int physY = slotIdx / TilesPerAxis;

        // Update indirection texture data
        int indirIdx = (mipLevel * 512 * 512 + tileY * 512 + tileX) * 4;
        if (indirIdx >= 0 && indirIdx + 3 < _indirectionData.Length)
        {
            _indirectionData[indirIdx + 0] = (byte)physX;      // physical tile X
            _indirectionData[indirIdx + 1] = (byte)physY;      // physical tile Y
            _indirectionData[indirIdx + 2] = 0;                // mip delta = 0 (exact match)
            _indirectionData[indirIdx + 3] = 1;                // flags: valid
            _indirectionDirty = true;
        }
    }

    /// <summary>
    /// Upload dirty indirection data to GPU.
    /// </summary>
    public void FlushIndirection(CommandList commandList)
    {
        if (!_indirectionDirty) return;

        for (var i = 0; i < VTConstants.MipCount; i++)
        {
            int size = 512 * 512 * 4;
            _stagingTexture.SetData(commandList, _indirectionData.AsSpan(size * i, size));
            commandList.CopyRegion(_stagingTexture, 0, null, IndirectionTexture, i);
            commandList.Flush();
        }

        _indirectionDirty = false;
    }

    private void TouchSlot(int slotIdx)
    {
        var node = _slots[slotIdx].LRUNode;
        if (node != null)
        {
            _lruList.Remove(node);
            _slots[slotIdx].LRUNode = _lruList.AddLast(slotIdx);
        }
    }

    private void InvalidateIndirection(long tileKey)
    {
        // Decode tileKey back to x,y,mip and clear the indirection entry
        int mip = (int)((tileKey >> 48) & 0xFF);
        int y = (int)((tileKey >> 24) & 0xFFFFFF);
        int x = (int)(tileKey & 0xFFFFFF);

        int indirIdx = (mip * 512 * 512 + y * 512 + x) * 4;
        if (indirIdx >= 0 && indirIdx + 3 < _indirectionData.Length)
        {
            _indirectionData[indirIdx + 3] = 0; // clear valid flag
            _indirectionDirty = true;
        }
    }

    public void Dispose()
    {
        DiffuseAtlas.Dispose();
        NormalAtlas.Dispose();
        RoughnessAtlas.Dispose();
        IndirectionTexture.Dispose();
        _stagingTexture.Dispose();
    }

    struct Slot
    {
        public int Index;
        public long TileKey;
        public SlotState State;
        public LinkedListNode<int> LRUNode;
    }

    enum SlotState
    {
        Free,
        Allocated,
        Resident
    }
}


