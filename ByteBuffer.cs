using ENet;
using System;

public static class ByteBuffer
{ 
    [ThreadStatic]
    private static byte[] byteBuffer;
    [ThreadStatic]
    private static IntPtr[] pointerBuffer;

    public static byte[] GetByteBuffer()
    {
        if (byteBuffer == null)
            byteBuffer = new byte[64];

        return byteBuffer;
    }

    public static IntPtr[] GetPointerBuffer()
    {
        if (pointerBuffer == null)
            pointerBuffer = new IntPtr[Library.maxPeers];

        return pointerBuffer;
    }
}
