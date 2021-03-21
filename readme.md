#VRoid Studio External Edit Helper

Adding "Edit Externally Once" to texture layer context menu.

##Usage

This plugin works as a [BepInEx](https://github.com/BepInEx/BepInEx) plugin. Put this dll in (VRoid Studio root folder)/BepInEx/plugins and done.

##Build

`PostBuildEvent` defined in .csproj will automatically copy artifact dll to `C:\Program Files (x86)\Steam\steamapps\common\VRoid Studio\BepInEx\plugins` (Path to Steam version of VRoid Studio) and execute `VRoidStudio.exe`. Please disable it if you don't need.