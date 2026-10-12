# Third-party notices

YPTV uses the following components. Each remains under its own license.

| Component | License | Project |
| --- | --- | --- |
| LibVLC (VideoLAN.LibVLC.Windows, shipped in `libvlc\`) | LGPL 2.1 or later | https://www.videolan.org/vlc/libvlc.html |
| LibVLCSharp | LGPL 2.1 | https://github.com/videolan/libvlcsharp |
| .NET 8 runtime and WPF (self-contained zip) | MIT | https://github.com/dotnet/runtime |
| System.Security.Cryptography.ProtectedData | MIT | https://github.com/dotnet/runtime |

LibVLC is shipped as separate DLLs and plugins in the `libvlc\` folder beside the executable, so it can be replaced
with a modified build. Its source is available from https://code.videolan.org/videolan/vlc.
