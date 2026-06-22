using System;
using System.Runtime.InteropServices;

namespace YaraXWrapper;

public sealed class Rules : IDisposable
{
    internal IntPtr _pointer = IntPtr.Zero;

    internal Rules(IntPtr rulesPtr)
    {
        _pointer = rulesPtr;
    }

    internal Rules()
    {
        _pointer = IntPtr.Zero;
    }

    public int Count => YaraXNative.yrx_rules_count(_pointer);

    internal void Import(byte[] buffer)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Length == 0) throw new YaraXException("Import buffer is empty.");

        YRX_RESULT result = YaraXNative.yrx_rules_deserialize(buffer, buffer.LongLength, out _pointer);
        if (result != YRX_RESULT.YRX_SUCCESS)
        {
            throw new YaraXException($"Import failed: {result}");
        }
    }

    internal byte[] Export()
    {
        YRX_RESULT result = YaraXNative.yrx_rules_serialize(_pointer, out IntPtr bufferPtr);
        if (result != YRX_RESULT.YRX_SUCCESS)
        {
            throw new YaraXException($"Export failed: {result}");
        }

        YRX_BUFFER buffer = Marshal.PtrToStructure<YRX_BUFFER>(bufferPtr);
        byte[] bytes = new byte[(int)buffer.length];
        Marshal.Copy(buffer.data, bytes, 0, bytes.Length);
        YaraXNative.yrx_buffer_destroy(bufferPtr);

        return bytes;
    }

    public void Dispose()
    {
        if (_pointer == IntPtr.Zero)
        {
            return;
        }

        YaraXNative.yrx_rules_destroy(_pointer);
        _pointer = IntPtr.Zero;
    }
}
