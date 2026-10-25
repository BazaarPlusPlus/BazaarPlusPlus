# Android smoke: smoke-enabled

| Level | Result | Evidence |
|---|---|---|
| Injection | pass | plugin.loaded sha256 `7c17a705daec6a829a8b3c2edb86e337124fb83548e221f9b1ab032335413965`, built `7c17a705daec6a829a8b3c2edb86e337124fb83548e221f9b1ab032335413965` |
| Hook hit | pass | VersionLabelPatch installed 1 time(s); version_label_hits 0 in the first summary, 2 in the last |
| Feature | pass | version label ` Version: 1.0.12825-production-android-d9601ded \| BPP 6.0.0 ` |

Loaded: `[14:17:05] [BazaarPlusPlus] [BPP][Android] event=android.plugin.loaded version=6.0.0 mode=enabled sha256=7c17a705daec6a829a8b3c2edb86e337124fb83548e221f9b1ab032335413965 game_version=1.0.12825-production-android-d9601ded`

Database (evidence only):

    /storage/emulated/0/Android/data/com.tempogames.thebazaar/files/BazaarPlusPlusV6/bazaarplusplus.db
    user_version 1
    battle_snapshots 9
    battles 9
    bundle_outbox 0
    bundle_seal_jobs 0
    combat_replay_videos 0
    favorite_battles 1
    run_events 44
    run_screenshots 0
    runs 3
