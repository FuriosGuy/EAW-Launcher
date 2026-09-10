# EMPIRE AT WAR Launcher release feed

This is the updater feed consumed by installed copies of EMPIRE AT WAR
Launcher. The launcher reads the feed from the `master` branch:

<https://raw.githubusercontent.com/FuriosGuy/EAW-Launcher/master/releases/LauncherUpdateData.xml>

`compileRelease.bat` is the local/manual release helper. It builds the full
solution, finds MSBuild automatically, and generates the feed from the Release
host output. Tagged pushes (`v2.0.0`, `v2.0.1-beta`, or `v2.0.1-test`) use
`.github/workflows/release.yml` to do the same work in GitHub Actions, publish
the Windows ZIP as a GitHub Release asset, and commit the feed back to
`master`.

Keep generated metadata at the directory root and the files inside `Stable`,
`Beta`, or `Test` according to their release channel. The GitHub Release ZIP
is for initial/manual installation; the raw feed is what in-app updating uses.

The expected layout is:

```text
releases/
  LauncherUpdateData.xml
  Stable/
    EMPIRE AT WAR Launcher.exe
    EMPIRE AT WAR Launcher Updater.exe
    FocLauncher.dll
    FocLauncher.Theming.dll
    FocLauncher.Threading.dll
  Beta/
  Test/
```
