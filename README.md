# PS5 Payload Manager – Fix-Quelle

Eigene Quelle für [PS5 Payload Manager](https://github.com/itsPLK/ps5-payload-manager) mit den Payloads, die im Feed von
[nexgen999/PS5-Super-PLDMGR-Auto-Updater](https://github.com/nexgen999/PS5-Super-PLDMGR-Auto-Updater) (Stand `eb128d7`, 2026-09-28) HTTP 404 liefern
oder statt einer ELF eine GitHub-HTML-Seite enthalten.

Quelle in pldmgr hinzufügen (Settings → Manage Sources → Add Source):

```
https://raw.githubusercontent.com/cosmicflow2512/PS5-PayloadManager/store/payloads.json
```

## Herkunft der Dateien
- PS5 Beta + ghost-toothAPI: `Internal/payloads/...` aus dem nexgen999-Repo (die echten ELFs, nicht die HTML-Seiten aus dem Feed)
- kstuff-1.13-fpkg-dr-test6: manuell hinzugefügt (Upload von Maik, 2026-10-11, ersetzt dr-test5)
- ShadowMountPlus 1.7beta5-fix1: aus dem offiziellen Release https://github.com/drakmor/ShadowMountPlus/releases/tag/1.7-beta5fix1 (2026-10-11, ersetzt 1.7beta5)
- webkit-autoloader-installer v0.6.1: manuell hinzugefügt (Upload von Maik, 2026-10-08)
- pkg-manager v1.5.1: aus dem offiziellen Release https://github.com/itsPLK/ps5-pkg-manager/releases/tag/v1.5.1 (2026-10-10, ersetzt v1.4.1)
- 2026-10-16 aus offiziellen Releases aktualisiert: ps5upload v7.0.27, elf-launcher v2.0.5, MemDBG nightly-20260930 (Vorversionen bleiben bis zum nächsten Update liegen)
- 2026-10-16 neu aus offiziellen Releases: FGG-PlayPods-GUI v0.1.5 (menfis911), kstuff-a53 1.4 (Soonniicc)
- 2026-10-16: PS5 Payload Manager v0.6.1 (itsPLK) aufgenommen; alle Einträge mit stabiler id, author und source (pldmgr ab v0.6.0)
- Übrige: aus `ps5_super_pldmgr_auto_updated_offline.aio_latest.zip` (Release `latest`, Build 2026-09-28 02:20 UTC), SHA-256 identisch mit dem nexgen999-Feed

Alle Dateien haben einen gültigen ELF-Header; `checksum` ist der SHA-256 der hier gehosteten Datei.
Statischer Stand, keine automatischen Updates. Rechte und Credits liegen bei den jeweiligen Autoren (siehe Credits im nexgen999-README).
