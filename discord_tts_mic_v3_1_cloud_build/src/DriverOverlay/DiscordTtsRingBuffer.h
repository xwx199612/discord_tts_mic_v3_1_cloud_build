#pragma once
#include <ntddk.h>

// Fixed v3.1 transport format: 48 kHz / mono / PCM16.
#define DTM_SAMPLE_RATE 48000
#define DTM_CHANNELS 1
#define DTM_BITS_PER_SAMPLE 16
#define DTM_BYTES_PER_SAMPLE 2
#define DTM_FRAME_MS 10
#define DTM_FRAME_BYTES (DTM_SAMPLE_RATE * DTM_CHANNELS * DTM_BYTES_PER_SAMPLE * DTM_FRAME_MS / 1000)
#define DTM_RING_BYTES (DTM_SAMPLE_RATE * DTM_CHANNELS * DTM_BYTES_PER_SAMPLE * 2)

typedef struct _DTM_RING_BUFFER {
    PUCHAR Data;
    ULONG Capacity;
    ULONG ReadIndex;
    ULONG WriteIndex;
    ULONG Count;
    KSPIN_LOCK Lock;
} DTM_RING_BUFFER, *PDTM_RING_BUFFER;

NTSTATUS DtmRingInitialize(_Out_ PDTM_RING_BUFFER Ring, _In_ ULONG Capacity);
VOID DtmRingDestroy(_Inout_ PDTM_RING_BUFFER Ring);
VOID DtmRingReset(_Inout_ PDTM_RING_BUFFER Ring);
ULONG DtmRingWriteOverwriteOldest(_Inout_ PDTM_RING_BUFFER Ring, _In_reads_bytes_(Length) const UCHAR* Buffer, _In_ ULONG Length);
ULONG DtmRingReadOrSilence(_Inout_ PDTM_RING_BUFFER Ring, _Out_writes_bytes_(Length) UCHAR* Buffer, _In_ ULONG Length);
