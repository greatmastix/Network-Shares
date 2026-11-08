# Network Shares

A small, portable Windows tool to share a folder on the network: pick a folder and share it with a user who already
exists, a new user the app creates, or everyone. Remove a share and everything the app set up for it goes too.
WinForms on .NET 10, a single exe with .NET built in.

## Download

Every push builds `NetworkShares-win-x64` (one exe, about 45 MB zipped, nothing to install). Find it under
**Actions → build → Artifacts**, or on the Releases page for tagged versions.

Prefer the **Releases** page: a release is the same file for everyone, so browsers and SmartScreen learn to trust it,
whereas every Actions build is a brand-new unknown file.

To publish a release, either draft one on GitHub (**Releases → Draft a new release**, new tag such as `v0.1.0`, target
`main`), or push a tag: `git tag v0.1.0 && git push origin v0.1.0`. Either way the build takes its version from the tag
and attaches the zip to the release.

## Using it

The app asks for administrator rights when it starts (Windows only lets administrators create shares and users).

1. **Folder:** browse, type a path, or drop a folder onto the window.
2. **Share name:** suggested from the folder's name.
3. **Share with:**
   - **Existing user:** any account on this PC (or a domain account as `DOMAIN\name`). Others sign in with that
     account's own password.
   - **New user:** the app creates a standard account just for this share, with a generated password, hidden from the
     Windows sign-in screen. The password is shown once, after sharing.
   - **Everyone:** one shared guest login, `share-guest`, with a password the app keeps and shows, so you can hand it to
     anyone. Every "Everyone" share uses the same login. **New password** changes it.
   - **Everyone, anonymous (advanced):** no password at all. This switches on Windows' built-in Guest account and lets
     it connect over the network; both are switched back off when the last anonymous share is removed. Other PCs may
     still refuse to connect: Windows 10 and 11 block guest access by default, and Windows 11 24H2 also requires SMB
     signing. Use the guest login if you can.
4. **Access:** Read only, or Read & write.

The result shows exactly what to type on the other PC: `\\PC-NAME\Share`, the user name (with the PC's name in front)
and the password, with a Copy button. The footer shows the PC's name and IP addresses.

**Shares made with this app** lists the app's own shares (not other shares on the PC). **Copy path** copies
`\\PC-NAME\Share`; **Remove** stops sharing and removes the folder permissions the app added, and offers to delete the
user the app created for it (ticked by default; only if no other share of the app uses it). A share that was removed
outside the app shows as "no longer shared"; Remove then cleans up the rest.

The pill in the header checks whether other PCs can reach your shares: is the **Server service** running, and does
**Windows Firewall** allow file sharing on the current network. If not, **Fix…** says what it will change and does it;
**Undo fixes** puts both back the way they were.

**Clean up** removes every share the app made, optionally its users, and undoes every fix: use it before you take the
app off a PC.

## What it changes on the PC

As little as possible, and all of it can be undone:

| What | When | Undone by |
|---|---|---|
| The share itself, with its share permissions | Share folder | Remove |
| One permission entry on the folder (NTFS), for the chosen account | Share folder, if the account doesn't have that access yet | Remove (the previous entry is restored exactly) |
| A local user, hidden from the sign-in screen | New user, or the first Everyone share | Remove (ticked) or Clean up |
| Built-in Guest account on, allowed on the network; share on the anonymous list | Anonymous share | Removing the last anonymous share |
| Firewall "File and Printer Sharing (SMB-In)" rule on, for the current network | Fix… | Undo fixes |
| Server service started / set to automatic | Fix…, only if it was off | Undo fixes |

Nothing is installed and no files are written. The app records what it did in one registry key,
`HKLM\SOFTWARE\NetworkShares`, readable by administrators only: the shares and users it created, and the state of
anything it changed before the change. The key only exists while something is recorded. The guest password is stored
there encrypted for this PC (DPAPI), so the app can show it again. Shares also carry the description "Shared with
Network Shares", which other PCs see when browsing.

## Building

```
dotnet test tests/NetworkShares.Tests -c Release
dotnet publish src/NetworkShares -c Release --self-contained -p:PublishSingleFile=true -p:DebugType=none -o out
```

Builds on Windows, Linux or macOS (it runs on Windows only). `tools/make_icon.py` draws `app.ico` (needs Pillow).
