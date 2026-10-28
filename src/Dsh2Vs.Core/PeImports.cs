using System.Runtime.InteropServices;
using System.Text;

namespace Dsh2Vs.Core;

/// <summary>
/// Reads a PE image's import table so startup failures can be explained before a debugger is even
/// started: a missing dependency (STATUS_DLL_NOT_FOUND, 0xC0000135) otherwise looks like
/// "the program exited immediately" with an empty console.
/// </summary>
public static class PeImports
{
    public static IReadOnlyList<string> ReadImportedDlls(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        using var reader = new BinaryReader(stream);

        if (stream.Length < 0x40)
        {
            return [];
        }

        stream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        if (peOffset <= 0 || peOffset + 24 > stream.Length)
        {
            return [];
        }

        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550) // "PE\0\0"
        {
            return [];
        }

        reader.ReadUInt16(); // machine
        var sectionCount = reader.ReadUInt16();
        reader.ReadUInt32(); // timestamp
        reader.ReadUInt32(); // symbol table
        reader.ReadUInt32(); // symbol count
        var optionalHeaderSize = reader.ReadUInt16();
        reader.ReadUInt16(); // characteristics

        var optionalHeaderStart = stream.Position;
        var magic = reader.ReadUInt16();
        var pe32Plus = magic == 0x20b;

        // Data directory index 1 = import table. Its offset inside the optional header depends on
        // PE32 vs PE32+ because the image base field is wider on 64-bit.
        var dataDirectoriesStart = optionalHeaderStart + (pe32Plus ? 112 : 96);
        stream.Position = dataDirectoriesStart + 8; // skip export directory (index 0)
        var importRva = reader.ReadUInt32();
        var importSize = reader.ReadUInt32();

        if (importRva == 0 || importSize == 0)
        {
            return [];
        }

        var sectionsStart = optionalHeaderStart + optionalHeaderSize;
        var sections = new List<(uint VirtualAddress, uint VirtualSize, uint RawPointer, uint RawSize)>();
        stream.Position = sectionsStart;
        for (var i = 0; i < sectionCount; i++)
        {
            var name = reader.ReadBytes(8);
            _ = name;
            var virtualSize = reader.ReadUInt32();
            var virtualAddress = reader.ReadUInt32();
            var rawSize = reader.ReadUInt32();
            var rawPointer = reader.ReadUInt32();
            reader.ReadUInt32(); // relocations
            reader.ReadUInt32(); // line numbers
            reader.ReadUInt16(); // relocation count
            reader.ReadUInt16(); // line number count
            reader.ReadUInt32(); // characteristics
            sections.Add((virtualAddress, virtualSize, rawPointer, rawSize));
        }

        long RvaToOffset(uint rva)
        {
            foreach (var section in sections)
            {
                var size = Math.Max(section.VirtualSize, section.RawSize);
                if (rva >= section.VirtualAddress && rva < section.VirtualAddress + size)
                {
                    return section.RawPointer + (rva - section.VirtualAddress);
                }
            }

            return -1;
        }

        var importOffset = RvaToOffset(importRva);
        if (importOffset < 0)
        {
            return [];
        }

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        stream.Position = importOffset;

        while (stream.Position + 20 <= stream.Length)
        {
            _ = reader.ReadUInt32(); // original first thunk
            _ = reader.ReadUInt32(); // timestamp
            _ = reader.ReadUInt32(); // forwarder chain
            var nameRva = reader.ReadUInt32();
            _ = reader.ReadUInt32(); // first thunk

            if (nameRva == 0)
            {
                break;
            }

            var nameOffset = RvaToOffset(nameRva);
            if (nameOffset < 0 || nameOffset >= stream.Length)
            {
                break;
            }

            var position = stream.Position;
            stream.Position = nameOffset;
            var bytes = new List<byte>();
            int b;
            while ((b = stream.ReadByte()) > 0 && bytes.Count < 260)
            {
                bytes.Add((byte)b);
            }

            stream.Position = position;

            var name = Encoding.ASCII.GetString(bytes.ToArray());
            if (name.Length > 0 && seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }
}

/// <summary>
/// Resolves an image's imports the way the loader would: the image's own directory first,
/// then the normal search path (application directory, System32, PATH).
/// </summary>
public static class DependencyResolver
{
    private const uint LoadLibraryAsDatafile = 0x00000002;
    private const uint LoadLibraryAsImageResource = 0x00000020;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    public static (IReadOnlyList<string> Resolved, IReadOnlyList<string> Missing) Check(string imagePath)
    {
        var full = Path.GetFullPath(imagePath);
        var directory = Path.GetDirectoryName(full) ?? Environment.CurrentDirectory;

        var resolved = new List<string>();
        var missing = new List<string>();

        foreach (var dll in PeImports.ReadImportedDlls(full))
        {
            // API sets are contract names resolved by the loader through the apiset schema, not files
            // sitting in a directory. Probing them by name produces false "missing" reports.
            if (dll.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase) ||
                dll.StartsWith("ext-ms-win-", StringComparison.OrdinalIgnoreCase))
            {
                resolved.Add(dll);
                continue;
            }

            var local = Path.Combine(directory, dll);
            if (File.Exists(local))
            {
                resolved.Add(dll);
                continue;
            }

            var system = Path.Combine(Environment.SystemDirectory, dll);
            if (File.Exists(system))
            {
                resolved.Add(dll);
                continue;
            }

            var handle = LoadLibraryExW(dll, IntPtr.Zero, LoadLibraryAsDatafile | LoadLibraryAsImageResource);
            if (handle == IntPtr.Zero)
            {
                missing.Add(dll);
                continue;
            }

            FreeLibrary(handle);
            resolved.Add(dll);
        }

        return (resolved, missing);
    }
}
