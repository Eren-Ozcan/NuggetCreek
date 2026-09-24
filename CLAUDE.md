# Nugget Creek

Portrait idle gold-panning game. Unity 6000.3.25f1 (6.3 LTS), URP with the 2D Renderer.
Android first (`com.yilkgames.nuggetcreek`), iOS second. The game itself is English only.

## Layout

- `Assets/Scripts/Core/` — engine-free game logic (`NuggetCreek.Core` asmdef,
  `noEngineReferences: true`): `BigNumber`, `NumberFormat`, `EconomyConfig`, `Economy`,
  `Stat`/`StatSheet`, `GameCatalog` (upgrades, 16 crew),
  `OfflineEarnings`, `RewardedDoubleButton`, `SaveEnvelope`, `PacingModel`.
  No `UnityEngine` here — platform code (ads, Remote Config, Keystore, clocks) lives in
  other assemblies and feeds this one plain values.
- `tests/NuggetCreek.Core.Tests/` — NUnit tests that compile the core sources outside
  Unity. Run with `dotnet test tests/NuggetCreek.Core.Tests`. `LangVersion` is pinned to
  9.0 so the core stays compilable by Unity.

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
