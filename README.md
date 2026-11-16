# Eclipse

Super powers for Batman in **Batman: Arkham Knight** (PC). He flies, runs at super speed, punches cars across the
street and burns through thugs with heat vision.

Built on [BmSDK-AK](https://github.com/Team-BmSDK/BmSDK-AK) with the fixes in
[XeTrinityz/BmSDK-AK](https://github.com/XeTrinityz/BmSDK-AK). Runs fine alongside
[Oracle: Trinity Edition](https://github.com/XeTrinityz/Oracle) and Batwing.

## What it does

**Flight.** Press **J** and he kicks off into a hover. He moves the way the camera faces. Hold **Shift** to boost into
fast flight and steer with the camera. Double tap a direction to dash. Boosting, double tap **A** or **D** to roll
and **W** for a sonic burst that takes him past top speed. Hold **Ctrl** as he settles onto the ground to land.

**Ground slam.** Hovering a few metres up, double tap **Ctrl**. He dives straight down, lands on his feet and throws
back everything round him. The further he falls, the harder it hits.

**Heavy.** Cars that drive into him stop dead and get dented. He shoves cars and wrecks out of his way and breaks
props he flies through. He takes no damage, hits don't stagger him and stuns don't work on him.

**Flight combat.** Left click punches the bracketed thug or vehicle, flying to it first if it's out of reach. Tap to
keep punching, hold to wind up a heavy punch with a shockwave. Right click picks it up, cars included, and right click
again throws it where you look or at another bracketed target.

**Heat vision.** Hold middle mouse, on foot or flying. Thugs and cars it touches are destroyed and it leaves fire
where it rests.

**Super run.** Hold **Shift** on foot. He builds up speed, the world slows down round him and he skids when you let
go. Press **Space** at speed to launch into flight. Skim the ground while boosting and hold **Ctrl** to land in a run.

**Super punch.** His punches on foot launch thugs. Pick every hit or only knockouts in the menu.

## Controls

These are the defaults. Every mod key can be changed in the menu, and its MOVES page lists everything.

| Keyboard | Controller | What it does |
|---|---|---|
| J | | Start and stop flight |
| W A S D | Left stick | Move while hovering |
| Space / Ctrl | RT / LT | Climb and sink |
| Hold Shift | RB | Boost in flight, super run on foot |
| W A S D twice | LB | Dash |
| Space twice | A | Burst up, or pull up while boosting |
| Ctrl twice | B | Drop, dive while boosting, ground slam from high up |
| Left mouse | X | Punch, hold for a heavy punch |
| Right mouse | Y | Grab and throw |
| Hold middle mouse | R3 | Heat vision |
| F5 | Hold LS + RS | Menu |

## Settings

Press **F5** for the menu. It covers the powers, flight speeds, the super run, the camera, effects and sounds, and
the keys. Turn **POWERS** off to play as plain Batman without uninstalling. Your choices are saved to `settings.toml`
in the mod folder, so updating the mod doesn't reset them.

## Install

1. Install [XeTrinityz/BmSDK-AK v0.24.0](https://github.com/XeTrinityz/BmSDK-AK/releases/tag/v0.24.0) or newer.
   Run its installer, or copy the `Binaries` and `BmGame` folders from its zip into your game folder. Older BmSDK
   builds don't run Eclipse.
2. Put the `Eclipse` folder from the zip in `Batman Arkham Knight/BmGame/Mods/` so you have
   `BmGame/Mods/Eclipse/Eclipse.dll` and `BmGame/Mods/Eclipse/mod.toml`. When you update, copy both over the old
   ones. Your settings stay. You can delete an old `scripts` folder in there. It isn't used any more.
3. Start the game, load your save and press **J**.

You can't fly in the Batmobile, during cutscenes or while you're knocked down.

## Bug reports

The mod writes `Eclipse.log` in its folder. Attach it to any bug report.
