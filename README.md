# Stride streaming terrain


![Streaming Terrain](screenshot.jpg?raw=true "Streaming Terrain Screenshot")

Would not recommend for production use as there are some hardcoded paths to get it to work in the editor. It's works fine though and is stable but it does require some manual steps. The project mostly serves as a guide on how you could make your own custom implementation, mainly the control map / material will most likely have to be customized for the intended use case. The material can easily be modified with some shader knowledege. 

The project also contains a custom fork of my TR.Stride.Atmosphere/Ocean projects (https://github.com/johang88/TR.Stride/) with some extra fun stuff volumetric fog rendering with terrain shadow sampling. The atmosphere has been mostly remade and might event perform a bit better than the original, it does lack some of the features that were not needed for this project.

There is also some setup for enabling reverse Z rendering in stride (custom scene renderer), my custom fork (https://github.com/johang88/stride) has some additional changes to get post effect like SSAO working as well as in editor scene picking.

## Project overview
* StrideTerrain - Main Project
* - Rendering - Dynamic Cubemap renderer and reverse z rendering
* - TerrainSystem - All terrain related logic
* - Weather - Atmosphere and volumetric fog rendering, based on https://github.com/sebh/UnrealEngineSkyAtmosphere

## How to use

* NOTE: The sample program is incomplete as I cannot distribute the assets.
* It wont compile without changes to the stride source code, available here https://github.com/johang88/stride. The changes mostly expose some internal stride logic for light / shadow handling that is has custom implementations.
* Get a big heightmap (8k+ for the streaming to make snse). It currently must be power of 2 (or power of 2 + 1), square and be 16bit single channel png (other formats might work but has been not tested).
* Fix stride versions in 
* Compile `StrideTerrain.Importer.sln`
* From root folder of the project run something like this `"StrideTerrain.Importer\bin\debug\net8.0\StrideTerrain.Importer.exe --input "<PathToHeightMap>" --output "StrideTerrain.Sample\Resources" --name "<MapName>" --chunk-size 128 --max-height <MaxHeight>`
* You will now have some files in the resoruces folder `MapName`, `MapName_StreamingData`
* Open the sample project (or add referneces as needed to your own project)
* Import `MapName`, `MapName_StreamingData` as raw assets. Compression **must** be disabled or you will get corrupt data
* Add `TerrainComponent` to an entity and set `TerrainData` and `TerrainStreamingData` properly. You will also need to set a material.
* Material should displacement set to the included `Terrain Displacement` feature.
* Setup the compositor if you want to get the reverse z rendering working `LightDirectionalShadowMapRendererReverseZ`, `ReverseZPipelineProcessor`, `ReverseZRenderer` or just use the compositor in the sample folder. Note: this will probably also break with a bunch of stuff : ) 


## How does it work?
A heightmap is split into a number of chunks at each lod level. All chunks have the same size but each increasing lod level covers twice the distance on the heightmap. The `StrideTerrain.Importer` tool is used to generate this data in the form of a file containing information about the terrain and a file containing all the chunks, in addition it also generates normal maps for each chunk, it can also handle a control (splat map).

The per chunk data is currently stored in the following sizes and formats.

| Type        | Format    | Size (px) |
|-------------|-----------|-----------|
| Heightmap   | R16_Unorm | size + 1  |
| Normal map  | BC5_Unorm | size + 4  |
| Control map | R16_Unorm | size + 1  |

All maps have 1 px border with the normal map having an additional 3pixel border to enable block compression.

At runtime an atlas texture is allocated for each type of data and chunks are streamed in as requested by the terrain processor, the least detailed lod level will always be resident. The chunks are processed as a quad tree a node will considered for splitting into lower lod levels if it's within some configurable distance of the camera, it will then only be split if all four child chunks are resident. If they are not resident then they will be requested by the streaming system and uploaded to the atlas textures.

In addition there is a separate processor for the physics system, currently using the Stride Bullet Physics implenentation. It will stream in heightmap data for 7x7 chunks around the camera at the highest detail and create colliders for them.

The class `GpuTextureManager` is responsible for uploading data to the atlas and managing the pending stream requests. The `StreamingManager` is used by both the graphics and physics systems to stream in the data on a background thread.

### Mesh and material
The mesh is generated on the GPU, it does not use any vertex or index buffer and the entire terrain is drawn in a single instanced draw call this is managed in the custom render feature `TerrainRenderFeature`. A custom displacement shader is used to put genereat the triangles/quads and it also merges vertices if a chunk borders another chunk with lower detail in order to prevent seams between the chunks.

The actual terrain material consist of a custom diffuse material feature `MaterialTerrainDiffuseFeature` that adds a stream initiailzier `TerrainMaterialStreamInitializer` which loads data for all terrain material streams `TerrainDiffuse, TerrainNormal, TerrainRoughness`. These streams contain all the shading information for the current fragment and are in turn read by a couple of Compute Color shaders that are setup in the Stride material asset.

The terrain data is made available to any Stride material and can be queryed by using the `TerrainQuery` shader file.