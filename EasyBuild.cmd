 :: Create a single-file, self-contained executable for Windows x64
 fxc.exe /T ps_2_0 /E main /Fo CropEffect.ps CropEffect.fx
 dotnet publish -c Release -r win-x64 /p:AllowUnsafeBlocks=true --self-contained true /p:PublishSingleFile=true /p:IncludeAllContentForSelfExtract=true
