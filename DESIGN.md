# Design — late-2000s media player (WMP 11, Zune, Winamp skins) rebuilt with modern proportions: charcoal with a teal cast, soft vertical gradients, 1 px light inner edges, dense real information, exactly one accent.

## Tokens — `src/Yptv/Theme.xaml`, one ResourceDictionary, every brush frozen
- Bg: `Bg0` #0F1314 window, status · `Bg1` #161B1D panels, list · `Bg2` #262E30→#1B2123 raised (buttons, headers) · `Well` #0A0E0F inputs, tracks · video #000
- Edges: `Line` #070A0B outer border · `Hi` #344042 1 px top inner highlight · `Div` #242B2D separators · wells are inset: dark top edge, `Hi` bottom edge
- Text: `Fg` #E3EAEA primary · `Fg2` #97A3A4 meta, numbers · `Fg3` #5E6A6B placeholder, disabled
- Accent `Acc` #1FD1C1 (selection, focus, volume/progress fill, LCD text only) · `AccHi` #5BE3D6 hover · `AccLo` #138A80 pressed, LCD meta · `AccGhost` #0F2B28 LCD unlit · `Sel` #143331 selected row
- States: hover `Bg2H` #2D3638→#222A2C · pressed = `Bg2` reversed, no `Hi`, glyph +1 px down · focus = 1 px `Acc` border, keyboard only · disabled = `Fg3` on flat `Bg1` · status dot only: buffering #D9A441, error #E05A4F

## Themes — Dark (above), Light, Amoled, System (follows the Windows app mode)
- Same token names in `ThemeDark/Light/Amoled.xaml`, swapped at runtime, brushes via `DynamicResource`, frozen. `Theme.xaml` holds what never changes: fonts, the LCD well (`LcdBg`, `LcdEdge`, `LcdAcc`, `LcdAccLo`, `LcdGhost`: dark teal in every theme) and the video overlay (`VFg`, `VFg2`, `VErr`, `Veil`, `VideoBlack`, `FsFade`: fixed light-on-black). The DWM title bar follows the theme
- Light: Bg0 #E4E8E9 · Bg1 #F3F5F5 · Bg2 #FBFCFC→#E1E6E7 · Bg2H #FFFFFF→#EAEEEF · Well #FFF · Line/WellEdge top #768486 · Hi #FFF · Div #D2D9DA · Fg #12191A · Fg2 #4A5658 · Fg3 #556264 · Acc #09695F · AccHi #0E8F83 · AccLo #0B7A70 · Sel #CDEAE6 · SelDim #DCE2E3 · RowHover #E7ECED · thumbs #B9C3C5 / #A3AEB0 · Warn #8A5A00 · Err #B3261E
- Amoled: flat, no gradients. Bg0/Bg1 #000 · Bg2 #161616 · Bg2H #202020 · Well #080808 · Line #262626 · WellEdge #2A2A2A · Hi #1C1C1C · Div #1A1A1A · Fg #E6E6E6 · Fg2 #9A9A9A · Fg3 #6A6A6A · accent as Dark · Sel #0C2724 · SelDim #1A1A1A · RowHover #101010 · thumbs #2A2A2A / #3C3C3C
- Switch: status bar, right end, glyph E706 → popup menu (Dark / Light / Amoled / System). Popup menu = `Bg1` + `Line`, rows 28 px, accent tick E73E on the checked row, hover `RowHover`

## Type and spacing — system fonts, 4 px grid
- UI: `Segoe UI Variable Text, Segoe UI` — 12 body/list · 11 meta, status, tooltips · 11 SemiBold group labels
- Numbers + LCD: `Bahnschrift` — ch# 28 SemiBold · LCD name 16 · LCD meta 11 SemiCondensed · counts, rates, times 11. Root: `UseLayoutRounding`, `SnapsToDevicePixels`, `TextFormattingMode=Display`
- Steps 4/8/12/16 only; padding 8, gaps 4; radius 2 on buttons/wells, 0 elsewhere. Heights: toolbar 32 · button 28 · group header 24 · row 32 (logo 24) · deck 56 · status 22

## Layout — min 960×600, native title bar forced dark (DWM)
```
┌──────────────────────────┬─────────────────────────────────────────────┐
│ [≡] [Search........] [*] │                                             │
│ [All groups 5,214     ▾] │                 VideoSurface                │
│ ▾ NEWS              124  │                                             │
│ 012 [] Channel One    *  ├────────────────────────────┬────────────────┤
│ ▸ SPORTS             87  │ 012  CHANNEL ONE  NEWS·HD  │ |< || [] >| =- │
├──────────────────────────┴────────────────────────────┴────────────────┤
│ ● Playing │ H.264 1920×1080 │ 4.2 Mb/s │ buf 300ms │ 5,214 ch │ 2m ago │
└────────────────────────────────────────────────────────────────────────┘
```
- Channel panel left (300 px, GridSplitter 240–440), video right, deck below video = LCD strip (flex) + transport (prev, play/pause, stop, next · mute + 96 px volume · playback options · fullscreen). Status bar full width
- Fullscreen: video only; overlay inside `VideoSurface.Content` (airspace): deck on a black→transparent gradient, shown on mouse/key, hidden with cursor 2.5 s later (one-shot timer). Zap OSD = LCD well top-left 3 s; digit entry `01_` top-right

