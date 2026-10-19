# Casino Mares — Game Design Document

| | |
|---|---|
| **Version** | 0.2 (draft implementation) |
| **Last updated** | 2026-09-30 |
| **Engine** | Unity 6000.6.3f1, URP |
| **Networking** | Photon Fusion 2.1.3, Shared Mode |
| **Platforms** | Windows, macOS (keyboard + mouse); web browsers on desktop, phones and tablets (WebGL, experimental, §15) |

---

## 1. Overview

### 1.1 Pitch
A cozy multiplayer casino where every guest is a cat. Up to 10 friends wander a warm, low-poly lounge,
hop onto stools at the tables and play blackjack against a cat dealer. Cards and chips float around
by magic, NPC cats fill empty seats and wander the room, and you can walk up behind a friend to watch
their hand play out.

### 1.2 Design pillars
1. **Game night with friends.** The game is a place to hang out. Friends talk over Discord, and
   the game adds shared moments: big wins, busts, crowding around a hot table.
2. **Everything is visible and in sync.** Every player sees the same cards, chips, turns and
   timers at the same moment. Walking up to a friend's table shows exactly what they're seeing.
3. **Cats being cats.** Real four-legged cats sit on stools, meow when they win and sulk when they
   bust. Cards float because nobody here has hands.
4. **Small scope, high polish.** Finish blackjack and make it feel great before adding any other
   game.

### 1.3 Audience and trust model
- A private group of friends, at most 10 players in one room.
- **Clients are fully trusted.** There is no anti-cheat and no server-side validation. Each client
  owns its own balance. That keeps the networking simple on purpose, so don't add validation layers
  later.

### 1.4 Scope summary

| In scope (v1) | Later | Out of scope |
|---|---|---|
| One casino hall, one room | Roulette, Texas Hold'em, Slots, Craps, Baccarat | Real money, microtransactions |
| 2 blackjack tables × 5 seats | Cosmetic accessories | Matchmaking, multiple rooms |
| NPC dealers, seat-fill NPCs, ambient NPCs, staff | Gamepad support | Mobile |
| Persistent local chip balance | Stats and history | Anti-cheat, dedicated server |
| Text chat, leaderboard, chip gifting | | In-game voice (Discord is used) |
| Spectator info panel near active players, emote wheel, paw-pushable props | | |

---

## 2. Core loop

```
Launch → enter name + pick fur color → Play → spawn in the casino lobby
   ↓
Walk around (3rd person) ──► approach a table ──► [E] Sit
   ↑                                              ↓
   │                                   Bet → Play hand → Settle (repeat)
   │                                              ↓
   └──────────────── [E] Stand up ◄──────────────┘

Meanwhile: watch friends' tables, chat, gift chips, check leaderboard, visit cashier when broke.
```

---

## 3. Characters

### 3.1 Cats
- **Every character is a real, quadruped cat**: players, dealers, staff and patrons. They don't
  stand upright, hold objects or use hands.
- Players and NPCs use the **same model and rig**. Fur color or texture variant tells them apart.
- Scale: the casino is built at cat scale. Tables are low, stools are tall cushions or cat-tree
  platforms, and the camera is set up for a cat-sized character.

### 3.2 Player customization
- **Display name**: shown on a nameplate above the cat. If two players choose the same name, the
  second gets a suffix (`Mochi`, `Mochi (2)`).
- **Fur color**: pick one of six presets: **Midnight, Ginger, Snow, Smoke, Cream, Cocoa**. The first
  three are the cat pack's own textures, and the other three are tinted copies (see §10.1).
- Both are chosen on the main menu and saved locally. There is **one profile per machine**.
- Hats and other accessories are planned for later and aren't in v1.

### 3.3 Required animations

| Animation | Used by | Notes |
|---|---|---|
| Idle (several variants) | all | Random variation for life |
| Walk, Run | players, patrons | Blend tree driven by speed |
| Jump | players | Free jump on Space: about 1 m high with a slower, floaty fall (0.8 s in the air). Cats can jump onto tables and the bar |
| Sit idle | seated players, dealers, patrons | **Must have** |
| Happy / celebrate | win, blackjack | Triggered by table events |
| Angry / sulk | bust, dealer blackjack | |
| Meow | emote-like reaction | Also a sound |
| Eat / drink | bartender area (ambient) | Nice to have |
| Sleep / lie down | ambient patrons | Nice to have |

### 3.4 Controls (keyboard + mouse)

| Input | Action |
|---|---|
| WASD | Move (camera-relative; the camera swings behind the cat as it turns). A key always trots; a gentle joystick push on touch walks slowly, growing to the trot at 90% of the stick |
| Mouse | Orbit camera |
| Shift | Run |
| Space | Jump |
| Left click | Paw swipe: swat a glass, mug, bottle or yarn ball in front of you |
| Q (hold) | Emote wheel: move the mouse toward an emote, release Q (or click) to play it |
| E | Interact: sit down / stand up, cashier, gift chips to the nearest player |
| Enter | Open chat / send |
| Tab (hold) | Leaderboard overlay |
| Esc | Pause menu (settings, leave) |

Seated shortcuts (every action also has a clickable button; the cursor is free while seated).
**WASD** (or the arrows) look around the table at every game, so no game uses W, A, S or D:

| Game | Keys |
|---|---|
| Blackjack | **1-5** add chips, **Backspace** clear, **Space** deal, **H** hit, **Space** stand, **X** double, **P** split, **V** switch the table view (Seat ↔ Overhead) |
| Roulette | **Space** ready (bets are placed by clicking the layout) |
| Baccarat | **Space** ready (click Player / Banker / Tie), **V** table view |
| Poker | **F** fold, **C** check/call, **R** raise, **V** table view |
| Slots | **Space** spin |
| Bar stool | **Space** order a glass of wine (20 chips) |

### 3.5 Camera (Cinemachine)
- **Exploration**: a third-person camera behind the cat (`CinemachineCamera` with orbital follow
  and a rotation composer). Mouse orbits it, and it avoids walls through collision/deoccluder.
  - It follows a smoothed anchor instead of the cat itself. The anchor tracks the cat sideways
    exactly, but only 15% of a jump's height, so jumping doesn't bounce the view (less motion
    sickness). Landing somewhere higher (a table top) eases the view up.
  - **Auto-follow** (on by default, the "Camera turns with the cat" toggle in the pause menu): while the
    cat walks, the orbit swings behind the cat within about a second, and never when the cat walks
    straight at the camera. It aims at the cat's own facing (not its smoothed velocity) with an
    exponential approach: 3/s of the remaining angle, never slower than 25°/s nor faster than 100°/s,
    stopping within 1°; nothing happens beyond 150° off (walking at the camera) or for 0.6 s after any
    mouse look. The cat itself turns to its move direction in ~0.17 s (`CatMotion.TurnSharpness`). So
    WASD or the joystick alone can steer around the room.
- **Seated**: each seat has its own `CinemachineCamera` that frames the player's hands, the
  dealer's hand and the chip area. Sitting down blends to it (~0.8 s ease-in-out) and standing up
  blends back.
  - **Look around**: A/D swing the seat camera up to 35° around a point in the middle of the felt, and
    W/S tilt it 12°. The table stays in view while the other players' hands come into sight. The view
    stays where it's left and resets the next time you sit down.
  - **Table views** (blackjack, baccarat and poker; `TableViewControl` is the shared button/caption/key):
    the View button (**V**) switches between **Seat** and **Overhead** (a Wide view, the seat camera moved
    back and up, was dropped after the 2026-10-19 playtest). Overhead is a camera on the players' side above the felt,
    pitched 70° with a fixed 50° lens (`CasinoSceneBuilder.TableViews`, `OverheadFraming`). It frames the
    felt only, not the stools. At runtime it backs away just far enough for the felt to fit the screen's
    actual aspect, so a phone held sideways gets a much closer view than a monitor. The fitted camera is
    at most 2.2 m high.
    - Zoom: pinch on touch, or the scroll wheel, from 0.7× to 1.3× of the fitted distance. The zoom is
      remembered per table (`PlayerPrefs` "OverheadZoom.<table>") and kept under the ceiling beams (3.1 m).
    - No look input. The
    brain's 0.8 s blend carries each change, a caption names the view for 1.5 s, standing up goes back
    to Seat, and the last choice is remembered per game (`PlayerPrefs` "TableView.Blackjack",
    "TableView.Baccarat", "TableView.Poker") and restored on sitting down.
