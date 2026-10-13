# Dota Lobby Overlay

Standalone Windows x64 GUI for displaying Dota 2 lobby players. The intended capture path is an embedded, patched NetHook2 DLL. Steam packets are forwarded directly to the overlay window with `WM_COPYDATA`; the overlay does not poll dump folders, create `.bin` files, or use a named-pipe bridge.

## Build

The GitHub Actions workflow builds the native NetHook2 dependency and then publishes a self-contained .NET 8 Windows x64 single-file executable. Download the `DotaLobbyOverlay-win-x64` artifact from a successful workflow run.

## Runtime behavior

- The app attempts to launch NetHook2 injection elevated and targets the running Steam process.
- When a GC packet contains a `CSODOTAStaticLobby` object with player names, the app brings its window to the foreground.
- If ten player names are present, the first five are displayed as Team 1 and the next five as Team 2, preserving packet order.
- The current parser does not reliably identify the local player. It therefore does not guess which nickname is yours.
- Packet parsing and forwarding are intended to happen in memory. The DLL is extracted to the current user's LocalAppData directory because Windows needs a physical DLL path for injection; packet dumps are not written.

## Limitations

A successful CI build proves compilation and packaging only. Actual packet delivery and roster detection still need to be verified on a Windows machine with Steam and Dota 2 running. The patched NetHook2 source is based on upstream SteamKit and may need adjustment if upstream changes its source layout or protocol behavior.
