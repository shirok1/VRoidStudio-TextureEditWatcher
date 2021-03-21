# VRoid Studio External Edit Helper

Adding "Edit Externally Once" to texture layer context menu.

![image](https://user-images.githubusercontent.com/12044683/111904245-e35e7f00-8a80-11eb-9353-74d7258cbcc1.png)

## Usage

This plugin works as a [BepInEx](https://github.com/BepInEx/BepInEx) plugin. Put this dll in `(VRoid Studio root folder)/BepInEx/plugins` and done.

Default image editor is MS Paint. Change it in `(VRoid Studio root folder)/BepInEx/config/Shiroki.VRoidStudioPlugin.TextureEditWatcher.cfg`.

## Build

`PostBuildEvent` defined in .csproj will automatically copy artifact dll to `C:\Program Files (x86)\Steam\steamapps\common\VRoid Studio\BepInEx\plugins` (Path to Steam version of VRoid Studio) and execute `VRoidStudio.exe`. Please disable it if you don't need.
