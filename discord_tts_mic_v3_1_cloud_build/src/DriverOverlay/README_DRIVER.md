# Discord TTS Virtual Microphone driver overlay

This directory is the **v3 driver integration layer**. The production driver should be built from Microsoft's current `Windows-driver-samples/audio/sysvad` source, then reduced to a single capture endpoint and extended with a user-mode PCM ingress control device.

Target behavior:

- Endpoint shown to Windows/Discord: **Discord TTS Virtual Microphone**
- Capture format: 48 kHz, mono, PCM16 (expandable later)
- User-mode control device: `\\.\DiscordTtsVirtualAudio`
- `DiscordTtsMic.exe` continuously writes 10 ms PCM frames to the control device.
- The WaveRT capture stream consumes the same ring buffer. On underrun, return silence.
- Ring buffer writes overwrite the oldest unread data instead of blocking the GUI/TTS process.

## Why this is an overlay rather than copied SysVAD source

Microsoft maintains SysVAD as the reference WDM/WaveRT virtual audio sample. Keeping their upstream tree separate makes it much easier to rebase onto the matching Visual Studio / WDK version instead of freezing a large vendor sample inside this app repository.

## Required implementation points in SysVAD

1. Keep one microphone/capture topology only.
2. Rename endpoint strings to `Discord TTS Virtual Microphone`.
3. Add a non-PnP control device/symbolic link `\\DosDevices\\DiscordTtsVirtualAudio`.
4. Implement `IRP_MJ_WRITE` for PCM ingress into a nonpaged circular buffer.
5. Replace sample tone generation in the capture stream with reads from that ring buffer.
6. Return zero-filled samples on underrun.
7. Reset ring indices on stream reset/power transition.
8. Package as a componentized audio driver for modern Windows.

The app-side protocol header is in `../Shared/DiscordTtsAudioProtocol.h`.