## Components — states via style triggers, instant, no Storyboards
- Button 28×28, glyph 16: `Bg2` + `Line` border + `Hi` top; toggled (mute, fav) = glyph `Acc`. Tooltip: `Bg1` + `Line`, 11 px, shows hotkey
- Search: `Well` inset, search glyph, clear button when non-empty, placeholder `Fg3`, focus border `Acc`. Group combo: button look + chevron; popup `Bg1`, items use row states
- Group header (24): `Bg2`, chevron, NAME 11 SemiBold `Fg2`, count right-aligned Bahnschrift `Fg2`; header rows live in the flat list (no CollectionView grouping)
- Channel row (32): number `Fg2` · logo 24 (2-letter monogram on `Bg2` until loaded) · name 12 · star. Hover #1D2426 · selected `Sel` + 2 px `Acc` left bar · selected-unfocused #202A2C, no bar · playing = number + play glyph `Acc`. No zebra
- LCD well: #071211→#0B1918, inset edges; ghost `888` in `AccGhost` behind ch#; name `Acc`, group/codec/clock `AccLo`. Static text, no marquee
- Slider: 4 px `Well` track, `Acc`→`AccLo` fill, 8×14 `Bg2` thumb (hover `Bg2H`, focus `Acc` border, disabled `Fg3` fill). Scrollbar 8 px, no arrows, thumb #2C3537 / hover #3A4547 / drag `AccLo`
- First run (no playlist), video area, centered: title 16 `Fg`, hint `Fg2`, two 40 px buttons: Open playlist (E8E5, file dialog) · Paste URL (opens dialog, clipboard prefill). While loading: "Loading playlist…", no buttons
- Add-playlist dialog: modal, 480 wide, dark title bar; URL-or-path well + browse button, OK (default, disabled while empty) / Cancel
- Can't play: video stays black; centered warning glyph (E7BA) + "Can't play this stream" in `Err`, channel name in `Fg2` (never the URL), Try again button; status dot `Err`
- Playback options: deck button (E713) → popup menu (same style as the theme menu) with section rows (disabled, `Fg3`): Audio track and Subtitles only when the stream offers a choice, Aspect (Auto, 16:9, 4:3, Fill, Original), Deinterlace (Off, On). Hotkey changes show as a short LCD-well note top-left (OSD). Reconnecting: status text "Reconnecting n/5", `Warn` dot
- Playlists: the toolbar ≡ button opens a popup menu (saved playlists with a tick on the active one, Manage playlists…, Add playlist…). Manager dialog 720×420: list (marker E768 on the active row, name over host, type, channels, refreshed), buttons Add / Set active / Refresh now / Rename / Remove / Account (Xtream only) / Export / Copy link, auto-refresh combo. Add dialog 480 wide: two ScopeTab-style tabs (M3U, Xtream login), name, fields, status line in `Err` on failure. Account dialog: label/value rows, password masked until Show. Export dialog: Live/Movies/Series/All tabs, checkbox, warning text. Password box = text box look
- Expiry segment (status bar, Xtream only): "Expires 12 Nov 2026 | 33 d", `Fg2`, `Err` when expired or under 7 days; click opens the account dialog
- Settings dialog 480 wide, one column of label/control rows: Theme, Startup, Auto-refresh, then Hardware decoding, Buffering, User-Agent (hint line in `Fg3`), then cache rows (name, size in Bahnschrift, Clear button), then Shortcuts / Open log folder / Copy diagnostics and an About line (version, LibVLC) beside Close. Changes apply at once, no OK. Shortcuts dialog 420 wide: key column in `Acc` Bahnschrift, action column, rows 24 px, generated from `Hotkeys.cs`
- Mini mode (Ctrl+M): native frame kept, only the video; the deck sits in the overlay on a black fade like fullscreen, LCD strip hidden. Pin (E718, deck, toggled = `Acc`) = always on top, works in normal and mini mode. Status bar right end: Settings E713 beside Theme E706
- Status bar: `Bg0`, `Hi` top edge, 1 px `Div` between segments, values Bahnschrift; stats poll 1 Hz only while playing and window visible

## Icons — `Segoe Fluent Icons, Segoe MDL2 Assets` glyphs (16 in buttons, 12 in rows), no image assets. Play E768 · Pause E769 · Stop E71A · Prev E892 · Next E893 · Volume E767 · Mute E74F · Fullscreen E740 / exit E73F · Search E721 · Star E734 / E735 · Clear E894 · Chevron E70D / E76C · Open E8E5 · Refresh E72C · Settings E713 · Collapse all E70E · Expand all E70D · Groups E71D · Check E73E · Theme E706 · Playback options E713 · Pin E718

- **Will NOT:** purple/rainbow gradients, glassmorphism/acrylic/mica, blur, DropShadowEffect, OpacityMask, emoji, pills or radius >2, oversized padding, card grids, a second accent
- Animations/Storyboards, marquees, scanline/noise textures, image assets, bundled fonts, UI libraries, custom title bar, skin engine
