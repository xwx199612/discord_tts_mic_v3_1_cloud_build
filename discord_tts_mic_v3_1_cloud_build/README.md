# Discord TTS Microphone v3.1 Cloud Build

This package changes the v3.1 driver bring-up workflow so the heavyweight Windows driver toolchain runs in **GitHub Actions**, not on your local PC.

## Goal

Final target:

```text
Physical microphone -----> gain/duck ----\
                                      mixer ---> 48 kHz mono PCM ---> virtual audio driver ---> Discord
Text ---> Windows TTS ---> resample/gain /
```

Discord will ultimately select:

**Discord TTS Virtual Microphone**

VB-CABLE and VoiceMeeter are not intended runtime dependencies.

## What changed in this package

- Added `.github/workflows/build-driver.yml`.
- Uses a GitHub-hosted Windows runner to build the Microsoft SysVAD baseline.
- Builds the portable Discord TTS app in the same workflow.
- Uploads build products as GitHub Actions artifacts.
- Local Visual Studio / Windows SDK / WDK installation is no longer required for this milestone.
- Existing local scripts are retained only as optional/reference tools.

## Use the cloud build

See:

`cloud/README_CLOUD_BUILD.md`

Short version:

1. Create an empty GitHub repository.
2. Upload this folder's **contents**, including `.github`.
3. Go to **Actions**.
4. Run **Build Discord TTS Virtual Audio**.
5. Download the build artifacts after the job succeeds.

## App

The current app side includes:

- WASAPI physical microphone capture.
- Windows TTS.
- 48 kHz mono PCM16 TTS conversion.
- Mic + TTS mixing.
- Independent gain.
- TTS microphone ducking.
- 10 ms PCM frame pump.
- `\\.\DiscordTtsVirtualAudio` bridge client.

## Driver milestone

This workflow intentionally builds the **unmodified Microsoft SysVAD baseline first**. That is the correct validation point for the cloud toolchain.

The source under `src/DriverOverlay` contains the project-specific protocol/ring-buffer work for the following milestone, but it is not yet patched into Microsoft's SysVAD tree by this package.

The next target after a successful cloud baseline is:

```text
DiscordTtsMic.exe
      |
      v
\\.\DiscordTtsVirtualAudio
      |
      v
PCM ring buffer
      |
      v
WaveRT capture stream
      |
      v
Discord TTS Virtual Microphone
      |
      v
Discord
```
