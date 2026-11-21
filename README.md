# SysVolumeMixer

<a href="https://macro-deck.app/store/com.cjhackeryt.sysvolumemixer">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://assets.macro-deck.app/branding/Open%20in%20the%20Macro%20Deck%20Store%20Badge%20Dark.png">
    <img alt="Open in the Macro Deck Store" src="https://assets.macro-deck.app/branding/Open%20in%20the%20Macro%20Deck%20Store%20Badge%20Light.png" height="56">
  </picture>
</a>

SysVolumeMixer is an out-of-process Macro Deck 3 plugin for changing the per-application volume of
Windows audio sessions. Its virtual profile shows six vertical sliders per page. Each slider spans
one column and three rows, is labeled with its application's name, and displays its current volume
percentage.

Applications are discovered from active sessions on all active Windows render devices. This includes
applications routed to a non-default device, such as Discord on a separate communications endpoint.
Multiple sessions belonging to the same executable are displayed as one application and adjusted
together. Additional applications appear in numbered mixer folders. An empty column is inert.
The plugin does not change Windows master volume or persist audio settings itself.

The profile updates when the active session catalogue or a displayed session's volume changes. Drag
a slider to set that application's actual Windows audio volume. On startup it refreshes the
virtual-profile catalogue after audio discovery, so an initially empty profile is not kept as a
cached description. Variable IDs are persisted Macro Deck identifiers; do not rename them without
a migration plan. Applications appear when they have active audio sessions on any active Windows
render device.

## Requirements

- Windows x64
- .NET SDK 10
- Macro Deck 3 with the .NET 10 runtime
- `macrodeck-plugin` CLI for conformance tests and release packaging

## Install

The Store listing is not live yet. Once published, install **SysVolumeMixer** from the Macro Deck
Store and open its virtual profile. Applications appear when they have active audio sessions on a
Windows render device.

## Build and test

From the repository root:

```powershell
dotnet build
dotnet test
macrodeck-plugin test --project src\VolumeMixer --report markdown --output conformance.md
```

Build and unit tests do not require a running Macro Deck installation. The conformance suite starts
a stub host and checks the plugin's protocol behavior.

## Run against Macro Deck

Open the solution in an IDE, select the **Macro Deck - Real Host** launch profile for
`src\VolumeMixer\VolumeMixer.csproj`, and start Macro Deck on the same machine. The plugin uses
self-registration with the host at `http://127.0.0.1:8193`. The launch profile contains no enrollment
token; if the host requests one, provide it through .NET User Secrets rather than a launch setting or
source file.

The integration and virtual profile appear after the plugin connects. Audio detection includes active
sessions on all active render endpoints. If an application has no active audio session, it is not
listed yet.

If applications are still missing while audio is playing, check the Macro Deck log for the
`Windows audio session discovery found ... active applications` message. The plugin ignores
non-render devices and inactive sessions.

## Controls and variables

The **SysVolumeMixer** virtual profile has a `Mixer` folder and one folder per additional six
applications. Each folder has a three-row, six-column grid with six vertical sliders. Each slider
occupies one column by three rows. A slider's label is the application name; its displayed value is
the current percentage. Dragging a slider changes the matching application's volume and selects that
page for the legacy slot variables and manual actions. Empty slots are read-only.

The integration provides two manual actions, **Increase application volume** and **Decrease
application volume**. Choose an application with an active audio session from the action's
application list. If the list is empty, start audio in the application and refresh the list. Actions
saved with the older mixer-column parameter continue to target that column on the selected mixer
page.

The page-specific slider bindings are writable and scoped to their folder so each slider always
controls the application shown on that page:

- `slider-page-{page}-slot-{slot}-volume`: current volume for that page and slot, using 1-based
  numbering; writes set the application's volume from 0 to 100.

Existing read-only variable IDs remain available:

- `slot-1-app` through `slot-6-app`: application name and current volume, or `Empty`.
- `slot-1-volume` through `slot-6-volume`: volume percentage, or zero for an empty slot.
- `slot-1-muted` through `slot-6-muted`: mute state, or false for an empty slot.
- `slot-1-active` through `slot-6-active`: whether an active application occupies the slot.
- `page`, `page-count`, and `total`: selected page, number of pages, and detected application count.

## Package a release

The package ID is `com.cjhackeryt.sysvolumemixer`. This is a new Macro Deck identity, not an update
to `com.cjhackeryt.volumemixer`; existing installations and saved bindings are not migrated.

The manifest currently declares only `win-x64`. Build and inspect the package with:

```powershell
macrodeck-plugin build --source src\VolumeMixer --rid win-x64 --output artifacts
macrodeck-plugin inspect --artifact artifacts\<plugin-id>-<version>-win-x64.macroDeckPlugin
```

The plugin is framework-dependent and uses the .NET 10 runtime shipped with Macro Deck. Release
artifacts must be built with `macrodeck-plugin build`; a regular `dotnet build -c Release` does not
create the manifest's `runtimes\win-x64` entrypoint. `Directory.Packages.props` currently pins all
Macro Deck packages to `3.0.4`.

## Dependencies

- [Macro Deck Plugin SDK](https://github.com/Macro-Deck-App/Macro-Deck)
- [NAudio](https://github.com/naudio/NAudio), for Windows audio session enumeration and volume control
- `System.Drawing.Common`, for extracting executable icons on Windows

The packaged NuGet dependencies and their license texts are listed in
[THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES).

The plugin reads local Windows process and audio-session metadata. It does not send that data to a
remote service. The plugin icon is an AI-generated asset, disclosed in the manifest.

## License

MIT. See [LICENSE](LICENSE).
