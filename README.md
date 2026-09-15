![](Resources/Mods/EnglishTranslate/Image/ui/uimain/Main_title.png?raw=true)

# Path of Wuxia ModLoader + English Patch + Mods

Join our [Discord community](https://discord.com/invite/PH4Z4Dn) for update notifications, direct issue reporting, and to chat with other fans of the game.

## Installation Guide

1. Download the latest release.
2. Extract the contents of `PoW_English.zip` into the game’s directory.

> If you installed Steam and Path of Wuxia in their default locations, your game folder should be:
> 
> ```plaintext
> C:\Program Files (x86)\Steam\steamapps\common\PathOfWuxia
> ```

![](InstallationGuide.gif)

Your folder should resemble the following structure:

![](InstallationExample.png?raw=true)

Once everything is in place, simply launch the game through Steam and enjoy!

## Technical Details

### Building the Mod

To build the mod yourself:

1. Clone the repository:

    ```bash
    git clone https://github.com/Magxm/PoW-Loader
    ```

2. Open the solution file and compile. A ready-to-use build will be located in `Output/Mod`.


### Translation and Update Automation

To get the latest translations or update the translation sheet to match the current game version, use the `PoW_Tool_SheetUtils` tool, which automates these tasks. 

> **Requirements**:
> - Access to the translation sheets
> - A configured Google Cloud account

For access and setup support, join our Discord server and reach out to us!

The tool starts an interactive menu when launched without arguments, and also accepts commands directly, which is what CI and quick checks should use:

```bash
SheetUtilities.exe stats                     # translation statistics over all sheets
SheetUtilities.exe stats --sheet Talk        # only one sheet, no throttling between sheets
SheetUtilities.exe update                    # update the sheets from the game files in ./Input
SheetUtilities.exe build mod                 # build the mod data (output|mod selects the target folder)
SheetUtilities.exe csv --sheet Talk,Book     # export translated lines to ./ExportOutput
SheetUtilities.exe formats Talk              # print the variable format of an input asset
SheetUtilities.exe sheets                    # list the known sheet names
SheetUtilities.exe help
```

`--sheet` takes a comma separated list of sheet names and applies to `update`, `build`, `stats` and `csv`. Commands exit with 1 on failure, so they can be scripted.

`stats` prints a line count per cell color bucket plus a catch all bucket. Lines in the catch all are listed with their hex color: either the color belongs into one of the buckets in `Program.cs` or the cell is mismarked in the sheet.

---

### Mod Loader and Plugins

This repository leverages **BepInEx** as the underlying mod loader and includes a `ModAPI` plugin offering core functionality for other mods and plugins. Key features include:

- **Dynamic Asset Overrides**: Support for multiple external asset folders, enabling several mods to register asset overrides.
- **Prefab Texture Modification**: Ability to modify textures within game prefabs.

Additionally, the `EnglishPatch` plugin is included to support English translations. Place translated asset files into the `Mods/EnglishTranslate` folder, and the plugin will handle loading these resources and resolve display issues.

---
### Important Configuration Note for BepInEx

When updating or replacing BepInEx, ensure you update the `BepInEx.cfg` file and replace the `[Preloader.Entrypoint]` section as follows:

```ini
[Preloader.Entrypoint]

## The local filename of the assembly to target.
# Setting type: String
# Default value: UnityEngine.CoreModule.dll
Assembly = UnityEngine.CoreModule.dll

## The name of the type in the entrypoint assembly to search for the entrypoint method.
# Setting type: String
# Default value: Application
Type = Object

## The name of the method in the specified entrypoint assembly and type to hook and load Chainloader from.
# Setting type: String
# Default value: .cctor
Method = .cctor
```

This adjustment ensures the entrypoint is set correctly, allowing all necessary assets to load before the game fully starts.

-------------------------------------------

The Dependencies folder contains components from the games, Unity assemblies and compiled BepInEx and Harmony binaries required for plugins. All rights to these files belong to their respective copyright holders.
I am aware that we should have contributor provide these files locally, however I am honestly too lazy to change this