- Only the local player's cameras are active. Remote players have no camera objects.

---

## 4. The casino world

### 4.1 Look and feel
**Cozy lounge at night.** Warm, dim lighting with pools of light over the tables. Velvet, dark wood
and brass. Soft jazz. As built:
- A wood-beamed ceiling with a brass crown molding. Pendant lamps hang over every game, the bar, the
  lounge, the cashier and the slots; each is a spot light (soft shadows over the games), a glowing
  bulb disc, a soft light shaft (`LightCone` shader) and drifting dust motes.
- Brass wall sconces, a pink "CASINO MARES" neon sign over the roulette, glowing slot marquees, and
  two shelves of bottles behind the bar lit by glowing strips.
- Generated textures: burgundy carpet with a gold diamond lattice, striped red wallpaper with gold
  pinstripes, wood grain and felt.
- A baked reflection probe for brass and glass, light warm fog, and stronger SSAO.
- Post-processing: ACES tonemapping, bloom for the lamps and neon, white balance and split toning,
  vignette and fine film grain.
- Cats get a warm rim-light pass (`CatRim` shader) so dark fur reads against the dim room.

### 4.2 Layout (single hall)

The hall is 22 × 14 m (as built):

```
 ┌───────────────────────────────────────────────────────────┐
 │   BAR (bartender)        [ROULETTE]       LEADERBOARD WALL │
 │                                                            │
 │ [POKER]      [BJ Table 1]            [BJ Table 2]   LOUNGE │
 │                                                     SLOTS ▐│
 │                       [BACCARAT]                    SLOTS ▐│
 │   CASHIER                               ENTRANCE    SLOTS ▐│
 └───────────────────────────────────────────────────────────┘
```

- Players spawn at the entrance: a brass-framed doorway in the south wall, over the carpet runner,
  into a short vestibule with a velvet curtain. NPC players arrive and leave through it (§5.6).
- The NavMesh (AI Navigation package) covers the whole walkable floor for NPCs. It is baked by the
  scene builder.
- Seaside scene only: the terrace outside the east door runs on 9 m south along the hall's east wall and
  past its corner. Seen from the café tables looking out to sea, that is to the right, so their view is
  open. The glass railing goes round the new part.
  - In its south-east corner, open water to the east and south, is a 4 × 5.5 m stone pool, 1.2 m deep,
    the water 0.15 m below the deck.
  - Shallow steps at its north end, toward the terrace door, lead in and out.
  - Two cat-sized sun loungers lie along its west edge, on the hall side south of the steps, facing
    across the water; a fourth café table and a pool bar (§4.3) are at the south end.
  - Patrons never walk into it.
  - Stepping into the water makes a cat swim (the Swim state, plan §11.1): it floats at the surface,
    paddles slowly, and can't jump, sprint, emote or paw. Walking up the steps ends it. Other players
    see it swim.
  - The stone edge is too high to climb from the water, so the steps are the only way out.

