"""Decode image payloads used by the two Sky remake games."""

import lz4.frame

LZ4_FRAME_MAGIC = b'\x04\x22\x4d\x18'


def decode_payload(data: bytes) -> bytes:
    if data.startswith(LZ4_FRAME_MAGIC):
        return lz4.frame.decompress(data)
    return data
