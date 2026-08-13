#include "DiscordTtsRingBuffer.h"

#define DTM_POOL_TAG 'mTDD'

NTSTATUS DtmRingInitialize(PDTM_RING_BUFFER Ring, ULONG Capacity)
{
    if (!Ring || Capacity == 0) return STATUS_INVALID_PARAMETER;
    RtlZeroMemory(Ring, sizeof(*Ring));
#if (NTDDI_VERSION >= NTDDI_WIN10_VB)
    Ring->Data = (PUCHAR)ExAllocatePool2(POOL_FLAG_NON_PAGED, Capacity, DTM_POOL_TAG);
#else
    Ring->Data = (PUCHAR)ExAllocatePoolWithTag(NonPagedPoolNx, Capacity, DTM_POOL_TAG);
#endif
    if (!Ring->Data) return STATUS_INSUFFICIENT_RESOURCES;
    Ring->Capacity = Capacity;
    KeInitializeSpinLock(&Ring->Lock);
    return STATUS_SUCCESS;
}

VOID DtmRingDestroy(PDTM_RING_BUFFER Ring)
{
    if (!Ring) return;
    if (Ring->Data) ExFreePoolWithTag(Ring->Data, DTM_POOL_TAG);
    RtlZeroMemory(Ring, sizeof(*Ring));
}

VOID DtmRingReset(PDTM_RING_BUFFER Ring)
{
    if (!Ring || !Ring->Data) return;
    KIRQL oldIrql;
    KeAcquireSpinLock(&Ring->Lock, &oldIrql);
    Ring->ReadIndex = Ring->WriteIndex = Ring->Count = 0;
    KeReleaseSpinLock(&Ring->Lock, oldIrql);
}

ULONG DtmRingWriteOverwriteOldest(PDTM_RING_BUFFER Ring, const UCHAR* Buffer, ULONG Length)
{
    if (!Ring || !Ring->Data || !Buffer || Length == 0) return 0;
    KIRQL oldIrql;
    KeAcquireSpinLock(&Ring->Lock, &oldIrql);
    ULONG written = 0;
    for (ULONG i = 0; i < Length; ++i) {
        if (Ring->Count == Ring->Capacity) {
            Ring->ReadIndex = (Ring->ReadIndex + 1) % Ring->Capacity;
            Ring->Count--;
        }
        Ring->Data[Ring->WriteIndex] = Buffer[i];
        Ring->WriteIndex = (Ring->WriteIndex + 1) % Ring->Capacity;
        Ring->Count++;
        written++;
    }
    KeReleaseSpinLock(&Ring->Lock, oldIrql);
    return written;
}

ULONG DtmRingReadOrSilence(PDTM_RING_BUFFER Ring, UCHAR* Buffer, ULONG Length)
{
    if (!Buffer || Length == 0) return 0;
    RtlZeroMemory(Buffer, Length);
    if (!Ring || !Ring->Data) return Length;

    KIRQL oldIrql;
    KeAcquireSpinLock(&Ring->Lock, &oldIrql);
    ULONG available = min(Length, Ring->Count);
    for (ULONG i = 0; i < available; ++i) {
        Buffer[i] = Ring->Data[Ring->ReadIndex];
        Ring->ReadIndex = (Ring->ReadIndex + 1) % Ring->Capacity;
        Ring->Count--;
    }
    KeReleaseSpinLock(&Ring->Lock, oldIrql);
    return Length; // caller always receives a complete audio buffer; missing bytes remain silence
}