### 4.3 Staff NPCs
- **Dealer cats**: one per table. They sit behind the table and react to results (see §5.8).
- **Cashier cat**: this is where a broke player gets a refill (§7.2).
- **Bartender cat**: serves wine at the bar stools (sea scene). A cat seated on a stool orders a glass
  (**Order wine · 20**, or **Space**; 20 chips, paid once the bar accepts the order, refused with a toast when short). The bar
  (`WineBar`, on the stools' `TerraceTable`, master-client authority) stamps an order id and tick per
  seat; every client plays the same 7.5 s sequence from the tick, so late joiners agree: the glass is
  poured in front of the bartender (0.8 s), the bartender stands, turns and swats it (`PlaySwipe`), it
  slides down the counter to the stool (to 2.5 s), the cat laps at it (the `Drink` animator state,
  `licking`, 4 s) while the glass stands on the counter's edge under its snout and empties (it
  doesn't ride on the cat), then the empty glass sits on the
  counter for a second and the seat can order again at once (three glasses in about 25 s). A glass
  belongs to the cat that ordered it; if that cat leaves the stool, the glass is cleared. Chat line: "<name> ordered a glass of wine."
- **Pool bartender cat** (sea scene): a second bar by the pool, along the extension's south railing, with
  three stools on the pool side, so seated cats look across the counter at the open sea. It has
  its own bartender and serves wine exactly like the hall bar, with its own orders. Its glasses count
  toward the same `DrinkCount`, so drinks at both bars add up.
- **Drunk cats**: each glass counts for **6 minutes**; the owner keeps `PlayerCat.DrinkCount`
  (networked, not saved: a reload sobers you). At **3** glasses the cat is drunk: everyone sees a slow
  sway of the model (roll ±4°, yaw ±3° at 0.7 Hz, a fifth stronger per extra glass up to 5), the chat
  says "<name> has had one too many." For the drunk player (`TipsyEffect`, "Tipsy visuals" in the
  pause menu, on by default): a fisheye lens, colour fringing, a wine-dark vignette and a slow
  Cinemachine sway on the live camera, at half strength with 3 glasses, three quarters with 4, full
  with 5+, ramped in over 3 s and out over 6 s. The HUD shows a pip per glass beside the balance and
  "Tipsy" from the third. At blackjack the drunk player's own unsettled hand labels show two totals,
  "17 or 21?" (the real one and a decoy within 4, never a bust, fixed per hand until a card is added),
  and the turn banner drops the total; bust and settled hands, the dealer and other players are honest.
  The upside: net wins pay extra while drunk, **20%** at blackjack and **10%** at poker (§7.2).

### 4.4 Ambient patrons
- 10 NPC cats wander between points of interest: the bar, near tables, lounge cushions. The
  NavMesh lets them hop up and stroll across table tops now and then.
- At a table they stop and "watch", and they react when a big win happens nearby.
- **Crowds around winners** (`WinnerCrowd`): once a seated player is **50 chips up** at their current
  table or machine, a patron walks over and watches from behind the seat. More winnings draw more
  watchers: 1 at 50, 2 at 150, 3 at 300, 4 at 500, 5 at 800, 6 at 1,200. At least two patrons keep
  wandering. Watchers hop and meow at every winning round and drift off one by one when the streak
  drops below the mark or the player gets up. Winnings (`SessionWinnings`) are the net of every
  round since sitting down. The owner networks them, and the master client, which simulates the
  patrons, reads them.
- They're networked so everyone sees the same cats in the same places (§9.5).
- Seats (stools, chairs, cushions) are holes in the NavMesh, so a patron leaving one starts its path
  from the nearest NavMesh point. After 3 failed paths in a row, or after 5 s standing off the
  NavMesh without moving, it hops down onto the nearest NavMesh point. The first failed path logs a
  `[Patron]` warning.

### 4.5 NPC budget (`NpcBudget`)
- The hall should feel about as busy at any player count, so the fewer players, the more NPCs. A
  master-client object sets the budget: NPCs = clamp(20 − players, 5, 19). That is today's full set
  (10 patrons plus about 9 seat cats, five tables at an average of 1.72) plus one, less the players,
  and never fewer than 5.
- The budget is split between patrons and seat cats in the full set's 10 : 9 proportion:

  | Players | NPCs | Patrons | Seat cats (average) |
  |---|---|---|---|
  | 1 | 19 | 10 | 9 (scale 100%) |
  | 5 | 15 | 8 | 7 (78%) |
  | 10 | 10 | 5 | 5 (56%) |

- The budget changes 10 s after the last join or leave, so a quick rejoin doesn't make cats come and go.
- **Patrons** beyond the budget (by their order under `Patrons`) walk out of the entrance (`CasinoDoor`)
  and wait there out of sight (`Away`, networked, so late joiners agree). When the budget grows they
  step back in through the entrance. A leaving cat can't be pawed or sent to watch a winner.
- **Seat cats:** a table on "random" multiplies its rolled target by the seat scale (rounded up or down
  at random, so tables average the scaled count) when it next rolls (every 30–80 s). Seated cats are
  never pulled out mid-round; a table that wants fewer simply lets cats leave between rounds and doesn't
  refill. A table set to an exact number of NPCs keeps it.

---

## 5. Blackjack (primary game)

### 5.1 Tables
- **2 tables, 5 seats each.** The dealer sits on the far side.
- Limits: **min bet 10, max bet 500** per hand.
- Each table has its own settings (§5.7).

### 5.2 House rules (standard Vegas set)

| Rule | Value |
|---|---|
| Decks | 6-deck shoe |
| Reshuffle | Cut card at ~75% penetration. Shuffle between rounds when it's reached |
| Dealer on soft 17 | **Stands** (S17) |
| Blackjack payout | **3:2** |
| Dealer hole card | US style: dealer peeks for blackjack with an Ace or 10-value up card |
| Double down | On any first two cards |
| Double after split | Allowed |
| Split | Pairs of equal value (e.g. K+Q), up to 3 splits (4 hands) |
| Split aces | One card each, no resplit. 21 after a split is not a blackjack |
| Insurance | **Not offered** |
| Surrender | **Not offered** |
| Side bets | None |
| Push | Bet returned |

### 5.3 Round flow

```
WaitingForPlayers ─(≥1 seated player or NPC)─► Betting
Betting ─(all seated have bet or passed, or timer expires)─► Dealing
Dealing ─► DealerPeek ─(dealer blackjack?)─► Settlement
                      └─(no)─► PlayerTurns
PlayerTurns (seat 1→5, each hand in order) ─► DealerTurn
DealerTurn (skipped if every player hand busted) ─► Settlement
Settlement ─► Cleanup ─► Betting (or WaitingForPlayers if table empty)
```

- **Betting**: seated players place a bet with chip buttons, then press **Deal** (lock in).
  Someone who doesn't bet sits the round out. With the timer on, betting ends when it runs out. With
  it off, betting ends when every seated human has locked in or passed.
- **Dealing**: two cards to each active seat and two to the dealer, one of them face down, in real
  casino order.
- **Player turns**: the active seat and hand are highlighted, and actions are Hit, Stand, Double
  and Split as allowed. A hand ends automatically on 21 or bust.
- **Dealer turn**: the hole card flips, and the dealer draws to 17 (stands on soft 17).
- **Settlement**: results appear per hand (Win / Lose / Push / Blackjack / Bust), chips float to
  the winner or the dealer, and cats react.
- **Cleanup**: cards float into the discard tray. Short pause, then the next round.

### 5.4 Joining and leaving
- **Sitting**: walk up to a free stool and press E. If a round is running, you're "waiting" until
  the next betting phase.
- **Standing up mid-round**: your unfinished hands auto-stand and settle normally. You still get
  paid when settlement happens (§9.4).
- **Disconnect mid-round**: same as standing up. The seat frees after settlement.
- **Seat taken by an NPC**: a human can take an NPC's seat between rounds. The NPC hops off and
  wanders away.

### 5.5 Presentation: floating cards and chips
Since cats have no hands, everything moves by **magic**:
- Cards rise out of the shoe, glide along an arc to their spot and **flip in mid-air** with a soft
  sparkle trail.
- Chips float from the player's stack to the betting circle while betting, and to the winner or
  the dealer rack at settlement.
- The dealer cat "directs" with a head turn toward the target seat. That's a look-at, not a new
  animation.
- The whole dealing animation for one round must fit within ~3 s so the pace stays good.
- Late joiners and spectators arriving mid-round see the current layout straight away, without
  replaying animations.

### 5.6 NPC seat players
- **Random** (the default): NPC cats come and go between rounds on their own. Every 30–80 s the
  table picks a new target of 0–4 NPCs (mostly 1 or 2), and cats join or leave one at a time, 3–10 s
  apart. A **number** in the table settings instead keeps exactly that many. One seat always stays
  free, and a human wanting a seat always takes priority.
- NPCs walk in from the entrance and hop onto their stool. Their place on the path comes from a
  networked arrival timer per seat (`NpcSeating`), so every client, including late joiners, shows
  them in the same spot. They only play once seated. Leaving NPCs get up, hop down and walk out
  through the entrance, and cross a table top now and then on the way. The authority seats the NPCs
  that are already there at session start without the walk.
- NPCs play **basic strategy** (the strategy table for the §5.2 rules) with short, random "thinking"
  delays of 0.6–1.5 s.
- NPC bankroll is effectively unlimited. They bet random amounts between min bet and 5× min bet and
  don't appear on the leaderboard.
- NPCs react with the same happy/angry animations as players.
- **Pawing a seated NPC** (any game table) makes it hiss like a pawed patron (§4.4), stand on the
  seat, then hop down and run to the entrance at the patrons' flee trot. It leaves the seat the way
  a player leaving mid-round does: poker folds its hand, a blackjack hand plays out on its own, and
  bets placed during betting are withdrawn. That seat takes no new NPC for 60 s. A networked scare
  count per seat tells every client to play the run; late joiners just see the empty seat.

### 5.7 Table settings
Only **players seated at that table** can change its settings, using a small settings panel in the
seated UI. Changes apply from the next betting phase, and everyone at the table sees a chat notice
("Mochi set turn timer to 30s").

| Setting | Options | Default |
|---|---|---|
| Turn timer | Off / 10 / 20 / 30 / 60 s | 20 s |
| Betting timer | Off / 10 / 15 / 30 s | 15 s |
| NPC players | Random / 0–4 NPCs | Random |

When the turn timer runs out, the hand **auto-stands**. When the betting timer runs out, anyone
who didn't lock in a bet sits the round out.

### 5.8 Reactions and feedback
| Event | Player cat | Dealer cat | Other |
|---|---|---|---|
| Blackjack | Celebrate + meow | Annoyed | Gold sparkle burst on the cards |
| Win | Happy | — | Chips float to the player |
| Bust | Angry/sulk | Smug idle | Cards dim |
| Push | — | — | "Push" label |
| Dealer blackjack | Angry | Celebrate | |
| Big win (≥ 250 net) | Celebrate | — | Nearby patrons turn and cheer |
| Huge win (≥ 500 net in a round, or a top-tier slot win) | Lights a victory cigar for 5 minutes: a glowing ember and smoke that breathe on a 4 s cycle, seen by everyone including late joiners (`CatCigar`, `PlayerCat.CigarUntilTick`); a later huge win extends it | — | Chat: "<name> lights a victory cigar."; toast for the winner on a fresh light |

- **Result pause**: every table game waits `GameTables.ResultPause` (1.5 s) between the last card
  (or the ball settling) and the result, so players can read the cards before labels, payouts and
  reactions give it away. The table authority waits, so all clients stay in step:
  - blackjack: after the dealer's final card;
  - baccarat: after the last card;
  - roulette: after the ball lands (the spin phase is 1.5 s longer than the animation);
  - poker: the hands are turned over first and the pot is awarded 1.5 s later (§13.2).
- **Staggered reactions**: each cat at the table reacts after its own random 0.05–0.7 s delay (the
  dealer a little later), so a table doesn't hop in unison. Patrons cheering a big win are staggered
  the same way.

---

## 6. Spectating

You can watch a friend just by walking up to them, and this works the same way for every game.

- **World view**: every card, chip and animation is networked (§9), so a spectator standing nearby
  sees exactly what's on the table.
- **Spectator panel**: standing within ~3 m of a seated player (and not seated yourself) brings up
  a small UI Toolkit panel for the nearest player you're facing:
  - name and fur-color swatch
  - current bet(s) and hand total(s) (e.g. "Soft 17", "Bust")
  - whose turn it is and the turn timer
  - balance
- **Hidden information stays hidden.** In future games with private cards (poker), spectators
  **don't** see a player's hole cards.
- Spectators are passive. They can't act, bet or change settings for the player they're watching.

---

## 7. Economy

### 7.1 Chips
- A new profile starts with **1,000 chips**.
- The balance is **saved locally** (JSON in `Application.persistentDataPath`) and loaded on launch.
  Since the client is trusted, the saved file is the source of truth for that player.
- The balance is also networked so the leaderboard and spectator panel can show it (§9.3).
- Chip denominations in the betting UI: **5, 10, 25, 100, 500**. With min bet 10, the 5 chip is
  only there for making up odd amounts.

### 7.2 Going broke: "Cat Welfare", and earning chips
- If your balance drops below the minimum bet (10) and you have no active bets, the **cashier cat**
  offers a **500-chip Cat Welfare refill**, at most **once an hour**. While it's cooling down, the
  cashier's prompt says how long is left. There's no debt tracking. It's a friends game.
- Small activities earn chips too (`ChipRewards`):
  - **Pawing** a knockable prop or a wandering patron: **10 chips**, at most once per object every
    **5 minutes**. Only a paw that lands pays (a patron still spooked from an earlier paw ignores
    it, so it doesn't). Other players' cats and the NPCs seated at tables can't be pawed.
  - **Topping the cat wall**: **100 chips**, at most once every **10 minutes**.
  - **Tipsy bonus** (`TipsyBonus`): while drunk (§4.3), a net win at a skill game pays extra
    (rounded down, at least 1 chip), shown as "+N" with a toast. No cooldown; it's bounded by the
    wins themselves. The rates live in `TipsyBonus.RateFor`:

    | Game | Bonus | Why |
    |---|---|---|
    | Blackjack | **20%** | the hard game |
    | Poker (pots won) | **10%** | skill game |
    | Baccarat, roulette, slots | none | pure chance |
 A glass of wine costs 20 chips.
- A "+10" pops up beside the balance with a coin sound; the cat wall and welfare show a bigger
  "+100"/"+500" with a chime. A paw on cooldown pays nothing and says nothing.
- The balance is client-side, so these cooldowns are the only guard against farming. They're saved
  in the profile as UTC times (expired ones are dropped), so a reload doesn't reset them. Paw
  cooldowns are keyed by the object's scene path, e.g. `CasinoSea/Props/Glass_3`.

### 7.3 Chip gifting
- Stand near another player, press **E**, then choose **Gift chips** and pick an amount (chip
  buttons plus a text field).
- The sender's balance goes down and the receiver's goes up. Both get a toast, and the room chat
  logs it ("Mochi gifted 200 chips to Luna").
- You can't gift chips that are sitting in an active bet.

### 7.4 Leaderboard
- An in-world board on the wall (world-space UI Toolkit panel), plus a **Tab** overlay.
- "Top cats": it ranks everyone **currently in the room** by **chips earned this session**
  (`PlayerCat.SessionNet`, networked, kept by the owner): every game's net result plus paw, cat-wall and
  tipsy-bonus rewards; the Cat Welfare refill doesn't count. Highest first, ties by name (`SessionRanking`, shared
  by the overlay, the wall board and the nameplates).
- The overlay shows a podium for the top three, rows 4–10 (4–6 on a phone) with an up/down arrow for 3 s after a
  rank change, and your own row pinned under a "···" gap when you're further down. Until anyone has won or lost
  anything it reads "Earn your first chips". The wall board shows the top ten the same way.
- The #1 cat wears a crown over its nameplate while its net is above 0.
- NPCs aren't on it.

---

## 8. UI (UI Toolkit)

Everything is built with UI Toolkit (UXML + USS) using its built-in controls and one simple shared
theme. No custom UI art is made. Some UI is world space: the leaderboard, nameplates and spectator
panel, using world-space UI Toolkit panels where Unity 6.x supports them, with screen-space
fallbacks.

| Screen / widget | Contents |
|---|---|
| Main menu | Name field, fur color palette, cat preview, **Play**, Quit |
| Connecting | Spinner, status text, error + retry |
| HUD | Balance, interact prompt ("[E] Sit"), toasts |
| Nameplates | Name above each cat (fades out with distance) |
| Blackjack seat UI | Chip buttons, bet amount, Deal/Clear, Hit/Stand/Double/Split, turn timer ring, hand totals, result labels, settings button |
| Table settings | Turn timer, betting timer, NPC players (random or 0–4); at poker and baccarat also the personal Coach toggle |
| Rules / Coach (poker, baccarat) | Rules page (scrollable), coach card with live advice and win-chance bar |
| Spectator panel | See §6 |
| Chat | Scrolling log (system + player messages), input line |
| Leaderboard overlay | Hold Tab |
| Gift dialog | Recipient, amount, confirm |
| Pause menu | Volume sliders, mouse sensitivity, camera auto-follow toggle, Leave |

---

## 9. Multiplayer architecture (Fusion 2, Shared Mode)

### 9.1 Session
- **One fixed room**: every client calls `StartGame` in `GameMode.Shared` with the same hard-coded
  session name (`CasinoMares-Main`). The first to join creates the room.
- **Region pinned to `eu`** in code (`ConnectionManager.Region`). `PhotonAppSettings.asset` is
  gitignored because it holds the App ID, so the region can't live there. Without pinning, friends in
  different places could land on different servers and never see each other.
- Max **10 players**. NPCs don't count toward that.
- **One scene (`Casino`).** The main menu is a UI overlay on top of the casino, shown with an overview
  camera. Pressing Play starts the session on the already-loaded scene. Leaving or disconnecting reloads
  the scene.
- **Testing**: Unity's Multiplayer Play Mode package runs extra virtual players from one editor. Each
  virtual player gets its own profile file.

### 9.2 Authority model (as built)
Shared Mode has no host, so authority is split like this:

| Object | State authority | Why |
|---|---|---|
| `PlayerCat` (one per player) | That player | Own movement, appearance, balance, seated flag |
| Game tables: `BlackjackTable`, `RouletteTable`, `BaccaratTable`, `PokerTable`, `SlotMachine`, and the bar stools' `TerraceTable` + `WineBar` (scene objects) | **Master client.** `MasterClientObject` is set and `DestroyWhenStateAuthorityLeaves` is cleared, so authority moves when the master leaves | One authority runs each round's state machine, shoe, NPC seats and timers |
| Ambient patrons (`PatronBrain`, scene objects) | Master client | Walks NavMesh paths inside `FixedUpdateNetwork`. Everyone else sees the `NetworkTransform` |
| Staff, dealer and NPC seat cats | Nobody (local visuals) | Driven locally from networked table state |

Player input to a table (sit, bet, hit, raise...) is an **RPC from the seated player to the table's
state authority**. The authority only checks that the action is legal in the current state; it
doesn't check for cheating.

**Movement:** the player cat uses Fusion's `NetworkCharacterController`, moved in
`FixedUpdateNetwork`. A plain `NetworkTransform` plus a `CharacterController` jittered, and moving in
`Update` gets overwritten by render interpolation.

### 9.3 Networked state
Everything a table needs lives in fixed-capacity `NetworkArray`s on the table: seat occupants and
players, per-seat bets, hands and cards (as `byte`s, 0-51 with 255 = empty), dealer cards, phase,
`TickTimer`s, round ids and settings. Shoe seeds and draw positions are networked for blackjack and
baccarat, so a new authority continues with the same shoe. Clients render purely from this state:
new cards fly in, and a late joiner sees the current layout straight away.

**Hidden information (poker):** hole cards and the deck seed exist only in the authority's memory.
Each player receives their own two cards through a targeted RPC (`[RpcTarget] PlayerRef`). Cards
become networked only for hands that reach a showdown. If authority changes mid-hand, the new
authority can't know the hole cards, so the hand is voided and every contribution is refunded.

### 9.4 Chip accounting (trusted, no double-spend bugs)
Each client keeps its own balance and derives every change from networked state, keyed by round id:
- **Blackjack / roulette / baccarat:** when the local player's wager on a round grows (bet, double,
  split), the difference leaves the balance. When the round is settled, the payout is credited once
  per round id. Roulette and baccarat also refund if bets shrink while betting is open (clear, or
  leaving the seat).
