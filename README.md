# Define Board

Turn scripting define symbols on and off from a table of switches instead of editing one long
semicolon-separated string in Player Settings.

Namespace: `DefineBoard` — Editor only.

## Installation

Add this line to `Packages/manifest.json` under `dependencies`:

```json
"com.kienack123.define-board": "https://github.com/kienack123/Define-Board.git#1.0.0"
```

or **Window ▸ Package Manager ▸ + ▸ Install package from git URL…** and paste
`https://github.com/kienack123/Define-Board.git#1.0.0`.

Drop the `#1.0.0` suffix to always track the latest commit on `main`.

The package ships a precompiled editor-only `DefineBoard.dll` that references nothing but
`UnityEngine` / `UnityEditor`. The source it was built from is on the `source` branch.

## Open it

- `Tools ▸ Define Board ▸ Open Window`
- Or the **Defines** button in the main toolbar. The dll carries the toolbar code path for Unity
  2022 – 6000.4, which inserts the button into the toolbar directly. Unity 6000.5+ reports that as an
  unsupported toolbar element and hides it: right click the main toolbar ▸ *Unsupported User
  Elements* to show it again.

## What the window shows

```
Standalone (active) ▾ │ Search │ Options ▾
[ All 85 ][ On 12 ][ Off 73 ][ Changed 1 ]      scanned 2026-09-08 11:46
▌ +STREAMING  -DEVELOPMENT                      [ Revert ] [ Apply ]
▼ In Player Settings                                              16
   [ ON ] DEVELOPMENT                                             10
   [ OFF] STREAMING ¹                                              9
▶ Used in code    ▶ Unity built-in
```

- **ON green / OFF red** — click the pill to flip a symbol.
- Changes are **batched**: the amber bar lists exactly what Apply will write, so a set of edits costs
  one recompile instead of one per click.
- **Sections** separate what is really in Player Settings from symbols only found in `#if` directives
  and from symbols Unity defines itself (those are shown as `AUTO` and cannot be toggled by mistake).
- The **usage count** on the right opens a menu of `File.cs:line` entries that jump into the code.
- **Build target** is selectable; the active one is marked, and editing another platform says so.

## The trailing `1` convention

Some projects disable a symbol by appending `1` to its name (`STREAMING` ➜ `STREAMING1`) instead of
deleting it, so the name stays visible in the define string. Define Board understands this: such a
symbol is listed once, shown as OFF with a `¹` badge, and toggling it rewrites the suffix instead of
adding a duplicate. Names that end in `_<digit>` (for example `ODIN_INSPECTOR_3_1`) are never treated
this way. Turn the behaviour off under `Options ▸ Turn off by appending 1`.

## Notes

- The symbol list comes from a scan of every `.cs` file under `Assets/`, cached in
  `Library/DefineBoard.cache`; rescan from the `Options` menu.
- Applying while Play Mode is running asks first, because the domain reload can break the session.
