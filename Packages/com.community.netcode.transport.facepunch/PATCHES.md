# Local patches

This package is vendored (embedded in `Packages/`) rather than pulled from git, so these
changes stick. If you ever re-pull it from upstream, re-apply them.

## 1. Orphaned `#endregion` — `Runtime/FacepunchTransport.cs`

Upstream `main` ships with four `#endregion` directives and only three `#region`s. The trailing
one at the end of the class has no opener, so the file does not compile at all:

```
error CS1028: Unexpected preprocessor directive
```

Removed the orphan. No behavioural change whatsoever — `#region` is purely an editor folding
hint.

# Why this package is vendored

- Installing from a git URL needs Git for Windows on PATH, which Unity shells out to.
- The package targets `com.unity.netcode.gameobjects: 1.0.0-pre.4` and is not actively
  maintained — the bug above is proof it is not being compiled upstream. Owning the source
  means we can fix things like this in seconds instead of being blocked.
- It bundles its own Facepunch.Steamworks with correct per-platform meta files. Do **not** also
  install Facepunch.Steamworks into `Assets/` — the duplicate assemblies make every Steam type
  ambiguous (`CS0433`).

Verified against NGO 2.13.2: `FacepunchTransport` implements exactly the `NetworkTransport`
abstract surface that NGO 2.x declares, so the stale dependency version is a floor, not a
ceiling.

## 2. Steam lifetime ownership — `Runtime/FacepunchTransport.cs`

The transport assumed it owned the Steam client. It cannot: lobby creation needs Steam up
before `NetworkManager` initialises the transport, so `SteamManager` initialises it first.

- **`Initialize`** now returns early when `SteamClient.IsValid`. Previously it called
  `SteamClient.Init` unconditionally and logged a red `already initialized` exception on every
  single host.
- **`Shutdown`** no longer calls `SteamClient.Shutdown()`. `NetworkManager.Shutdown()` runs
  every time you leave a session, so the original behaviour tore down the entire Steam client
  mid-play — leaving `RunCallbacks` pumping a dead client, breaking the overlay, and making
  every later host attempt fail. `SteamManager.OnDestroy` owns shutdown.

Connections are still closed properly; only the process-wide Steam teardown was removed.