- **Slots:** charged when a new spin id appears for the local player, paid when it settles.
- **Poker:** the buy-in (and any rebuy) leaves the balance when a new seat session appears. The last
  stack the client saw comes back when it's no longer seated, and also when the client quits or
  disconnects while seated. Chips already in the pot stay there.
- If the player leaves a blackjack seat mid-round, their hands auto-stand and their client still
  gets paid at settlement.
- **Gifting:** the sender takes the amount out of its balance, then sends an RPC to the receiver's
  `PlayerCat` state authority, which adds it.
- The profile is saved to disk on every balance change.

### 9.5 Other sync
- **Chat:** an RPC from the sender's `PlayerCat` to everyone. Late joiners don't see earlier
  messages. Table notices (settings changed, shuffles, poker results) come from the table authority.
- **Animations:** nothing animation-specific is networked. Speed comes from the interpolated
  position, sitting from `PlayerCat.IsSeated`, and reactions are triggered on every client from the
  same networked results.
- **Timers:** `TickTimer`s, so every client shows the same countdown.

### 9.6 Code structure
- `Assets/CasinoMares/Scripts/Rules` (**CasinoMares.Rules**, no engine references):
  - blackjack rules and basic strategy
  - shoe
  - roulette, baccarat tableau, slot paytable
  - poker hand evaluator and side pots

  EditMode tests in `Assets/CasinoMares/Tests/Editor` (95 tests).
