# EMPIRE AT WAR Launcher

A heavily modified and expanded launcher for **Star Wars: Empire at War** and
**Forces of Corruption**. It provides a modern way to manage mods, presets,
artwork, themes, launch options, and Steam Workshop content.

Project repository: <https://github.com/FuriosGuy/EAW-Launcher>

![EMPIRE AT WAR Launcher](img/launcher.png "EMPIRE AT WAR Launcher")

## Screenshots

Current launcher views:

![Edit preset](img/launcher-edit-preset.png "Edit preset")

![Settings](img/launcher-settings.png "Settings")

## Features

- Launch Empire at War and Forces of Corruption.
- Create and manage mod presets, including custom artwork and subthemes.
- Switch between playing with mods and without mods.
- Reorder selected mods and refresh Workshop content while the launcher is open.
- Use game, preset, or custom artwork with translucent acrylic-style panels.
- Save launcher size and sidebar layout between sessions.
- Configure themes, language settings, launch arguments, and updater behavior.

## Credits

This project is a substantially modified continuation of the original FoC Mod
Launcher. The original launcher and its updater foundation were created by
**Anakin Sklavenwalker** and the original contributors. This version is
maintained and expanded by **FuriosGuy** with new UI, preset, artwork, theme,
mod-management, and launcher features.

Third-party library and asset notices are documented in
[`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).

## Releases and updater

The launcher checks this repository first for
`releases/LauncherUpdateData.xml` and release files under the matching
`releases/Stable`, `releases/Beta`, or `releases/Test` directory. Run
`compileRelease.bat` to generate the metadata and release layout. Existing
installations temporarily fall back to the original update feed if the new
repository feed is not available yet.
