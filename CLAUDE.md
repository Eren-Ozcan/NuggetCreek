# Nugget Creek

Portrait idle gold-panning game. Unity 6000.3.25f1 (6.3 LTS), URP with the 2D Renderer.
Android first (`com.yilkgames.nuggetcreek`), iOS second. The game itself is English only.

## Layout

- `Assets/Scripts/Core/` — engine-free game logic (`NuggetCreek.Core` asmdef,
  `noEngineReferences: true`): `BigNumber`, `NumberFormat`, `EconomyConfig`, `Economy`,
  `Stat`/`StatSheet`, `GameCatalog` (upgrades, 16 crew, progress goals),
  `OfflineEarnings`, `RewardedDoubleButton`, `SaveEnvelope`, `PacingModel`,
  `PlayerProgress` (save data) and `GameSession` (rules: collect, idle, offline, purchases,
  Gems, crew hires and candidates, goals).
  No `UnityEngine` here — platform code (ads, Remote Config, Keystore, clocks) lives in
  other assemblies and feeds this one plain values.
- `Assets/Scripts/Game/` — Unity layer (`NuggetCreek.Game`): `GameRoot` builds the whole
  greybox UI in code (uGUI, `UI/`), `SaveStore` (signed PlayerPrefs), `GameClock`
  (monotonic + trusted HTTPS time), `FakeRewardedAds` until the real ad SDK lands.
- `Assets/Scenes/Creek.unity` — only camera, EventSystem and `GameRoot`. Regenerate with
  menu *Nugget Creek > Rebuild Greybox Scene* or
  `Unity.exe -batchmode -quit -projectPath . -executeMethod NuggetCreek.Editor.GreyboxSceneBuilder.Build`.
- `tests/NuggetCreek.Core.Tests/` — NUnit tests that compile the core sources outside
  Unity. Run with `dotnet test tests/NuggetCreek.Core.Tests`. `LangVersion` is pinned to
  9.0 so the core stays compilable by Unity.
- `Assets/Tests/PlayMode/` — greybox smoke tests that press real buttons. Run with
  `Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults <file>`
  (the editor must be closed); set `NC_SHOT_DIR` to also save 1080x1920 screenshots.
  `RobustnessTests` add the save round trip, damaged saves, a seeded monkey and a layout check.
- `scripts/android-device-tests.sh` — device pass on a connected phone (starts, background kill,
  no network, font scale, monkey, memory); backs up and restores the phone's save.
  What each layer covers and what it does not: `docs/TESTING.md`.

## Android builds

`Assets/Scripts/Editor/AndroidBuild.cs` owns every Android setting (package, IL2CPP ARM64,
targetSdk 36, version code, signing). Menu *Nugget Creek > Android* or
`Unity.exe -batchmode -quit -projectPath . -executeMethod NuggetCreek.Editor.AndroidBuild.<Method>`:

- `BuildDevApk` — development APK for phone playtests, debug key.
- `BuildEmulatorApk` — dev APK that also carries x86_64. The emulator's ARM64 translation
  crashes the ARM64-only build at start; run the AVD with `-gpu swiftshader_indirect`
  (the default Vulkan path takes the emulator down when Unity starts).
- `BuildMeasureApk` — store settings plus `PerfProbe` (logcat FPS/memory), debug key.
  Then `scripts/android-measure.sh` installs it, taps for 2 min and prints APK size, PSS and FPS.
- `BuildReleaseAab` — Play upload, signed with the upload key; refuses to overwrite an existing
  version code.
- `BumpPatch` — version rule is `AppVersion` (Core): name `major.minor.patch`,
  versionCode `major*10000 + minor*100 + patch`. Every Play upload bumps at least the patch.