- `Assets/CasinoMares/Scripts/Runtime` (**CasinoMares**): one folder per game (`Blackjack`,
  `Roulette`, `Baccarat`, `Poker`, `Slots`), each with a networked table, a view and a seat
  interactable. The rest is shared:
  - `Core`: input, profile, interactables, the game-table interface, chat log, pixel font
  - `Player`: cat, movement, seating, interaction, reactions
  - `Npc`, `Social` (cashier, gifting, wall board), `Audio`, `UI`, `Networking`
- Every game implements `IGameTable`. Seating, the seat camera, the spectator panel and the cashier's
  "chips in play" check work for all games through it.
- `Assets/CasinoMares/Scripts/Editor/CasinoSceneBuilder.cs` builds the whole scene and all generated
  assets. Run it from **Casino Mares ▸ Setup ▸ Rebuild Casino (Assets and Scene)**.

---

## 10. Art, animation and audio assets

### 10.1 Cat model
**Decision: use "Black Cat Animated" by WDallgraphics** (a Unity Asset Store pack the project owner
bought). It's in `Assets/ThirdParty/BlackCatAnimated/` (model, clips and textures only; the pack's
demo scene, script, prefab and Standard-shader material were left out):
- A realistic black cat, Generic rig (74 bones, ~4.6k vertices), about 0.36 m tall, facing +Z. The
  prefabs scale it **1.6×**.
- 17 clips, one FBX each (`cat@<clip>.FBX`): **idle, walking, trotting, running, jumping, sitting,
  getup**, sneaking, meow, licking, eating, attack, attack2, hited, died, turnleft, turnright.
  `CasinoSceneBuilder` sets their import settings: a shared avatar, no root-motion node, and an
  `AnimalSound` event on idle.
- The clips move the whole rig forward through its `GeneralController` root (walks drift, sitting
  starts 0.3 units forward). `CatRigAnchor` pins that root back over the cat's origin after the
  Animator runs, keeping its height and rotation, so every clip plays in place and `CatMovement` alone
  moves the cat. (Unity's Generic root motion with a baked root rotation lost the rig's Z-up
  correction after reimports and pitched the cats 90°, so this stays explicit.)
- Locomotion speeds come from measured planted-paw speeds (walking 0.31, trotting 0.73, running
  1.18 m/s at scale 1). Walking (1.4 m/s) plays the trot at 1.2×, sprinting (2.6 m/s) plays the run at
  1.4×, so both cadences match. Speeds and jump tuning live in `CatMotion`.
- **Jump**: the clip is a crouch, a short leap (paws off the ground from 36% to 60%) and a recovery.
  The Jump state starts at the push-off and stretches the leap over the real air time.
- **Paw swipe**: a second Animator layer masked to the right foreleg plays the reaching paw from the
  pounce clip, while `CatReactions` rears the cat up on its hind legs.
- **Emotes** (the Q wheel): Meow, Sit & purr, Groom, Happy hop, Pounce, Butt wiggle, Hiss and Play
  dead. Meow, purr and groom sit down first; moving cancels any emote. Each has its own sound.
- Fur: the pack ships only the black texture (plus a normal map). Midnight uses it as is. The builder
  makes the other five furs by recoloring the dark, unsaturated fur pixels and keeping their detail.
  Eyes, teeth, claws, mouth and paw pads keep their colors. The results are
  `Assets/CasinoMares/Art/Textures/CatFur_*.png`, with materials `Art/Materials/CatFur_*.mat`.

The earlier cat, from the owner's "Low Poly Animated Animals" pack, is still in
`Assets/ThirdParty/LowPolyAnimatedAnimals/` but nothing uses it now; it can be deleted.

Other assets from the owner's collection that may be useful later: PolygonPrototype (graybox kit),
CasualGameSounds (UI sounds), HighlightPlus (outline for interactables), Feel (game feel/juice) and
MK Toon (stylized shader).

The Sketchfab research below is kept for reference.

**Policy: we don't buy assets.** Use free models only, preferably from Sketchfab (downloaded through
MCP for Unity's Sketchfab import with the connected API key). If no free model works, the project
owner will get an asset.

**Candidates found on Sketchfab** (free download, CC Attribution). I imported each into
`Assets/_AssetEval/` and read its animation clips:

