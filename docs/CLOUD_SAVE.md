# Cloud save (Play Games Saved Games)

The save is backed up to one Play Games Saved Games slot (`progress`) in the player's own
Google account. We run no server and hold no copy.

## How it behaves

- **Launch:** silent Play Games sign-in, then the slot is read. Nothing is uploaded until
  that read has settled, so a new phone never overwrites progress it has not seen.
- **Which save wins** (`Core/CloudSave.cs`, unit tested in `CloudSaveTests`):
  - no cloud copy, or one without real progress: keep this phone;
  - this phone's save is fresh or failed its signature: restore the cloud copy without asking;
  - both hold real progress and the cloud one is further along (rebirths, then creeks
    open, then lifetime Dollars, then play time): the player picks in "Two saves found";
  - otherwise keep this phone, and its next upload replaces the cloud copy.
- "Real progress" means 2 minutes of play, a second creek, a rebirth or a purchase. The
  first-launch answers and settings do not count, since a fresh install writes them before
  the cloud copy arrives.
- **A restore** writes the cloud copy as the local save (signed with this device's key),
  forgets the other phone's monotonic clock readings and scheduled notifications, and
  reloads the scene. The next return is paid on trusted time only, like after a reboot.
- **Uploads:** every time the app goes to the background, and at most every 5 minutes
  while playing. Play time is set on the slot, and conflicting writes resolve to the
  longer play time.
- **Delete my data** deletes the slot too. If that fails (offline, signed out), a flag
  stays in PlayerPrefs and the delete finishes at the next sign-in, before anything is read.
- **Settings > Save** shows the state and signs in manually when the silent sign-in failed.
- Events: `cloud_restore` (`stage` = fresh / damaged / chosen / ask, `code`) and
  `save_error` (`stage` = cloud_read / cloud_write / cloud_delete, `code` = Play Games status).

The cloud copy is plain save JSON, not signed: the device key lives in each phone's
Keystore, so another phone could not check a signature. The slot sits in the player's
Google account, which only the game can open; editing it would only change the editor's own
single-player game (design doc 13.1 rule 6).

## Without a Play Games app ID

The plugin (`com.google.play.games` 2.2.1, git package in `Packages/manifest.json`) is
compiled in, but `PlayGamesSlot.Configured` is false until
`Assets/GooglePlayGames/Resources/PlayGamesSettings.asset` carries an app ID. Until then no
Play Games call is made, the Settings row is hidden, and the game behaves as before.

## Setup checklist (after the Play Console app exists)

1. Play Console > Nugget Creek > **Play Games services > Setup and management >
   Configuration**: "No, my game doesn't use Google APIs" > create the Play Games project,
   named "Nugget Creek".
2. **Credentials:** create an Android credential for `com.yilkgames.nuggetcreek` for each
   signing certificate. All three are needed or sign-in fails silently in that build:
   - Play app signing key (what players get): Play Console > Test and release > App
     integrity > App signing, SHA-1 of the classic key;
   - upload key: SHA-1 in the private backup next to the keystore;
   - debug key (dev and measure builds):
     `keytool -list -v -keystore ~/.android/debug.keystore -storepass android`.
3. **Properties:** turn **Saved games on**. Without it every open returns an error.
4. Add testers under Play Games services > Testers until the project is published, then
   publish the Play Games project (it is separate from publishing the app).
5. Unity: *Window > Google Play Games > Setup > Android setup*, paste the resources XML from
   Configuration > "Get resources". This writes
   `Assets/Plugins/Android/GooglePlayGamesManifest.androidlib/` and
   `Assets/GooglePlayGames/Resources/PlayGamesSettings.asset`. Commit both: the app ID is
   public (it ships in the manifest), unlike `google-services.json`.
6. Device test, on a dev build with a tester account:
   - play past 2 minutes, background the app, uninstall, reinstall: the save comes back
     after the first-launch screen;
   - play a little on a second phone (or after clearing data and skipping the restore),
     then open the first: "Two saves found" appears only when the other copy is further;
   - Settings > Delete my data, reinstall: nothing comes back;
   - airplane mode at launch: no restore, no upload, the Settings row offers a retry.
7. Update `yilkgames.com` if what is stored changes, and the Data Safety answers in
   `docs/store-listing.md`.
