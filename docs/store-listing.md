# Store listing and Play Console answers

Draft for the Play Console app (`com.yilkgames.nuggetcreek`). Graphics (icon, feature
graphic, screenshots) are not in this repo: `docs/store-assets-originals/` locally and the
private pictures repo.

Status (2026-09-28): the Play Console app does not exist yet. Everything below is ready to
paste once it does.

## Main store listing (en-US, default language)

The game is English only, so the listing is English only.

**App name** (30 max, 28 used)

    Nugget Creek: Idle Gold Rush

**Short description** (80 max)

    Pan for gold, hire a crew and strike it rich along 20 frontier creeks.

**Full description** (4000 max)

    Grab your pan and head for the creek. Gold is shining in the water, and it is all yours.

    Nugget Creek is a relaxed idle game set in the old gold rush. Swipe the water to scoop up
    gold dust and Nuggets, sell your haul, and turn a muddy riverbank into a booming claim.
    Hire a crew that keeps panning while you are away, then come back to a pile of Dollars.

    SWIPE, COLLECT, GROW
    • One-handed play: swipe across the creek to collect everything you touch
    • Rare Nuggets sparkle, pulse and have their own shape, so you can spot them without
      relying on colour
    • Upgrade your shovel, pan and sluice to raise every catch

    HIRE A CREW
    • 16 prospectors, each with a skill of their own
    • Candidates wait ten minutes for your answer. No rushing, no reflexes needed
    • Level them up and watch the claim run itself

    TRAVEL 20 CREEKS
    • From quiet Willow Bend to the frozen Glacier Run and the rich Last Chance Lode
    • Every new creek brings richer gold and a new look
    • Start over with a rebirth for permanent bonuses and go further every time

    SOMETHING NEW EVERY DAY
    • The Mother Lode: a burst of gold that rewards quick hands
    • Daily jobs, a daily streak and the Daily Wash, with every reward and its odds listed
    • The Miners Guild: level it up by playing
    • Gear chests with their contents shown up front

    PLAYS WHILE YOU ARE AWAY
    • Your crew keeps working after you close the game
    • Come back to your haul, or double it

    PLAY YOUR WAY
    • Vibration settings, a high contrast mode, and text that follows your phone's font size
    • Nothing important is told by sound alone
    • Your progress can be backed up to your Google Play Games account

    Nugget Creek is free to play. It offers optional in-app purchases, including items with
    random contents, and shows ads; a one-time purchase removes the ads.
    There are no real money prizes. All gold and Gems exist only in the game.

    Questions or ideas? Write to yilkgamesstudio@gmail.com
    Privacy Policy: https://yilkgames.com/privacy-policy/

Before publishing, check the full description against the build: every feature above must be
in the game that reviewers install.

**Rules the text keeps** (design doc 2, 14.2): no "gambling", "casino", "slot" or "spin"; the
no-real-money line stays; nothing addressed to children ("kids", "for children", cartoon-age
wording), so the game stays outside the Families programme; no other game or company names.

**Category and tags:** Games > Simulation (idle games sit there). Tags: Idle, Tycoon, Casual,
Offline, Single player. Check the offered tag list when filling it in.

**Contact details:** email `yilkgamesstudio@gmail.com`, website `https://yilkgames.com`.
**Privacy Policy URL:** `https://yilkgames.com/privacy-policy/`.

## App content answers

| Section | Answer |
|---|---|
| Privacy policy | `https://yilkgames.com/privacy-policy/` |
| App access | All functionality is available without special access (no login; Play Games sign-in is optional) |
| Ads | Yes, the app contains ads |
| Content rating | IARC questionnaire below |
| Target audience | 13-15, 16-17, 18 and over. Not 12 and under |
| Appeals to children | No. The store listing and art are not aimed at children |
| News app | No |
| Government app | No |
| Financial features | None |
| Health | No |
| Data safety | Draft below |
| Account deletion | The app creates no account. Data deletion URL: `https://yilkgames.com/account-deletion/#data-only` |
| Advertising ID | Yes, used for advertising and analytics (AdMob, Firebase). The `com.google.android.gms.permission.AD_ID` permission comes merged in from the ads SDK (present in the 0.1.0 dev APK) |

The game asks for an age range at first launch. Under 13 turns analytics and crash reports off
and marks every ad request as child-directed (`Core/Compliance.cs`). That is a safety net and does
not make the game child-directed.

## Content rating (IARC questionnaire)

Category: **Game**.

