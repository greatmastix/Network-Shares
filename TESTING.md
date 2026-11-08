# Testing checklist

Things that can only be checked on real Windows PCs. Tick them off as they're confirmed.

## Confirmed (v0.1)

- [x] Layout looks good

## Confirmed (v0.2)

- [x] Drag a folder from Explorer onto the window: the folder field fills in

## Still to test

### Drag and drop (details)
- [ ] Drop onto a text box, the share list and the empty space: all work
- [ ] Drop a file: its folder is used

### Admin helper (new in v0.2)
- [ ] Starting the app shows the window first, then one UAC prompt
- [ ] Declining UAC: the pill says "Needs admin rights", **Allow…** asks again
- [ ] Closing the window ends the helper (no `NetworkShares.exe` left in Task Manager)
- [ ] Started with "Run as administrator": works without a second prompt

### Multiple people and editing (new in v0.2)
- [ ] Share with two people at once (Add, Add, Share): both can open it
- [ ] Per-person access: one Read only, one Read & write, each gets what was set
- [ ] Edit: add a person, remove a person, change access, rename the share
- [ ] Removing a person the app created (in Edit, or with Remove): asked to delete them, ticked by default
- [ ] Shares made with v0.1 still show up and can be edited and removed

### Fix… / Undo fixes
- [ ] On a PC where the firewall blocks file sharing: pill says "Sharing blocked", **Fix…** fixes it, another PC can connect
- [ ] **Undo fixes** puts the firewall rule back off
- [ ] Server service stopped or disabled: **Fix…** starts it; **Undo fixes** returns it to how it was

### Anonymous (advanced)
- [ ] Share anonymously: Guest account is switched on
- [ ] Another PC with guest access allowed can open it without a password
- [ ] Removing the last anonymous share (or editing it away) switches Guest back off
- [ ] `HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters\NullSessionShares` is back to how it was

### Clean up
- [ ] **Clean up** removes every share, the users (if ticked) and the fixes; `HKLM\SOFTWARE\NetworkShares` is gone afterwards
