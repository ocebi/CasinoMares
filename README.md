# Casino Mares

A cozy multiplayer casino for up to 10 friends, where everyone is a cat. Built with Unity 6
(URP), Photon Fusion 2 (Shared Mode), Cinemachine and UI Toolkit. The full design is in
[`Docs/GDD.md`](Docs/GDD.md).

**Games:** Blackjack (2 tables), European Roulette, Texas Hold'em, Baccarat, and 3 slot machines.
There's also chat, a leaderboard (hold Tab, plus the wall board), chip gifting, "Cat Welfare" at the
cashier when you're broke, NPC cats (dealers, seat fillers, patrons, staff) and a spectator panel
when you stand near a friend who's playing.

## Setup

1. Open the project with **Unity 6000.6.3f1**.
2. Put your Photon **Fusion App ID** into `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset`
   (Fusion Hub). The file is gitignored, so every machine that builds needs it. The region is pinned
   to `eu` in `ConnectionManager.Region`.
3. Open `Assets/CasinoMares/Scenes/Casino.unity` and press Play.

Everyone lands in the same room (`CasinoMares-Main`), so there are no codes to share. Your name,
fur color and chip balance are saved on your own machine.

## Controls

| Key | Action |
|---|---|
| WASD / Shift / Space | Move / run / jump (the camera swings behind the cat as it turns) |
| Mouse | Orbit camera |
| Left click | Paw swipe (knock glasses, mugs and yarn off tables) |
| Q (hold) | Emote wheel: point with the mouse, release to play |
| E | Sit down / stand up, cashier, gift chips to a nearby cat |
| Enter | Chat |
| Tab (hold) | Leaderboard |
| Esc | Pause menu (volume, mouse sensitivity, camera auto-follow, leave) |

While seated: **WASD** looks around the table; blackjack **1-5** chips, **Space** deal, then **H** hit,
**Space** stand, **X** double, **P** split; poker **F/C/R** fold/check-call/raise; roulette, baccarat
and slots use **Space** for ready/spin. Everything also has buttons. Poker and baccarat have a
**Rules** button and an optional **Coach** (table settings) that explains the game and suggests moves.

## Development

- **Scene and generated assets:** built by code. Run **Casino Mares ▸ Setup ▸ Rebuild Casino (Assets
  and Scene)** after changing `Assets/CasinoMares/Scripts/Editor/CasinoSceneBuilder.cs`. It
  overwrites the Casino scene, prefabs, materials, UI panel settings and sound bank.
- **Rules:** pure C# in `Assets/CasinoMares/Scripts/Rules`, with EditMode tests in
  `Assets/CasinoMares/Tests/Editor` (Test Runner, *CasinoMares.Rules.Tests*).
- **Two-player testing on one machine:** build the game, then run it as a bot next to the editor:
  ```sh
  open -n Builds/macOS/CasinoMares.app --args -bot -profile bot -botName BotCat -botTable 2 -screen-fullscreen 0 -screen-width 800 -screen-height 450
  ```
  The bot joins, sits at a blackjack table and plays basic strategy. The build remembers the bot's small
  window, but a normal launch that finds a small leftover window switches back to full screen
  (`DisplaySetup`). Multiplayer Play Mode also
  works (Window ▸ Multiplayer ▸ Multiplayer Play Mode).
- **Settings to keep:**
  - Player Settings "Run In Background" is on, so alt-tabbed players stay in sync.
  - Enter Play Mode Options: domain reload on, scene reload off.

## Credits

See [`CREDITS.md`](CREDITS.md).
