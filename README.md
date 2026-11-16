# Eclipse

Two heroes for Batman in **Batman: Arkham Knight** (PC). As Superman he flies, stops cars dead, punches them across
the street and burns through thugs with heat vision. As the Flash he runs at super speed, throws lightning, blitzes a
whole crowd, slows time and rewinds. It plays on keyboard or controller.

Built on [BmSDK-AK](https://github.com/Team-BmSDK/BmSDK-AK) with the fixes in
[XeTrinityz/BmSDK-AK](https://github.com/XeTrinityz/BmSDK-AK). Runs fine alongside
[Oracle: Trinity Edition](https://github.com/XeTrinityz/Oracle) and Batwing.

## Picking a hero

Press **F3** or hold the **MENU** button to open Eclipse. Pick Superman or the Flash, or plain Batman. A quick tap of
MENU still pauses the game. While a hero is on, Batman's gadgets are off so the powers can have their buttons. Detective
mode stays on LB. The control legend in the bottom left always shows what every button does right now.

## Superman

**Flight.** Press **RB** or **J** to take off into a hover. He moves the way the camera faces. Hold **RB** or
**Shift** to boost into fast flight and steer with the camera. Speed trails stream off him and the wind picks up.

**Quick moves.** Hovering, d-pad up dashes him the way you push and left or right dashes him sideways. Boosting,
d-pad up is a sonic burst past his top speed and left or right is a barrel roll. On keys it's **Q** and **E**, or
double tap a direction. **A** bursts him up or pulls the nose up, and **B** drops him or dives.

**Ground slam.** Hovering a few metres up, press **B**. He drops, lands on his feet and throws back everything round
him. The further he falls, the harder it hits.

**Landing.** Hold **B**, or hold **Ctrl** near the ground. He steps down onto his feet, or skids in if he's still
moving.

**Fighting in the air.** **X** or left click punches the bracketed thug or vehicle, flying to it first if it's out of
reach. Tap to keep punching, hold to wind up a heavy punch with a shockwave. **Y** or right click picks it up, cars
included. Press it again to throw it where you look, or **X** to finish a thug in your hand.

**Heat vision.** Hold **RT** on foot or **LB** in flight, or middle mouse. Thugs and cars it touches are destroyed
and it leaves fire where it rests.

**Strength.** His punches on foot launch thugs. He takes no damage, hits don't stagger him and cars that drive into him
stop dead. Each of these can be turned off in his POWERS page.

## The Flash

**Super run.** Press **RB** or hold **Shift**. He keeps building speed and the world slows down round him. **B** or
letting go of Shift skids him to a stop. Lightning jumps off him at things he runs past.

**Lightning.** Hold **RT** or middle mouse to charge, let go to throw it at the thug or car near the crosshair. A full
charge jumps on to two more thugs.

**Blitz.** Hold **LT** or **Q** and time all but stops. Mark thugs with **X** or left click, or mark none and he picks
the nearest. Let go and he hits every one of them, then time snaps back. **B** or right click calls it off.

**Flurry.** D-pad right or **F**. He punches the thug in front of him dozens of times in a second and a half, then
launches him.

**Slow time.** D-pad left or **V**. The world all but stops while he fights. Every hit he lands is kept and they all
land when time comes back.

**Rewind.** D-pad up or **R**. He streaks back along his path to where he was 3 seconds ago.

**Tornado.** **Y** while running, or **G**. He runs a tight circle round where he stands, lifts what's near and
flings it out.

Speed Force charges pay for the blitz and the rewind. They come back while he runs fast.

## Controls

These are the defaults. Every key and button can be changed on each hero's CONTROLS page.

### Superman

| Keyboard | Controller | What it does |
|---|---|---|
| J | RB | Take off |
| Hold middle mouse | Hold RT, LB in flight | Heat vision |
| W A S D | Left stick | Move while flying |
| Space / Ctrl | RT / LT | Climb and sink |
| Hold Shift | Hold RB | Boost |
| Q, or W A S D twice | D-pad up | Dash, or sonic burst while boosting |
| E, or A / D twice | D-pad left / right | Side dash, or barrel roll while boosting |
| Space twice | A | Burst up, or pull up while boosting |
| Ctrl twice | B | Drop, dive, or ground slam from high up |
| Hold Ctrl near the ground | Hold B | Land |
| J | | Stop flying and fall |
| Left mouse | X | Punch, hold for a heavy punch |
| Right mouse | Y | Grab and throw |

### The Flash

| Keyboard | Controller | What it does |
|---|---|---|
| Hold Shift | RB | Super run |
| Let go of Shift | B | Skid |
| Hold middle mouse | Hold RT | Lightning |
| Q | LT | Blitz |
| F | D-pad right | Flurry |
| V | D-pad left | Slow time |
| R | D-pad up | Rewind |
| G | Y while running | Tornado |

Both heroes switch with **H** or d-pad down.

## Settings

Each hero has his own pages for his powers, his controls and his look. The look page puts one of your unlocked suits
on him and shows or hides the cape. The shared pages cover the camera, the controller and the HUD. Your choices are
saved to `settings.toml` in the mod folder, so updating the mod doesn't reset them.

## Install

1. Install [XeTrinityz/BmSDK-AK v0.24.0](https://github.com/XeTrinityz/BmSDK-AK/releases/tag/v0.24.0) or newer.
   Run its installer, or copy the `Binaries` and `BmGame` folders from its zip into your game folder. Older BmSDK
   builds don't run Eclipse.
2. Put the `Eclipse` folder from the zip in `Batman Arkham Knight/BmGame/Mods/` so you have
   `BmGame/Mods/Eclipse/Eclipse.dll` and `BmGame/Mods/Eclipse/mod.toml`. When you update, copy both over the old
   ones. Your settings stay. You can delete an old `scripts` folder in there. It isn't used any more.
3. Start the game, load your save and press **F3** or hold **MENU** to pick a hero.

Powers don't work in the Batmobile, during cutscenes or while you're knocked down.

## Known issues

While Superman flies, the city stops sending thugs out on foot. They come back a little while after he lands.

## Bug reports

The mod writes `Eclipse.log` in its folder. Attach it to any bug report.