| Question | Answer | Why |
|---|---|---|
| Violence (any kind, including cartoon or fantasy) | No | Panning, hiring, upgrading. The Mother Lode is a gold rush event, not a fight |
| Fear / horror | No | |
| Sexuality, nudity | No | |
| Language (profanity, crude humour) | No | Old Pete's lines are friendly |
| Controlled substances (drugs, alcohol, tobacco) | No | No saloon drinking or tobacco shown. Recheck if art adds any |
| Gambling: real money | No | |
| Simulated gambling (casino games, betting) | No | The Daily Wash is a daily reward with listed odds, not a casino game. No slots, cards or roulette (design doc 2) |
| Users interact or exchange content | No | No chat, no sharing, no user-generated content |
| Shares the user's location with other users | No | |
| Digital purchases | Yes | Gems, offers, Remove Ads |
| Purchases include random items | **Yes** | Offers grant random crew members and gear cards; gear chests have random contents |
| Unrestricted internet access (web browser) | No | Only links to our privacy pages |

Expected result: ESRB Everyone, PEGI 3, USK 0, with the notice **"In-Game Purchases (Includes
Random Items)"**. If a rating comes back higher, check the answer that caused it before accepting.

## Data safety (draft)

Base: the SDKs in the build (Firebase Analytics, Crashlytics and Remote Config; Google Mobile
Ads with UMP; RevenueCat; Play Games Saved Games; Unity) and the privacy policy on
yilkgames.com. Check the current Data Safety guidance of each SDK when filling in the form, as
SDK versions change what they collect.

**Overview**

| Question | Answer |
|---|---|
| Collects or shares any of the required user data types? | Yes |
| All user data encrypted in transit? | Yes. Every SDK and the time check use HTTPS |
| Can users request that data be deleted? | Yes: in-game Settings > Delete my data, and the deletion URL above |
| Independent security review | No |

**Data types**

| Type | Collected | Shared | Purpose | Optional? | Source |
|---|---|---|---|---|---|
| Location > Approximate location | Yes | Yes | Advertising or marketing, Fraud prevention | Required | AdMob (from IP address) |
| App activity > App interactions | Yes | Yes (AdMob) | Analytics, Advertising | Required | Firebase Analytics events, AdMob |
| App activity > Other actions (game progress) | Yes | No | App functionality | Optional (only when signed in to Play Games) | Play Games Saved Games |
| App info and performance > Crash logs | Yes | No | Analytics | Required | Crashlytics |
| App info and performance > Diagnostics | Yes | Yes (AdMob) | Analytics, Advertising | Required | Crashlytics, Firebase, AdMob |
| Device or other IDs | Yes | Yes | Analytics, Advertising, Fraud prevention | Required | Advertising ID (AdMob), Firebase app instance and installation IDs, RevenueCat anonymous app user ID |
| Financial info > Purchase history | Yes | No | App functionality, Analytics | Required | RevenueCat, `iap_*` events |

Not collected: name, email, contacts, precise location, photos, files, messages, audio,
health, web history, calendar. Personal info > User IDs is not collected: no accounts. The
Play Games player ID stays inside Google Play services and never reaches us.

Notes for the form:

- "Shared" follows Google's definition: data sent to a third party that is not a service
  provider acting for us. Firebase and RevenueCat act as processors (collected, not shared);
  AdMob uses data for its own advertising, so its types are shared.
- "Processed ephemerally": No.
- Under-13 players send no analytics or crash data, but the form describes the app as a
  whole, so the rows above stay "Required".
- Game progress in Play Games lives in the player's Google account. Declare it as
  collected once cloud save is live (`docs/CLOUD_SAVE.md`), and remove the row if the game
  ships without it.

## Before Faz 5 (closed test)

- [ ] Play Console app created, package `com.yilkgames.nuggetcreek`, "Game", free, "Ads: yes"
- [ ] Main store listing pasted from above, graphics uploaded from the pictures repo
      (512 icon, 1024x500 feature graphic, at least 4 phone screenshots with the final art)
- [ ] App content: every row of the table above, content rating certificate received
- [ ] Data safety submitted
- [ ] AdMob app linked to the store listing (removes "Review required")
- [ ] RevenueCat key and Play products, then the IAP test with a licence tester
- [ ] Play Games project and cloud save test (`docs/CLOUD_SAVE.md`)
- [ ] First `BuildReleaseAab` upload to internal testing, then closed testing with 12 testers
      for 14 days
