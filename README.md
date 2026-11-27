# theyaremany

A RimWorld 1.7 faction mod. A clone guild: one lodge in orbit, one grudge, and as many clones as it takes.

Its master is an old enemy of your people. By accident he found a dormant cloning facility.

## Requirements

- RimWorld **1.7** with the **Odyssey** and **Biotech** DLC
- [Harmony](https://github.com/pardeike/HarmonyRimWorld/releases/latest) (must load before this mod)

## Installation (Windows)

1. Grab the latest release zip (`theyaremany-<version>.zip`) from the
   [Releases page](../../releases/latest).
2. Extract it into the game's `Mods` folder, e.g. for a Steam install:

   ```
   C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\
   ```

   The zip already contains a single `theyaremany` folder, so after extraction
   you should have `Mods\theyaremany\About\About.xml` (use *Extract All…* and
   move the `theyaremany` folder in if your extractor adds its own wrapper).
3. In RimWorld's mod list, enable **Harmony** first, then **theyaremany**.

Local mods can also go in `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Mods` if you prefer not to touch the game folder.

## Building from source

Any machine with the .NET SDK (see `Source/theyaremany/global.json`) - the game itself is not needed to compile:

```
git clone https://github.com/<your-account>/theyaremany
cd theyaremany\Source\theyaremany
dotnet build theyaremany.slnx --configuration Release
```

The built assembly lands in `Assemblies/`. CI builds and publishes a release zip on every `X.Y.Z` tag.

## Notes

- The guild is a permanent-enemy faction on the Orbit layer with a single settlement (its lodge). It never trades and never negotiates.
- The master's persona (name, backstories, skills inherited by every clone) is configurable in the mod settings ("theyaremany").
