# Testing checklist

Things that can only be checked on real Windows PCs.

## Confirmed

- [x] Layout looks good (v0.1)
- [x] Drag and drop from Explorer (v0.2)
- [x] Admin helper: one UAC prompt, window runs as a normal app (v0.2)
- [x] Multiple people per share, per-person access (v0.2)
- [x] Edit: add/remove people, change access, rename (v0.2)
- [x] **Fix…** opens the firewall for file sharing (v0.2)
- [x] **Undo fixes** puts the firewall rule back (v0.2)
- [x] Remove and Clean up (v0.2)

## Still to test

These need a special setup, so they're probably not covered yet.

### Server service
- [ ] Stop or disable the "Server" service (`services.msc`): **Fix…** starts it; **Undo fixes** returns it to how it was

### Anonymous (advanced)
- [ ] Share anonymously: Guest account is switched on
- [ ] Another PC with guest access allowed can open it without a password
- [ ] Removing the last anonymous share (or editing it away) switches Guest back off
- [ ] `HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\NullSessionShares` is back to how it was
