# Release notes

## v0.3.11

- Refreshed the BepInEx IL2CPP interop assemblies for the latest KOTAMON build.
- Removed the non-working figurine-shelf command.
- Added **All Cassettes** (default `F7`), which unlocks every configured cassette through `TapePlayer` and saves the updated collection.

## v0.3.10

- Added **All Figurines on Shelf** (default `F7`). It completes every active `CollectShelfById` using the game's collectible definitions, without routing the operation through Auto Cleanup.

## v0.3.9

- Auto Cleanup destroys confirmed garbage directly again. It no longer routes ordinary garbage through the game's pickup logic.
- Tightened the deletion rule so unknown pickup data remains in the world instead of being treated as disposable garbage.

## v0.3.8

- Auto Cleanup now uses the game's native zone routine to verify and collect unlabelled card fragments before removing trash. If that check is unavailable, it keeps ordinary items instead of risking a fragment being deleted.
- Added **Always Full Bag** (default `F5`), with a menu toggle and rebindable key.
- Added **Max Card Collection** (default `F6`), which unlocks all cards and upgrades them to the highest `Foil` quality.

## v0.3.7

- Restored use of the native `_partPickups` registry exclusively for card fragments.
- Fragment IDs from that registry now drive both cyan ESP classification and Auto Cleanup collection before normal trash removal.
- Kept unreliable `CardData` and generic card-zone classification disabled, so ordinary junk cannot become a false dirty card.

## v0.3.6

- Added the game build's `Fragment` and card-piece identifiers to the card-fragment classifier.
- Fragment ESP and Auto Cleanup keep using the normal native pickup route, so a detected fragment is registered by the game instead of being deleted.

## v0.3.5

- Removed two unreliable classification sources (`CardData` and private zone pickup lists) that marked all world objects as dirty cards.
- ESP and Auto Cleanup now classify pickups only by their native `EJunkType` plus explicit card/fragment identifiers.
- Card fragments now use `PlayerPickupController.Pick(..., true)`, the same native collection entry point as an in-game interaction, instead of a direct follow-up method call.

## v0.3.4

- Regenerated all IL2CPP interop assemblies from the Steam installation currently used to launch KOTAMON.
- Fixed the world-load Access Violation caused by an interop assembly generated from a different game build.
- Deferred optional fragment-HUD state reads until the menu is open; world loading no longer calls `ParametersController.GetValue()`.

## v0.3.3

- Rebuilt Auto Cleanup around the game's authoritative `JunkZoneController` lists instead of classifying world objects as trash.
- Dirty cards and card fragments are protected and collected from `_cardPickups` and `_partPickups` before any deletion runs.
- Only `_junkPickups` (the normal-junk list) is eligible for removal; figurines and all rare objects are protected.
- Fixed ordinary trash being falsely shown as figurines when it carried a generic `CollectibleData` reference.
- Added `CardData` and fragment-name fallback recognition for ESP and protection during the frame in which an object is spawned.

## v0.3.2

- Fixed dirty cards and fragments being removed without completing their native collection path.
- Auto Cleanup now binds each world pickup to `PlayerPickupController._pickup` before calling `TakeDirtyCard()` or `TakeCardPart()`.
- Trash removal is fail-safe and only deletes confirmed `EJunkType.Common` objects.
- Added green ESP boxes, labels, and tracer lines for figurines/collectibles.
- Figurines and unknown new special pickup types are preserved by Auto Cleanup.

## v0.3.0

Compatibility update for the August 2026 KOTAMON build.

### Plugin

- Regenerated IL2CPP interop assemblies from the updated `GameAssembly.dll` and metadata.
- Added native `EJunkType.Part` fragment detection.
- Added world-space fragment ESP with cyan boxes and tracer lines.
- Updated Auto Cleanup to collect every spawned fragment through `TakeCardPart()` before deleting trash.
- Removed the obsolete `UpdatePartTimer()` dependency.

### Build and launcher

- Build scripts now support the full game directory name containing commas.
- The standalone launcher packages the current patched BepInEx runtime and regenerated interop assemblies.
- Verified BepInEx chainloader completion with plugin version `0.3.0` loaded.

## v0.2.5

First public release.

### Plugin

- Adjustable Noclip and WorldSpeed.
- Dirty-card ESP with boxes, labels, and tracer lines.
- Virtual card-fragment counter and native timer display.
- Instant ordered Auto Cleanup.
- Money editor, draggable windows, rebindable hotkeys, and menu input capture.

### Launcher

- Self-contained BepInEx 6 IL2CPP installation.
- Bundled Unity 6000.4.1 libraries and compatible interop assemblies.
- Game-folder validation and loader backups.
- Install/update, launch, and confirmed uninstall workflows.
- Existing BepInEx installations are preserved during uninstall.

### Validation

- Clean install/uninstall cycle verified without a preinstalled BepInEx runtime.
- Tested against the Steam demo build of KOTAMON.
- BepInEx chainloader completes with the plugin registered and loaded.
