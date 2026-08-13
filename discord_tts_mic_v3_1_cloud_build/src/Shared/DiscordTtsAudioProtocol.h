#pragma once
#include <ntddk.h>

// User-mode writer opens \\.\DiscordTtsVirtualAudio and writes interleaved PCM16.
// v3 alpha fixed format: 48,000 Hz, mono, signed little-endian 16-bit.
#define DTM_SAMPLE_RATE 48000
#define DTM_CHANNELS 1
#define DTM_BITS_PER_SAMPLE 16
#define DTM_RING_BYTES (DTM_SAMPLE_RATE * DTM_CHANNELS * (DTM_BITS_PER_SAMPLE/8) * 2) // 2 sec
