# v3.1 Cloud Build

This version moves the heavyweight Windows driver build away from the local PC.

## What GitHub Actions builds

The workflow `.github/workflows/build-driver.yml` runs on GitHub's `windows-2025` hosted runner and:

1. Checks out this project.
2. Checks out Microsoft's official Windows-driver-samples repository including submodules.
3. Builds the unmodified `audio/sysvad/sysvad.sln` baseline as Release/x64.
4. Builds the Discord TTS app as a self-contained Windows x64 executable.
5. Uploads both results as GitHub Actions artifacts.

## Important milestone boundary

The SysVAD artifact produced by this workflow is deliberately the **clean Microsoft baseline**.
It proves the cloud WDK/MSBuild toolchain works before our custom virtual microphone endpoint is patched in.

It is NOT yet the final `Discord TTS Virtual Microphone` driver.

The next engineering step after this workflow succeeds is to apply the contents of `src/DriverOverlay` into a dedicated SysVAD-derived driver project and connect its WaveRT capture path to the app-fed PCM ring buffer.

## Browser-only use

You do not need Visual Studio or WDK on the local PC.

1. Create a new empty GitHub repository.
2. Upload the contents of this folder, including the hidden `.github` folder.
3. Open the repository's **Actions** tab.
4. Select **Build Discord TTS Virtual Audio**.
5. Choose **Run workflow**.
6. Wait for the Windows build job to finish.
7. Download the artifacts from the workflow run page:
   - `sysvad-baseline-x64`
   - `DiscordTtsMic-app-win-x64`
   - `build-info`

No local 18 GB Visual Studio/WDK installation is needed for this build.
