# Security and private data

Do not commit private hardware dumps, GBA save files, signing certificates, private keys, access tokens, logs containing local paths, or device-specific backups.

Firmware validation tests must use synthetic fixtures or legally redistributable public reference material. A private Action Replay dump is not required to build or test ActionReplayGBX.

If sensitive data is accidentally committed, revoke/rotate any credential first, then remove the data from all active refs and rewrite the affected Git history before relying on a normal file deletion.
