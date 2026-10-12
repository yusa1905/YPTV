# YPTV

Windows 11 desktop IPTV player. Goals: tiny, instant startup, instant channel zapping, low RAM.

## Stack
- C# .NET 8, WPF, LibVLCSharp (core + VideoLAN.LibVLC.Windows; own black HwndHost `VideoSurface` instead of LibVLCSharp.WPF)
- No Electron / web views. Minimal dependencies (no MVVM framework, no DI container)
- Layout: `src/Yptv/Yptv.csproj` (the app), `samples/sample.m3u` (tiny test playlist)

## Rules
- Parse M3U/M3U8 off the UI thread, streaming (line by line), never load the whole file into one string
- Cache the parsed playlist on disk (JSON) so the next start is instant; refresh in background
- Virtualized channel list, debounced search (~150 ms), logos lazy-loaded with a disk cache
- LibVLC: hardware decoding on (`--avcodec-hw=any`), low `--network-caching` (~300 ms) for fast zapping
- Reuse one LibVLC + one MediaPlayer instance for the app lifetime; never recreate on channel change
- Known gotcha: WPF controls drawn over the video (VideoView) hit the "airspace" problem. Put overlay UI inside VideoSurface's content (its owned transparent window), not on top of it
- Keep code simple: small files, no giant classes, no speculative abstractions
- Never read or print real playlists. Use `samples/sample.m3u`. Real playlists live in `playlists/` (gitignored)
- Never hardcode stream URLs or credentials. The app ships with no playlists

## Design + performance
- Follow `DESIGN.md`. WPF only: no UI libraries, no bundled fonts
- All brushes frozen, in one ResourceDictionary. Gradients and borders only: no DropShadowEffect/BlurEffect/OpacityMask
- Virtualized recycling list, logos decoded at ~32 px, no timers or animations running while idle
- Budget: cold start < 1 s to a visible window, idle RAM < 150 MB with 5k channels, ~0% CPU idle
- Banned: purple gradients, glassmorphism, emoji icons, pill shapes, oversized padding, generic card grids, more than one accent color

## Commands
- Build: `dotnet build src/Yptv -c Release`
- Run: `dotnet run --project src/Yptv`
- Publish (self-contained, ~148 MB zip): `dotnet publish src/Yptv -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:DebugType=none -o publish/YPTV-win-x64`
- Publish (needs .NET 8 Desktop Runtime, ~86 MB zip): same with `--self-contained false -o publish/YPTV-win-x64-fdd`. Both keep `libvlc\` beside the exe; zip the folder contents. Never ship `plugins.dat` (the app makes it on first run)

## Working style
- Be terse. Don't explain unless asked.
- One milestone at a time. After changes: build, fix errors, then stop.
- Don't re-read files you just wrote. Don't reformat unrelated code.
