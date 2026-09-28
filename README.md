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
- Übrige: aus `ps5_super_pldmgr_auto_updated_offline.aio_latest.zip` (Release `latest`, Build 2026-09-28 02:20 UTC), SHA-256 identisch mit dem nexgen999-Feed

Alle Dateien haben einen gültigen ELF-Header; `checksum` ist der SHA-256 der hier gehosteten Datei.
Statischer Stand, keine automatischen Updates. Rechte und Credits liegen bei den jeweiligen Autoren (siehe Credits im nexgen999-README).
