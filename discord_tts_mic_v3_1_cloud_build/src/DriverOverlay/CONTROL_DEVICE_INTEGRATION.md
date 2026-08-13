# v3.1 SysVAD control-device integration

The app already writes fixed-format PCM to `\\.\DiscordTtsVirtualAudio`. The SysVAD derivative must expose that symbolic link and copy each `IRP_MJ_WRITE` payload into `DTM_RING_BUFFER`.

## Adapter-owned state
Add one `DTM_RING_BUFFER` to the adapter/common object (or another object whose lifetime matches the audio adapter). Initialize it with `DTM_RING_BYTES` during adapter start and destroy it during teardown.

## Control device
Create a secure named control device and symbolic link:

- NT device name: `\\Device\\DiscordTtsVirtualAudio`
- DOS symbolic link: `\\DosDevices\\DiscordTtsVirtualAudio`
- user-mode path: `\\.\\DiscordTtsVirtualAudio`

The write dispatch must:

1. obtain the system buffer (`DO_BUFFERED_IO` is simplest for v3.1),
2. reject zero-length writes,
3. accept arbitrary byte counts,
4. push bytes with `DtmRingWriteOverwriteOldest`,
5. set `Irp->IoStatus.Information` to the accepted byte count,
6. complete the request with `STATUS_SUCCESS`.

Do not block the writer waiting for the capture stream. When the ring is full, overwrite the oldest unread data.

## Capture stream
The stock SysVAD sample can generate synthetic capture data. Replace that generation point for the selected microphone endpoint with:

```cpp
DtmRingReadOrSilence(&AdapterCommon->DiscordTtsRing, destination, bytesRequested);
```

The helper fills underruns with zeroes, so Discord sees a continuous microphone stream instead of a stalled capture pin.

## Format for the first milestone
Expose one host capture format first:

- 48,000 Hz
- 1 channel
- signed PCM 16-bit little-endian

Once the endpoint is proven in Discord, more Windows-friendly formats can be added without changing the app/driver transport.

## Security
This alpha is intended for local development. Use an ACL appropriate for a local interactive app rather than exposing a world-writable kernel control path in a production build.
