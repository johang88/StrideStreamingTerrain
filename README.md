# Stride streaming terrain
NOTE: Don't use, it's not production ready, does not work in editor, performance is not great, no real material support, hardcoded values in some places etc ... 


## How to use
If you insist ... 

* It wont compile without changes to the stride source code, available here https://github.com/johang88/stride if it's been kept up to date.
* Get a big heightmap (something like 8k+ it's got to be really big or it would make no sense to stream it now would it). It currently must be power of 2 (or power of 2 + 1), square and be 16bit single channel png (other formats might work but not tested).
* Fix stride versions in 
* Compile `StrideTerrain.Importer.sln`
* From root folder of the project run something like this `"StrideTerrain.Importer\bin\debug\net8.0\StrideTerrain.Importer.exe --input "<PathToHeightMap>" --output "StrideTerrain.Sample\Resources" --name "<MapName>" --chunk-size 128 --max-height <MaxHeight>`
* You will now have some files in the resoruces folder `MapName`, `MapName_StreamingData`
* Open the sample project (or add referneces as needed to your own project)
* Import `MapName`, `MapName_StreamingData` as raw assets. Compression **must** be disabled or you will get corrupt data
* Add `TerrainComponent` to an entity and set `TerrainData` and `TerrainStreamingData` properly. You will also need to set a material.
* Material should displacement set to the included `Terrain Displacement` feature.
* Setup the compositor if you want to get the reverse z rendering working `LightDirectionalShadowMapRendererReverseZ`, `ReverseZPipelineProcessor`, `ReverseZRenderer` or just use the compositor in the sample folder. Note: this will probably also break with a bunch of stuff : ) 