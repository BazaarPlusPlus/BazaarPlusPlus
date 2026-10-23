# #396 interop spike: renting the game's card prefabs under Il2CppInterop

AVD `bpp_api35` (emulator-5556), game `1.0.12825-production-android-d9601ded`, LemonLoader
`v0.7.4-android.4`, BPP 6.0.0 Release build. The spike code was temporary and is not committed;
the real adapter replaced it (see `../README.md`).

## What the spike did

A temporary page in the touch panel rented eight cards per press (two each of Small, Medium and
Large items and Skills), exactly as the desktop's `NativeCardPrefabLoader` and
`NativeCardPreviewFactory` do:

1. `Services.TryGet<AssetLoader>` → `AssetLoader.InstantiateAssetAsyncByReference(<Small|Medium|Large|Skill>UIAssetRef, scope)`
   returns an `Il2CppSystem.Threading.Tasks.Task<GameObject>`;
2. `root.SetActive(false)`, `GetComponent<CardPreviewBase>()`, reparent under a RectTransform of the
   touch UI's canvas (the root that carries the `ClassInjector`-registered `AndroidTouchRoot`);
3. `_showTooltips = false`, activate, `CardPreviewBase.SetUp(template, false, TCardInstanceItem|Skill, CancellationToken.None)`;
4. `Show(true)`, `Resize()`, size an item root from its `AspectRatioFitter` (484 high), scale into the cell.

Both IL2CPP tasks were polled once per frame from the touch root's `Update`; nothing awaited them.
Return = deactivate and reparent under a BPP pool root (BPP-owned queue per kind, as the desktop's
`NativeCardPreviewPool`); destroy = `Object.Destroy(root)`.

## Results (`bpp-spike.log`)

| Measurement | Event | Result |
|---|---|---|
| First rent, 8 fresh instances | `android.spike396.rented` | all 8 set up, 299–451 ms each; skills 484×484, items 252/503/755×484 (`01-first-rent.png`) |
| Next set from the pool | `rent_started offset=8` | `inst=8 hits=8 setup=16/0`: pooled instances re-`SetUp` with other templates and tiers, no art carried over (`02-second-rent-pooled.png`) |
| 30 rent/return cycles | `cycles_done` | 240 rents in 6.2 s, still `inst=8`, `scene_cards=8`, `balanced=true` |
| Close the panel | `returned reason=panel_closed:close_button` | 8 returned, `live=0 pooled=8` |
| Hold 8 live cards, lobby → run | `scene_loaded scene=Gameplay` | `live_alive=8` (the DontDestroyOnLoad root keeps them) but item art and frames turn white (`03-held-across-scene-art-released.png`) |
| Re-`SetUp` those pooled instances in the run | `rent_started current_scope=InRun` | frames reload, item art stays white (`04-pooled-resetup-in-run.png`) |
| Destroy, rent fresh in the run | `destroyed ... balanced=true` | fresh instances render (`05-fresh-instances-in-run.png`) |
| Instantiate with `AssetScope.Global`, run → main menu | `requested_scope=Global`, `scene_loaded scene=MainMenuScene` | `live_alive=8` and the art survives (`06`, `07`) |
| Destroy everything | `destroyed`, `status` | `inst=24 destroyed=24 balanced=true scene_cards=0` |
| Tap rented cards | `android.hooks.summary` | `tooltip_hits=0`, `upgrade_preview_entered=0` before and after: no cross-use with the tooltip hooks |

## Conclusions

- Feasible. The desktop rental contract carries over: instantiate through the game's
  `AssetLoader`, `SetUp`/`Show`/`Resize` on `CardPreviewBase`, a BPP-owned pool, `Object.Destroy`
  to end an instance. `Object.Destroy` is the right end: the game instantiates with
  `Object.Instantiate` of a loaded asset, not `Addressables.InstantiateAsync`.
- Instantiate and destroy boundaries: an instance belongs to the asset scope it was instantiated in.
  `ReleaseScope` on a lobby ↔ run change releases the prefab's assets under `Current`, which
  whitens every instance of that scope, live or pooled, and a re-`SetUp` does not recover it.
  Instantiate with `AssetScope.Global` (pass a real `Il2CppSystem.Nullable<AssetScope>`; the
  generated method rejects null), and still return every rental and destroy the pool on a scene load
  so no instance outlives the panel that rented it.
- The `ClassInjector`-registered `AndroidTouchRoot` needs nothing special: rented roots parent
  under any RectTransform of its canvas, and its `Update` is the frame tick that polls the IL2CPP
  tasks. A ScreenSpaceOverlay canvas renders the native card shaders, as on the desktop.
- `_showTooltips = false` keeps a rented card out of the game's tooltip path.
- The first `JsonGameDataManager.GetCardMap()` call stalls the main thread while the game reads every
  card from its database (5–35 s on the emulator, timed in the port); enumerating the 3375 entries
  afterwards takes 25 ms.
