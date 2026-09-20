# UDP to NDI

Created by **John Lightfoot** ([JohnDevAc](https://github.com/JohnDevAc)). Free to use for personal and commercial purposes under the [MIT License](LICENSE).

The MIT licence covers this project's original source code, documentation and icon. FFmpeg, Microsoft .NET and NDI retain their own licences; see [third-party notices](installer/THIRD-PARTY-NOTICES.txt). Third-party executables and generated installers are not stored in this repository.

A Windows x64 desktop application with **10 independently controlled audio/video slots**. Each slot decodes an RTP or UDP source with FFmpeg and publishes an NDI High Bandwidth source through the installed NDI runtime.

## Run

### Windows installer

Download the installer from [GitHub Releases](https://github.com/JohnDevAc/udp-to-ndi/releases/latest), or build it locally and run `dist/installer/UDP-to-NDI-Setup-1.0.3-x64.exe`. It installs the latest app for the current Windows user, adds a Start menu shortcut and offers a desktop shortcut. .NET Desktop Runtime is bundled. Setup downloads FFmpeg directly from its provider over HTTPS and verifies a pinned SHA-256 checksum. If the download fails, installation finishes with manual setup instructions. If NDI is missing, Setup launches the included official NDI Runtime installer, which may request administrator access and displays its own license. Existing Windows NDI settings are retained. Installed location: `%LOCALAPPDATA%/Programs/UDP to NDI`.

Remove the app through Windows Settings → Apps. Uninstallation preserves your saved slots and the shared NDI Runtime. The installer is unsigned. Build it again with `build-installer.ps1`; this requires Inno Setup and the NDI SDK redistributable.

### Portable builds

`build.ps1` publishes to `dist/portable-1.0.3`; an alternative output folder can be selected with `-OutputDirectory`. Portable builds do not include FFmpeg: download and extract a Windows build, then select `bin/ffmpeg.exe` using **More → Choose FFmpeg…**. The NDI 6 Runtime (or NDI Tools) must be installed. The framework-dependent build also needs .NET Desktop Runtime 8 x64.

1. Click **Edit** on a slot.
2. Choose **RTP H264**, **RTP H265**, **RTP MPEG-TS**, **UDP MPEG-TS**, or **SDP file**.
3. For unicast, enter `0.0.0.0` or this PC's receiving IPv4 address, and the destination port. Do not enter the remote encoder's IP as the listening address.
4. For multicast, enter the group address, e.g. `239.10.10.1`, and the destination port. Select the local receiving interface if this PC has multiple adapters.
5. Usually the defaults are sufficient. **Advanced** exposes RTP payload type, network interface and FMTP parameters. The default payload is 96 for H264/H265 or 33 for MPEG-TS. RTP also uses the following UDP port for RTCP. Default ports are 5000, 5002, …, 5018 to avoid overlap.
6. Prefer the encoder's **SDP file** if available. It preserves payload mappings and codec initialization information. Without SDP, the encoder must transmit the necessary SPS/PPS/VPS headers in-band, or you must supply its FMTP parameters. An SDP file controls its own addresses and ports; the corresponding manual fields are disabled.
7. Set a unique NDI source name. Click **Save**, then **Start**. Resolution, exact frame rate, pixel/display aspect ratio and progressive/interlaced scan type are detected automatically and mirrored to NDI. No output format configuration is needed. Hover over the Input cell for the detected frame rate, aspect information and source codec description.

`Live` means frames are being decoded and handed to NDI. `Viewers` reports connected NDI receivers. Input loss is shown after four seconds without frames. A stalled video or previously present audio stream causes the decoder to reopen after a 20-second frame timeout. Failed attempts wait three seconds and then retry, **with no attempt limit and no overall timeout**. The NDI source remains available across decoder retries. This also works when the source is absent at startup. Only **Stop**, **Stop all**, or closing the application ends retries. Hover over status for the retry count and latest message. Old saved settings that disabled reconnection are ignored.

## Audio

Audio is included automatically; there are no audio setup switches. The first audio track is decoded to floating-point PCM and sent through the same NDI source, retaining its detected sample rate and channel count (up to 64 channels). Video-only sources continue to work.

MPEG-TS over UDP or RTP can carry video and audio together. Plain H264/H265 RTP payloads are video-only: for separate RTP audio and video tracks, load the encoder's SDP file describing **both** media sections, payload mappings and ports. AAC and other codecs supported by the configured FFmpeg are decoded; compressed audio is not copied into NDI unchanged. Multiple alternative language/program audio tracks are not combined.

Audio and video use one source-timestamp timeline for pacing and NDI timecodes, preserving their relative offsets. Normalized PCM is mapped to NDI's SMPTE reference level (full-scale PCM corresponds to 10.0 NDI floating-point units). No stereo downmix or forced 48 kHz conversion is applied. An audio format change causes automatic decoder reopening. A source that starts without audio is rechecked for audio when it reconnects; adding an entirely new audio track to a continuously running video-only source may require stopping and starting that slot.

The decoder no longer scales, letterboxes, deinterlaces, or forces a chosen frame rate. It passes one decoded frame per input frame, with automatic NDI format metadata. Fractional rates such as 30000/1001 and 60000/1001 are retained. Interlaced video remains interlaced; bottom-field-first video is reordered to the top-field-first layout required by NDI. Unknown pixel aspect defaults to square pixels. If neither frame rate nor frame duration can be detected, the slot reports an error instead of guessing a rate. Old saved manual output settings are ignored.

HDR, alpha, genlock and original embedded source timecode are not carried. Pixel data is converted to 8-bit 4:2:2 for NDI High Bandwidth (not NDI HX); source codec, compressed bitrate and bit depth therefore are not copied unchanged. Supported detected dimensions are up to 8192×4320 with an even width, at up to 240 fps, subject to decoder, runtime and hardware limits. Pacing follows decoded presentation timestamps; this is not a genlocked broadcast clock.

## NDI multicast output

All ten outputs follow the existing **Windows NDI Access Manager settings**. There are no multicast output controls in this application, and it never edits the Windows NDI configuration. To change output transport, use NDI Access Manager and restart this app so the runtime reloads the settings.

NDI selects multicast addresses from the configured range. Receivers negotiate the transport; multicast must be enabled at the receiver and unicast fallback remains possible. This is independent of whether the input is unicast or multicast. For a multicast network, configure IGMP snooping/querier appropriately and allow sufficient bandwidth. Ten HD outputs can exceed a gigabit link depending on resolution, rate and scene content; performance is hardware dependent.

Existing NDI Access Manager adapter, group, and discovery-server selections are respected. If sources are not visible, check these settings, receiver groups, network reachability and Windows Firewall. Allow FFmpeg's input RTP/RTCP ports and the NDI application's network access on the intended network. The application does not alter firewall rules.

## Persistence and operation

Settings: `%LOCALAPPDATA%/UdpToNdi/settings.json`. Configuring a slot saves all slots. Nothing starts automatically. Closing the application stops its decoders and NDI senders. Duplicate active NDI names and overlapping manually configured endpoints are rejected. SDP port collisions must be checked against the source SDP files.

## Build and verify

Requires .NET SDK 8 and Windows x64. No additional NuGet dependencies.

For local media testing, place an FFmpeg Windows executable in `tools/ffmpeg.exe`. Release builds intentionally do not embed it. Obtain it from the [FFmpeg download page](https://ffmpeg.org/download.html). Install the NDI Runtime for running/testing. Building the installer also requires Inno Setup 6 and the official NDI SDK redistributable; pass their paths to `build-installer.ps1` using `-CompilerPath` and `-NdiRedistributable` if needed.

```powershell
dotnet build -c Release
.\build.ps1
$test = Start-Process -FilePath '.\bin\Release\net8.0-windows\UDP to NDI.exe' -ArgumentList '--self-test' -PassThru -Wait -WindowStyle Hidden
Get-Content .\test-results\self-test.txt
```

Tests validate the ABI, input configuration and SDP generation; generate local RTP/UDP video at different resolutions, integer/fractional frame rates, anamorphic aspect ratios and interlaced scan types; assert the resulting NDI receiver metadata; and start/stop all ten slots. They use ports 25100–25219 and temporary NDI sources. The multicast input test uses `239.250.44.1`. Tests do not change the shared NDI transport configuration. They are functional tests, not a ten-channel HD load qualification or proof of multicast output on a separate receiver.

Audio tests receive actual NDI samples from 48 kHz stereo and 44.1 kHz mono sources, plus separate RTP media tracks described by SDP. Run `--recovery-test` for the longer outage test: it waits through two complete timeout/retry cycles before a source appears, then removes and restores the source, verifies both audio and video recover, and verifies the NDI sender instance is retained. It uses local UDP port 25300 and writes `test-results/recovery-test.txt`.

**More… → Choose FFmpeg…** selects an alternative `ffmpeg.exe`. Release build scripts do not copy the local FFmpeg binary into the package. FFmpeg was obtained from the Windows build provider linked on FFmpeg's download page. Its license is included with the executable. NDI is dynamically loaded from the user's installation. The installer build includes the official NDI Runtime redistributable separately. Review FFmpeg's GPL distribution obligations and NDI's SDK terms before distributing a binary package to others.

References:

- [NDI configuration settings](https://docs.ndi.video/all/developing-with-ndi/sdk/configuration-files)
- [Windows NDI Access Manager](https://docs.ndi.video/all/using-ndi/ndi-tools/ndi-tools-for-windows/access-manager)
- [NDI SDK and runtime distribution](https://docs.ndi.video/all/developing-with-ndi/sdk/software-distribution)
- [FFmpeg downloads](https://ffmpeg.org/download.html)
- [FFmpeg RTP/UDP protocol options](https://ffmpeg.org/ffmpeg-protocols.html)

Activity is hidden by default; click **Activity** to open the log. **More…** contains the guide and optional FFmpeg selection.


Run `--ten-stream-test` for ten simultaneous 640×360/25 fps UDP MPEG-TS sources with 48 kHz stereo audio and ten NDI receivers. It uses ports 25400–25418 and writes `test-results/ten-stream-test.txt`.

## Licences and manual dependency setup

[NDI®](https://ndi.video/) is a registered trademark of Vizrt NDI AB. This independent application is not sponsored or endorsed by NDI. NDI components remain proprietary; the app's MIT licence does not relicense them. Setup presents [third-party terms](installer/THIRD-PARTY-TERMS.txt), and **More → Licences and credits** makes them accessible afterwards. Standard NDI SDK permissions do not cover every appliance or restricted cloud deployment.

From 1.0.2 onward, FFmpeg is downloaded directly from [Gyan's release](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2), not embedded or rehosted in our installer. This is a GPLv3 build with separate licence rights. If automatic downloading fails:

1. Download the **release essentials ZIP** from [Gyan's FFmpeg builds](https://www.gyan.dev/ffmpeg/builds/).
2. Extract it into a permanent folder on your PC.
3. In the app, open **More → Choose FFmpeg…** and select the extracted `bin/ffmpeg.exe`.

Use **More → Download FFmpeg** to open that page. You can also rerun the installer when internet access is available. For deliberately offline setup, `/SKIPFFMPEGDOWNLOAD=1` skips the download; the finishing page gives manual instructions. The official NDI Runtime installer remains embedded, and .NET remains bundled.

[Read the licensing review](docs/LICENSING-REVIEW.md), including the unresolved corresponding-source gap in the withdrawn 1.0.1 binary release. That old installer is no longer offered publicly. Downloading from the provider does not certify its compliance, and codec patent questions depend on jurisdiction and use. Do not redistribute downloaded FFmpeg binaries without fulfilling their applicable source and notice requirements.
