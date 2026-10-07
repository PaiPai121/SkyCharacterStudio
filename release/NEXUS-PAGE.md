# Upload draft — not yet published

Game: Trails in the Sky 1st Chapter

Game domain: trailsintheskyfirstchapter

Name: Sky Character Studio - CLE Edition (Beta)

Version: 0.3.8-beta

Summary: Offline 1st/2nd Chapter character shape editor with model previews, installation backups, optional 1st Chapter test summons and Chinese/English UI.

## Description

Select a character from your own installed game, preview shape adjustments and click **Save & apply to game** to generate and install the result without editing the original PAC archives. The editor supports overall width and chest/torso adjustments for characters whose adulthood is confirmed in the age catalog. Minors and characters with unknown ages are restricted to overall width. It automatically identifies usable bones or estimates the local surface using spine/neck landmarks and skinning weights.

Windows x64 .NET and Python runtimes are included. No original game assets are distributed. The 2nd Chapter installer checks its x64 executable and readable character resources without requiring a particular game EXE hash. A changed 2nd executable hash passed isolated installation and restore tests; this does not establish compatibility for every game update. The 1st Chapter's address-specific plugins remain limited to inspected 1.0.5.0/1.0.7.0 executable hashes. The reported 2nd Chapter 1.03.1 build and generated 2nd Chapter models still need in-game verification. GungHo/global-edition compatibility is not claimed.

The interface opens in Chinese and can be switched to English from the upper-right selector. See README-PLAYERS.md for Chinese instructions and an English quick start. The optional F8 summon requires script_sc.pac from the player's 1st Chapter game; if summon setup fails, the editor disables F8, reports the reason, and still installs the edited model normally. If the game folder or executable fails validation, the editor saves the model locally without installing. Keep installation backups if you want to undo changes.

Optional F8 summons are off by default. Enable the test option before applying, enter a free-roaming field scene and press F8. Press F9 then F8 to compare the original and edited model. This creates a temporary test actor, not a party member.

Offline coverage: 54 adult models, 432 export round trips, WPF preview/export position and normal checks, and front/side image review. User-confirmed in-game tests include Scherazard and Julia. Not every character animation, costume collision or renderer material has been tested in-game. See the bundled README for limitations and third-party notices. The player ZIP contains runtime files only; development source is kept separately in the project repository.

## Files / settings to review in the Nexus form

- Main file: portable Windows x64 beta ZIP; manual extraction outside the game folder.
- Use an appropriate adult/body-mod content classification for the chest-adjustment feature; avoid presenting the tool as unrestricted character editing.
- Icon source: the executable icon uses the user-provided Estelle/Joshua artwork. Confirm that you have permission to use this artwork before public upload; apply Nexus's AI-Generated Content tag only if the supplied image is AI-generated.
- Preserve GPL and PolyForm notices. Do not enable monetization for this build containing noncommercial components.
- Set page permissions and author/account details through the user's Nexus account; no author identity has been invented here.
- Use only relevant screenshots of verified adult characters. Do not claim all characters are in-game tested.

Official references checked: https://www.nexusmods.com/games/trailsintheskyfirstchapter/mods ; https://help.nexusmods.com/article/28-file-submission-guidelines ; https://help.nexusmods.com/article/117-why-has-my-mod-been-quarantined .