Upload key: `android-keystore/` (gitignored), password in `android-keystore/nuggetcreek-upload.pass` or
`NC_KEYSTORE_PASS`; backup and SHA-1 in `C:\Projects\pictures\nugget-creek\android-keystore\`.
Dev/measure builds stay on the debug key on purpose: a key switch on a phone needs an uninstall,
which drops the AndroidKeyStore key that signs the save (`SaveKey`), so the save is orphaned.

## Firebase

On a fresh clone: run `scripts/fetch-firebase.sh` (SDK tarballs, gitignored), then copy
`C:\Projects\pictures\nugget-creek\firebase\google-services.json` to `Assets/`. It holds the
project API key and stays out of this public repo; the Firebase editor plugin regenerates
`StreamingAssets/google-services-desktop.json` and the `Plugins/Android/Firebase*.androidlib`
folders from it (all gitignored).

## Game art

Sprites, crew portraits, the 10 dredge tiers and creek backgrounds live in `Assets/Resources/Sprites/`
(gitignored; loaded by `Game/Art.cs`, import settings in `Editor/SpriteImportRules.cs`). The main
screen is a top-down river drawn in code (`UI/RiverView.cs`) with the tier's dredge in the middle
(`UI/DredgeView.cs`, whose table holds each tier's hull and chest position on its drawing; re-measure
it after re-cutting a dredge); the side-view creek paintings are only used on the map. Cutting a
dredge also writes its silhouette, its moving parts and `Dredge/parts.json` (part rects, chimneys,
spray points; the spec is `DREDGE_PARTS` in the cutter), which `UI/DredgeParts.cs` animates.
A grab bucket (`jaws` in the spec) is cut into `_fixed`, `_left` and `_right` layers with its own
hinges; check new masks with a quick offline pose render before looking in the game.
Painted top-down rivers go in `Sprites/Rivers/river_NN.png` and replace the code river per creek. On a fresh clone run
`scripts/fetch-art.sh`, which copies them from `C:\Projects\pictures\nugget-creek\game-art\`
(private pictures repo). Without it the game runs with the greybox shapes. The source sheets and
the cutter (`tools/cut_sprites.py`, git-excluded) are backed up in `game-art/source/`; after
re-cutting, copy `Assets/Resources` back there, then commit and push the pictures repo.
The painted map (`Sprites/Map/`: four stacked panels plus `markers.json`, one marker per creek)
comes from `tools/build_map.py`, which evens out the panel seams; without it `MapPanel` falls
back to a plain list.

Audio follows the same path: `Assets/Resources/Audio/{Music,SFX}/` (gitignored), masters in
`game-art/Resources/Audio/`, copied by `fetch-art.sh` when present, file names as in section 6 of
`docs/AUDIO_PROMPTS.md`. `Game/Sound.cs` plays whatever is there and stays silent for missing
clips; import settings in `Editor/AudioImportRules.cs`, the pure rules in `Core/SoundRules.cs`.

Fonts are committed (open licences, next to them in `Assets/Fonts/`): `Assets/Fonts/Resources/Fonts/`
holds `Body` and `BodyBold` (static Fredoka 500/700 cut from the variable font with
`fontTools.varLib.instancer`) and `Heading` (Ultra, panel titles). `Ui.Font`/`BoldFont`/`HeadingFont`
fall back to the built-in font when a file is missing. Panels, rows and buttons use procedural
rounded sprites (`Ui.Panel`, `Ui.Button`); colours live only in `Palette`.

## Rules

- **Private material stays out of git.** The design docs (`docs/GAME_DESIGN.md`,
  `docs/BRANDING.md`, `docs/AUDIO_PROMPTS.md`), competitor research (`docs/research/`,
  the teardown file, scraped data at the root) and `tools/` are excluded through
  `.git/info/exclude` (local, so the ignore list itself does not leak names). Never
  stage them, and never name competitor games or companies in committed files
  (code, comments, commit messages, README). Refer to "the design doc" instead.
- Economy numbers come from `EconomyConfig` defaults (launch values from the design doc)
  and are overridden by Remote Config at session start only. Never hard-code a tuning
  number elsewhere.
- `tools/economy_tune.py` is the reference implementation of the economy formulas; the
  C# `Economy`/`PacingModel` must produce the same numbers (see `EconomyTests`). If a
  formula changes, change both and update the expected values.
- Dollar amounts are `BigNumber`, displayed only through `NumberFormat.Dollars`.
  Gems and Perk Points are plain integers.
- Offline earnings never trust the device clock for payouts (`OfflineEarnings`).

## Store / marketing assets

Store listing images, feature graphic, icon and screenshots are **never committed**
to this repo.

1. Local, gitignored copy: `docs/store-assets-originals/`.
2. Private backup: `C:\Projects\pictures\nugget-creek\` (local clone of the private
   `Eren-Ozcan/pictures` repo). When adding or updating a store asset, put it in both
   places, then commit and push in the `pictures` repo.

Studio-wide accounts, domain and Play Console details: `C:\Projects\pictures\STUDIO.md`.