| Model | Clips | Faces | Verdict |
|---|---|---|---|
| [cat animation](https://sketchfab.com/3d-models/cat-animation-9c5c0721cc3b4c068706085a2b3e0dc8) (Slava251114) | `A_idle`, `A_walk`, `A_run`, `A_jump_idle_noloop`, `AtoB` (stand→sit), `B_idle` (sit idle), `B_wash`, `A_eat_no_loop`, `C_Sleep`, `A_nail_pole_*`, `A_hang`, `A_punch_R` | ~900 | **Recommended.** The only free cat with idle, walk, run, jump **and** sit. One grey texture, which tints well into other fur colors |
| [Tuxedo Cat Animated 2.0](https://sketchfab.com/3d-models/tuxedo-cat-animated-20-783fcb78b55b4394a212c2b6392e1113) (DreamNoms) | IdleNorm, WalkCycle, IdleTailSwoosh, SitDown, IdleSit, StandUp | ~6.4k | Cute, but no run or jump. Could be an NPC-only cat |
| [Somali Cat Animated 1.2](https://sketchfab.com/3d-models/somali-cat-animated-ver-12-e185c3fd92b64c32b4515a32b29252fc) (DreamNoms) | Idle, WalkClean, SitDown, SittingIdle, StandUp | ~7.6k | Same limits as Tuxedo |
| [Cartoon Cat](https://sketchfab.com/3d-models/cartoon-cat-4a738eaed9d34547a3f5a705a0e37c6a) (baydinman) | 4 idles, then arm/pistol/sword attack, hit and death | ~4.1k | **Not usable.** Combat animations only, no locomotion |
| Assemblrworld86 "Stylized cats" (Persian / Siamese / Ragdoll) | Idle, Sit, Walk, Run | ~20k | Not free (paid store items, not downloadable) |

**How the recommended cat's clips map to §3.3:**

| Need | Clip | Notes |
|---|---|---|
| Idle / walk / run | `A_idle`, `A_walk`, `A_run` | Locomotion blend tree |
| Jump | `A_jump_idle_noloop` | Also used for hopping onto stools |
| Sit down / stand up | `AtoB` / `AtoB` reversed | Reverse playback (speed −1) for standing up |
| Sit idle | `B_idle`, `B_wash` | Seated at tables. Wash is an idle variant |
| Happy / celebrate | *(none)* | Fake it: a quick jump plus a meow sound and a sparkle effect |
| Angry / sulk | `A_punch_R` or procedural | Paw swipe, flattened-ear head tilt, grumble sound |
| Ambient | `A_eat_no_loop`, `C_Sleep`, `A_nail_pole_*` | Patrons and the bartender area |

glTFast imports these as glTF assets. Set the import to **Mecanim** animation (not Legacy) so an
Animator Controller can drive them.

**Evaluation checklist** (import each model and check it in Unity before choosing):
- It includes the must-haves from §3.3: idle, walk, run, jump, **sit idle**.
- It includes, or can take, reaction animations (happy, angry, meow). Missing ones can be faked with
  procedural squash/stretch, head bobs and sound.
- It imports cleanly as a Generic rig in URP (materials convert, scale is sane).
- Its texture can be recolored or tinted into the fur palette in §3.2.

**Licensing:** CC Attribution requires credit. Every third-party asset goes into `CREDITS.md` and an
in-game credits screen (title, author, link, license).

### 10.2 Other assets (as built)
- **Playing cards**: no art needed. Faces, backs, the roulette wheel and slot reels are painted into
  textures at runtime with a built-in 5×7 pixel font (`PixelFont`, `CardFaces`,
  `RouletteWheelTexture`, `SlotSymbols`).
- **Sounds**: [Kenney Casino Audio](https://kenney.nl/assets/casino-audio) and a subset of
  [Kenney Interface Sounds](https://kenney.nl/assets/interface-sounds), both CC0, in
  `Assets/ThirdParty/Kenney/` with their license files.
- **Cat voices and prop sounds**: synthesized at startup (`CatVoice`): meows, a yowl, purr, hiss,
  chirp, chatter, the paw swish, glass clinks and thuds for knocked props, and the roulette ball's
  ticks. There's no CC0 cat sound in these packs.
- **Furniture and props**: primitives built by the scene builder, dressed with generated textures.
  Candidates for a later art pass are the owner's PolygonPrototype / Low Poly Ultimate Pack, or CC0
  kits.
- **Pushable props** (`Knockable`): 19 glasses, mugs, a bottle and yarn balls on the tables, bar,
  lounge table, cashier desk and floor. Whoever swats one takes State Authority and simulates it;
  `NetworkTransform` syncs the Rigidbody to everyone else. It returns to its spot 12 s after it comes
  to rest.
- **Music**: none yet.
- **VFX**: not yet. Reactions are hops, paw swipes and sounds.

All third-party assets go in `Assets/ThirdParty/<Vendor>/` with their license files. See `CREDITS.md`.

---

## 11. Audio design (as built)
- **Table**: card slide when a card flies, a soft tap when it lands, shuffle on a new shoe or hand,
  and chip stack sounds on bets and payouts. All 3D, positioned at the table, so nearby tables sound
  louder.
- **Cats**: an occasional meow from the idle animation's mouth-open event, a meow on every happy
  hop, a sound for each emote, and a swish on paw swipes. Knocked props clink or thud when they hit
  something.
- **UI**: click on every button, a pluck for toasts, a tick for chat lines and for the last five
  seconds of your turn, and confirmation or error jingles for your own wins and losses.
- **Settings**: master volume slider in the pause menu (saved in PlayerPrefs).
- **Not yet**: music and separate music/SFX sliders.

---

## 12. Roadmap and status

M0 to M3 and all the M5 games except craps are implemented as a first draft (2026-09-30). Everything
below was checked in the editor. Blackjack (seating, payouts, spectator panel, wall board, gifting)
and poker (private hole cards, showdown reveal, cash-out on leave and on quit) were also checked in a
two-client session (editor + macOS build bot).

| Milestone | Status |
|---|---|
| M0 Foundations | Done: menu, profile, Shared Mode room (EU), cat movement, camera, nameplates |
| M1 Blackjack core | Done: rules + tests, networked table, floating cards/chips, seat camera, seated UI, chip accounting |
| M2 Blackjack complete | Done: two tables, settings/timers, NPC seats (basic strategy), leave/disconnect handling, spectator panel, reactions |
| M3 Social and casino life | Done: chat, leaderboard (Tab + wall board), gifting, Cat Welfare, dealer/cashier/bartender, 5 patrons, lighting and post-processing, sounds |
| M4 Polish | Partly done: pause menu with volume and sensitivity, macOS build tested. Still to do: Windows build, VFX, music, art pass, tuning |
| M5 Roulette | Done: European wheel, straight/outside bets, history, NPC bettors |
| M5 Texas Hold'em | Done: no-limit, blinds, side pots, private hole cards, NPC bots, buy-in/rebuy/cash-out |
| M5 Slots | Done: 3 machines, weighted reels (~98% RTP), paytable |
| M5 Baccarat | Done: Punto Banco with the full tableau, 5% commission, results road |
| M5 Craps | Not started |

**Testing helpers**
- `Assets/CasinoMares/Tests/Editor`: 95 EditMode tests for all rules.
- **Bot mode:** start a build with
  `-bot -profile bot -botName BotCat -botTable 2 -botSeat 0 -screen-fullscreen 0`. It connects, sits
  at a blackjack table and plays basic strategy, which gives you a second player for multiplayer
  checks.
- `-profile <name>` keeps a separate profile file for any test instance.

---

## 13. Games beyond blackjack (as built)
All of them reuse seating, the seat camera, spectating, chip accounting, settings by seated players
and the floating cards.

### 13.1 Roulette
- **European single-zero** wheel (0-36), no en prison or la partage.
- **The wheel is a cat head** (`CasinoSceneBuilder.RouletteCatHead`): two triangular wooden ears rise
  from the round rim of the wheel's bowl on the croupier's side (40° either side of centre, 20 cm tall,
  leaning back 15°), so from the seats the wheel reads as a cat's head. The table top is the plain
  rectangular felt and rim; the ears have no collider, so the colliders and NavMesh are unchanged.
- 6 stools and a croupier cat. There's one shared betting window (15/20/30/45 s, or shorter if
  everyone presses Ready). Then the wheel spins for 7.5 s and the ball lands in the networked result.
- **The wheel** is modelled, not painted flat: a wooden bowl with a ball track, a sloped apron with
  eight brass diamonds, and a rotor with 37 brass frets between the pockets and a four-armed turret
  (`RouletteWheelGeometry`, lathed at runtime and painted with `RouletteWheelTexture`). A collider
  keeps cats off the wheel, and the NavMesh routes NPCs around it.
- **The ball** (`RouletteBallPath`) is worked out backwards from the result pocket, so every client
  watches it land in the same one:
  - the croupier flicks it out of the last pocket onto the track, where it circles about 8 times
    against the rotor while slowing down;
  - it spirals down the apron, sometimes clipping a diamond, and skids along the rotor rim;
  - it hops across a few pockets (hop lengths follow their heights, as under gravity, slightly
    slowed), then rattles to a stop.

  The randomness comes from the round id, so it's the same everywhere. A local clock smooths the
  networked timer, and every strike ticks (a synthesized `BallTick`).
- **Wheel close-up**: when the spin starts, seated players' cameras blend to a camera over the
  wheel and the betting board steps aside. The view returns 1.5 s after the result is called.
  This is personal (*Wheel close-up* in the table settings, on by default). The seat camera's look
  is kept while the close-up shows.
- Bets: straight numbers (35:1), red/black, odd/even, 1-18/19-36 (1:1), dozens and columns (2:1).
  Split, street, corner and line bets aren't in yet. Minimum chip 5, max 500 per spin.
- The UI is a clickable layout with your bets as badges, winning spots highlighted, and the last 12
  results.
- **Everyone's bets are visible while betting**, as at a real table where each seat plays its own
  chip colour:
  - every other seat's bets show as a dot in that seat's colour on each spot;
  - a legend above the layout lists each seat's colour, name (or "NPC") and total, and who is
    ready;
  - pointing at a spot lists who has what on it.

### 13.2 Texas Hold'em
- 6 seats, no-limit, blinds 5/10, 10/20 or 25/50, turn timer 15-60 s. Up to 4 NPC bots play a simple
  hand-strength strategy with the occasional bluff.
- You buy in for up to 500 when you sit down (minimum 100). You can rebuy when busted, and standing
  up cashes out your stack.
- **Hole cards are private**: they're sent only to their owner by a targeted RPC. Everyone else
  sees card backs until a showdown.
- Side pots and split pots are handled, with odd chips going to the first winner clockwise from the
  button.
- If the master client leaves mid-hand, the hand is voided and all bets are returned.
- **Showdown in two steps**: the board runs out and every live hand is turned over ("Showdown!").
  After the result pause the pot is awarded. The award is computed from networked state only (the
  shown cards, the board and the contributions), so a new master can finish a showdown the old one
  started.
- **Teaching mode** (Rules button, and *Coach* in the table settings, which is personal, saved
  locally and off by default):
  - **Rules**: goal, how a hand goes, every action, the hand rankings with examples, and beginner
    tips.
  - **The coach card** only uses what the player can see. It gives:
    - the street and what comes next;
    - the starting-hand tier by the Chen formula;
    - the best made hand, what it beats and what beats it, with a warning when it's all on the board;
    - flush and straight draws with their outs (the rule of 4 and 2);
    - pot odds, and a simulated win chance against the live opponents (2,500 run-outs, off the main
      thread).
  - **Suggestions**: on your turn the coach suggests fold, check, call, raise (with a size) or all in
    by weighing the win chance against the price. The matching button is highlighted and the raise
    slider starts at the suggested size. The math is `PokerCoach` in the Rules assembly, with tests.

### 13.3 Slots
- 4 machines on the east wall, each a single-seat table: two three-reel machines and two Yarn
  Tumbles. The seated cat paws the spin button.
- **Three reels**: the authority rolls weighted reels (fish, milk, yarn, mouse, paw, 7) and every
  nearby client sees the same reels stop. When the first two match, the third slows down, ticking
  and glowing, before it stops.
  - Paytable: three of a kind pays more the rarer the symbol (fish 6×, milk 10×, yarn 12×, paw 24×,
    mouse 50×, 7 120×), two paws pay 4×, one paw hands the bet back (1×, "Bet back" on the HUD).
    Reel weights fish 30, milk 20, yarn 15, paw 11, mouse 8, 7 4. About 10% of spins win and 29% return
    the bet, at 96.1% RTP; tests check 95.5–97% and that rarer symbols pay more.
- **Yarn Tumble**: a 5×5 grid of yarn balls (blue, green, pink), fish, mice and golden paws. A group
  of 5 or more touching side by side pays (1× to 2× for five, a quarter more for each one more, up
  to 10). The cat bats the winners away, the rest fall, and new symbols drop in, a quarter of them
  as a copy of the one they land on, so wins tend to chain. Each chain raises the multiplier by a
  half: ×1, ×1.5, ×2, ×2.5, ×3, ×3.5. The authority only picks a seed; every client plays the same
  cascade out from it. About a third of spins win and one in eight chains at least twice, at about
  98% RTP, tested (sampled).
- **Celebrations by size**: under 10× the bet, a chime and a hop; 10× or more, coins burst from the
  tray, a BIG WIN banner counts the payout up and nearby patrons hop; 50× or more (MEGA WIN), also
  a bell the whole room hears and a line in the chat.

### 13.4 Baccarat and craps
- **Baccarat (Punto Banco)**: 5 seats, 8-deck shoe, the full drawing tableau. Player pays 1:1, Banker
  0.95:1, Tie 8:1, and Player/Banker bets push on a tie. There's a "road" of recent results.
- **Teaching mode**: the same Rules button and personal coach as poker.
  - **Rules**: card values, the deal and the full third-card table, payouts, and the odds with house
    edges.
  - **While betting**: the coach suggests Banker and highlights it, warns about Tie bets, and points
    out that streaks in the road don't predict anything.
  - **During the deal**: it walks through every step, e.g. "7 + 8 = 15 → 5", naturals, why the Player
    draws or stands, and which row of the Banker's table applies.
  - **After the coup**: it explains what the result did to each of your bets.
- **Craps**: not implemented yet.

---

## 14. Open questions
1. **Craps**: still wanted?
2. **Art pass**: which of the owner's packs (e.g. PolygonPrototype, Low Poly Ultimate Pack) should
   replace the primitive furniture? The realistic cats contrast with the simple props.
3. **Roulette inside bets** (split, street, corner, line): worth adding?

### Resolved
- Jump: players can jump freely with **Space**, with a real jump animation from the Black Cat pack.
  The camera only follows 15% of the jump's height.
- Teaching mode for poker and baccarat: a Rules page plus a personal live coach (§13.2, §13.4).
- NPC seat players: random by default, walking in and out through the entrance (§5.6).
- Crowds gather around players who are 50+ chips up (§4.4).
- Blackjack card counting works as in a casino: the 6-deck shoe persists across rounds and is
  reshuffled automatically at the 75% cut card (or if it ever runs out).
- Emote wheel: hold **Q**; eight emotes with their own sounds. Patrons emote now and then too.
- Paw swipe: **left click** swats props, which everyone sees fall.
- Roulette: **European** single-zero.
- Profiles: **one per machine**.
- Chat history for late joiners: **no**.
- Asset purchases: **none**. Free assets only.

---

## 15. Web build (WebGL, experimental)

The `webgl` branch runs the game in a browser, including phones and tablets held sideways. Browser
players join the same room as the desktop builds.

### 15.1 Build and hosting
- **Casino Mares ▸ Web ▸ Build (Desktop and Mobile)** (`WebBuild`) switches to WebGL and builds
  `Builds/WebGL` twice:
  - once with DXT textures for desktop browsers;
  - once with ASTC textures, which phones decode in hardware. It ships as a second data file
    (`*_Mobile.data*`).
- The page template (`Assets/WebGLTemplates/CasinoMares`) picks the data file when it loads. It also:
  - fills the window and shows a casino loading screen;
  - shows a "turn your phone sideways" screen in portrait;
  - goes full screen on the first tap where the browser allows it (Android; iPhone Safari can't, so it
    suggests Add to Home Screen);
  - keeps the profile file in IndexedDB (`autoSyncPersistentDataPath`).
- Files are gzipped with decompression fallback, so any static host works. `Tools/serve_webgl.py`
  serves the build on the local network for testing on a phone.
- Size (2026-10-19): `WebBuild` builds with managed stripping **High** and IL2CPP code generation
  **OptimizeSize** (verified to boot, join a room and spawn in Chrome); `Cat_N` is 512² and the sky
  photo 2048² on WebGL (platform overrides, desktop keeps its sizes); the Kenney clips are Vorbis
  0.5, mono. ProBuilder, VFX Graph, glTFast and Timeline are removed (the runtime never used them), and
  so is the unused `LowPolyAnimatedAnimals` pack. The Roslyn dlls in `Assets/Plugins/Roslyn` are editor-only.
- On-demand art (Addressables, `LazyContent`): the five recolored cat furs, the sky photo and the far
  islands and coast ship as bundles in `StreamingAssets/aa` (DXT) and `aa_Mobile` (ASTC, picked when the
  page loaded the mobile data file), not in the data file. `LazyContentSetup` makes the groups from code
  (Casino Mares ▸ Setup ▸ Set Up Lazy Content; `WebBuild` runs it), and they build with each player pass.
  - Furs: `CatFurPalette` holds each recolored fur's address, with the black fur as the stand-in. The
    menu starts loading the picked fur, so it's normally there before the player spawns; other cats show
    black until theirs arrives. Each fur is its own bundle (about 1 MB); the shared normal map is one more.
  - Sky and islands: the scenes keep their references. While a build processes a scene,
    `LazySceneProcessor` empties the island mesh filters and swaps the materials that sample the sky photo
    for scene copies holding a blurred 64×32 stand-in; `LazySceneContent` fills the real ones in after the
    scene opens.
  - A failed load is logged and the stand-in stays; nothing waits on a bundle.
  - The page loads StreamingAssets from the same place as `Build` (jsDelivr after a deploy, with the same
    GitHub Pages fallback); `Tools/deploy_pages.py` commits, size-checks and warms both folders.
- Sizes (gzip, 2026-10-19): data 10.9 MB desktop and 10.5 MB mobile, wasm 10.0 MB (9.98; Addressables
  added 0.33), from 18.6/19.0 and 12.7 on 2026-10-06. Bundles about 7.5 MB per texture format, loaded as
  needed. jsDelivr refuses files over 20 MB.
- URP 17.7 ships all ten film grain textures (2.6 MB) whenever post-processing is on; they live in the
  URP global settings (an internal resource class) and URP refills emptied slots, so they can't be trimmed
  from our code.

### 15.2 What changes in a browser (`Platform`)
- **Quality:** desktop browsers use the PC quality level, phones and tablets the Mobile one.
- **No Quit buttons.** The pause menu gets a Full screen button where the browser supports it.
- **PlayerPrefs** (volume, sensitivity, coach) are saved explicitly, since a closed tab never quits
  cleanly.
- **No worker threads**, so the poker coach's win-chance simulation runs in slices of 250 run-outs per
  frame.
- **No mouse lock.** The cursor stays free: the camera swings behind the cat as it walks, and holding
  the right mouse button turns it (the page blocks the context menu). Unity errors show in the page
  instead of a blocking `alert()`.
- **Background tabs** only get about one frame a second. A hidden tab marks its cat `IsAway`. If it's
  the master client (which runs every table), it hands that role to a player whose game is in front,
  so the tables don't stall for everyone. A phone that switches apps is frozen. After about 10 s
  Photon drops it, and it comes back to the menu with the disconnect message.
- **URL options:** `?profile=name` keeps a separate profile (two tabs share one storage); `?touch=1`
  forces the touch controls (`?touch=0` turns them off).
- **Phone performance** (a phone that has run hot slows its GPU, and each frame is then held up by pixel
  work; the per-pixel cost under Forward+ is the number of realtime lights reaching the pixel):
  - The Mobile pipeline asset renders without additional-light shadows or soft shadows, with a 512 main
    shadow map, 1 cascade and a 25 m shadow distance. `?shadows=hi` restores the PC main-light shadow
    settings on a phone, `?shadows=lo` applies the Mobile ones on a desktop, to compare.
  - **Light trim** (`MobileLightTrim`): the scene builder names the decorative point lights (sconces,
    neon spill, shelf glow, cat-wall lamp, lanterns, vestibule, the lounge's fills) `Ambience_*`; on the
    Mobile quality level their range is cut to 70 %, so at most three lights reach any pixel (the lamps
    over the games are untouched; the slot machines' lights are off on Mobile, `SlotCelebration`).
    `?lighttrim=0` turns it off, `?lighttrim=1` turns it on anywhere.
  - **Auto render scale** (`AutoRenderScale`): steps the render scale down to 0.6 while under 45 fps,
    back up after a steady 57 fps. `?autoscale=0/1`.
  - **Frame rate cap** (`FrameRateCap`): when the lowest render scale still leaves it under 45 fps for
    10 s, the frame rate is held at 30 (steady beats judder, and the phone cools); it's tried off again
    after 30 s, then 60, up to 4 minutes. The pause menu's **Hold the frame rate at 30** toggle (touch
    devices, `PlayerPrefs` `FrameRate.Cap30`, off by default) holds it regardless. `?fps30=1` caps from
    the start, `?fps30=0` never caps. The cap sets `Application.targetFrameRate` (on WebGL that means a
    `setTimeout` loop instead of `requestAnimationFrame`); if it judders, the alternative is
    `OnDemandRendering.renderFrameInterval = 2`, which keeps the browser's loop and renders every other
    frame.
  - `?debug=1` shows what the phone settled on: render scale step, frame rate target, light trim.

### 15.3 Touch controls (`TouchControls`, `Platform.IsTouch`)
Phones and tablets get on-screen controls, detected from a coarse pointer, since iPadOS reports a
desktop browser. Everything feeds `GameInput`, so gameplay code reads touch and keyboard the same way.

| Control | Action |
|---|---|
| Finger on the left part of the screen | Joystick appears under it (radius 96 UI px): a gentle push walks slowly, speed grows with the push, past the gold ring at 90% it runs (the ring and pad turn gold); the first 15% is a deadzone |
| Finger anywhere else | Drag to look (also while seated, where it swings the seat camera) |
| Jump / Paw / Emote buttons | Jump, paw swipe, open the emote wheel (tap an emote, or anywhere else to close) |
| The interaction prompt | Tap it to sit down, use the cashier or gift chips |
| Menu / Chat / Top cats pills | Pause menu, chat (with a Send button, shown at the top clear of the keyboard), leaderboard toggle |

- The touch document sits below every other UI document. Its full-screen surface only gets the
  touches no button or panel wants.
- The whole UI is scaled to the screen's physical size: about 560 UI pixels tall on a phone, against
  1080 on a desktop.
- Keyboard hints such as "(Space)" are dropped from button labels.
- The table corner buttons (View, Settings, Stand up) and toasts move to the top, clear of the bottom
  panels.
- **Testing in the editor:** turn on **Casino Mares ▸ Web ▸ Simulate Touch Controls** and set the
  Game view to a phone-shaped size. The Game view counts as a screen with 2 pixels per CSS pixel.
