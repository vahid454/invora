# Quick Invora backup

The normal backup policy runs automatically at 9 PM. To create one immediately, open Terminal in the Invora folder and run:

```bash
bash scripts/backup-now.sh
```

The command creates the encrypted, verified database and file backup and pushes it to the configured Backblaze destination. On macOS, you can double-click `Backup-Invora.command` instead. Keep the recovery password in your password manager; it is required to restore the portable backup on another machine.
