using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace YaraXWrapper;

public sealed class Compiler : IDisposable
{
    private IntPtr _compiler = IntPtr.Zero;

    static Compiler()
    {
        if (!Environment.Is64BitProcess)
            throw new PlatformNotSupportedException(
                "YaraXWrapper requires a 64-bit process. " +
                "Set <PlatformTarget>x64</PlatformTarget> in the consuming project, " +
                "or uncheck 'Prefer 32-bit' in project properties.");
    }

    public Compiler(CompileFlags flags = CompileFlags.None)
    {
        YRX_RESULT result = YaraXNative.yrx_compiler_create((uint)flags, out _compiler);
        if (result != YRX_RESULT.YRX_SUCCESS)
        {
            throw new YaraXException($"Failed to create compiler: {result}");
        }
    }

    public void AddRuleFile(string filePath)
    {
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new YaraXException($"Rule file does not exist: {fullPath}");
        }

        string directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        using var dir = new Utf8NativeStr(directory);
        YRX_RESULT includeResult = YaraXNative.yrx_compiler_add_include_dir(_compiler, dir);
        if (includeResult != YRX_RESULT.YRX_SUCCESS)
        {
            throw new YaraXException(
                $"AddRuleFile: failed to register include directory '{directory}': {includeResult}");
        }

        string source = File.ReadAllText(fullPath, Encoding.UTF8);
        using var src = new Utf8NativeStr(source);
        using var origin = new Utf8NativeStr(fullPath);
        YRX_RESULT result = YaraXNative.yrx_compiler_add_source_with_origin(_compiler, src, origin);

        if (result != YRX_RESULT.YRX_SUCCESS && result != YRX_RESULT.YRX_SYNTAX_ERROR)
        {
            throw new YaraXException($"AddRuleFile failed for '{fullPath}': {result}");
        }
    }

    public void AddRule(string source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        using var src = new Utf8NativeStr(source);
        YRX_RESULT result = YaraXNative.yrx_compiler_add_source(_compiler, src);

        if (result != YRX_RESULT.YRX_SUCCESS && result != YRX_RESULT.YRX_SYNTAX_ERROR)
        {
            throw new YaraXException($"AddRule failed: {result}");
        }
    }

    public void AddIncludeDir(string directory)
    {
        if (directory == null) throw new ArgumentNullException(nameof(directory));
        using var dir = new Utf8NativeStr(directory);
        YRX_RESULT result = YaraXNative.yrx_compiler_add_include_dir(_compiler, dir);
        if (result != YRX_RESULT.YRX_SUCCESS)
        {
            throw new YaraXException($"AddIncludeDir failed: {result}");
        }
    }

    public void IgnoreModule(string module)
    {
        using var mod = new Utf8NativeStr(module);
        YRX_RESULT result = YaraXNative.yrx_compiler_ignore_module(_compiler, mod);
        if (result != YRX_RESULT.YRX_SUCCESS)
        {
            throw new YaraXException($"IgnoreModule failed: {result}");
        }
    }

    public void SetNamespace(string name)
    {
        using var ns = new Utf8NativeStr(name);
        YRX_RESULT result = YaraXNative.yrx_compiler_new_namespace(_compiler, ns);
        if (result != YRX_RESULT.YRX_SUCCESS)
        {
            throw new YaraXException($"SetNamespace failed: {result}");
        }
    }

    public CompileResult Build()
    {
        CompileError[] errors = ReadDiagnosticsJson(isErrors: true);
        CompileError[] warnings = ReadDiagnosticsJson(isErrors: false);

        IntPtr rulesPtr = YaraXNative.yrx_compiler_build(_compiler);
        _compiler = IntPtr.Zero;

        return new CompileResult(new Rules(rulesPtr), errors, warnings);
    }

    public void Dispose()
    {
        if (_compiler == IntPtr.Zero)
        {
            return;
        }

        YaraXNative.yrx_compiler_destroy(_compiler);
        _compiler = IntPtr.Zero;
    }

    private CompileError[] ReadDiagnosticsJson(bool isErrors)
    {
        YRX_RESULT callResult = isErrors
            ? YaraXNative.yrx_compiler_errors_json(_compiler, out IntPtr bufferPtr)
            : YaraXNative.yrx_compiler_warnings_json(_compiler, out bufferPtr);

        if (callResult != YRX_RESULT.YRX_SUCCESS)
        {
            return Array.Empty<CompileError>();
        }

        YRX_BUFFER buffer = Marshal.PtrToStructure<YRX_BUFFER>(bufferPtr);
        CompileError[] result = ParseDiagnosticsBuffer(buffer);
        YaraXNative.yrx_buffer_destroy(bufferPtr);

        return result;
    }

    private static CompileError[] ParseDiagnosticsBuffer(YRX_BUFFER buffer)
    {
        if (buffer.length.ToUInt64() <= 2)
        {
            return Array.Empty<CompileError>();
        }

        byte[] bytes = new byte[(int)buffer.length];
        Marshal.Copy(buffer.data, bytes, 0, bytes.Length);

        try
        {
            CompileError[]? parsed = JsonSerializer.Deserialize<CompileError[]>(Encoding.UTF8.GetString(bytes));
            return parsed ?? Array.Empty<CompileError>();
        }
        catch
        {
            return Array.Empty<CompileError>();
        }
    }
}