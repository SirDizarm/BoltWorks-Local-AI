/* Copyright (c) 2026 Daniel Rydin and Llama Lite contributors. MIT License. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <fcntl.h>
#include <io.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

enum { BLOCK = 65536, LOOKAHEAD = 128, MAX_FINDINGS = 256, PREVIEW = 160 };
static unsigned char buffer[BLOCK + LOOKAHEAD];
static FILE *report;

// Keep MinGW's SSE2 diagnostic callback out of the pre-SSE2 executable.
int xp_matherr(struct _exception *error) __asm__("__matherr");
int xp_matherr(struct _exception *error) { (void)error; return 0; }

typedef struct { HANDLE handle; uint64_t length; } Input;

static uint16_t le16(const unsigned char *p) {
    return (uint16_t)(p[0] | ((unsigned)p[1] << 8));
}

static uint32_t le32(const unsigned char *p) {
    return le16(p) | ((uint32_t)le16(p + 2) << 16);
}

static uint32_t be32(const unsigned char *p) {
    return ((uint32_t)p[0] << 24) | ((uint32_t)p[1] << 16) | ((uint32_t)p[2] << 8) | p[3];
}

static int in_file(const Input *in, uint64_t offset, uint64_t size) {
    return offset <= in->length && size <= in->length - offset;
}

static int read_at(const Input *in, uint64_t offset, void *destination, DWORD size) {
    LARGE_INTEGER position;
    DWORD read;
    if (!in_file(in, offset, size)) return 0;
    position.QuadPart = (LONGLONG)offset;
    return SetFilePointerEx(in->handle, position, NULL, FILE_BEGIN) &&
           ReadFile(in->handle, destination, size, &read, NULL) && read == size;
}

static DWORD bounded(uint64_t value, DWORD limit) {
    return value < limit ? (DWORD)value : limit;
}

static int io_error(const char *operation) {
    fprintf(stderr, "%s failed (Windows error %lu).\n", operation, (unsigned long)GetLastError());
    return 1;
}

static const char *machine_name(uint16_t machine) {
    switch (machine) {
        case 0x014c: return "x86";
        case 0x8664: return "x64";
        case 0x01c0: case 0x01c4: return "ARM";
        case 0xaa64: return "ARM64";
        default: return "other";
    }
}

typedef struct { uint32_t rva, raw, size; } Section;

static int map_rva(const Input *in, const Section *sections, unsigned count,
                   uint32_t rva, uint64_t *offset, uint64_t *available) {
    for (unsigned i = 0; i < count; ++i) {
        const Section *section = sections + i;
        if (rva < section->rva || rva - section->rva >= section->size) continue;
        uint32_t delta = rva - section->rva;
        *offset = (uint64_t)section->raw + delta;
        *available = section->size - delta;
        return in_file(in, *offset, *available);
    }
    return 0;
}

static void list_imports(const Input *in, const Section *sections, unsigned count,
                         uint32_t rva, uint32_t size) {
    uint64_t offset, available;
    if (!map_rva(in, sections, count, rva, &offset, &available)) {
        fputs("Import table RVA could not be mapped to file bytes.\n", report);
        return;
    }
    unsigned descriptors = bounded(available < size ? available : size, MAX_FINDINGS * 20) / 20;
    for (unsigned i = 0; i < descriptors; ++i) {
        unsigned char item[20], name[128];
        if (!read_at(in, offset + i * 20, item, sizeof(item))) break;
        uint32_t name_rva = le32(item + 12);
        if (name_rva == 0) break;
        uint64_t name_offset, name_available;
        if (!map_rva(in, sections, count, name_rva, &name_offset, &name_available)) {
            fputs("  Import name RVA could not be mapped.\n", report);
            continue;
        }
        DWORD bytes = bounded(name_available, sizeof(name));
        if (!read_at(in, name_offset, name, bytes)) break;
        unsigned length = 0;
        while (length < bytes && name[length]) {
            if (name[length] < 32 || name[length] > 126) name[length] = '?';
            ++length;
        }
        fprintf(report, "  DLL: %.*s%s\n", (int)length, (char *)name, length == bytes ? " [unterminated or truncated]" : "");
    }
    if (descriptors == MAX_FINDINGS) fputs("Import listing is capped at 256 descriptors.\n", report);
}

static int inspect_header(const Input *in) {
    unsigned char mz[64], coff[24], optional[240], entry[40];
    if (in->length < 2) { fputs("File too small for an executable header.\n", report); return 0; }
    if (!read_at(in, 0, mz, bounded(in->length, sizeof(mz)))) return io_error("Read header");
    if (memcmp(mz, "MZ", 2) != 0) { fputs("No DOS MZ header.\n", report); return 0; }
    if (in->length < sizeof(mz)) { fputs("MZ header is truncated.\n", report); return 0; }
    uint32_t next = le32(mz + 60);
    if (next < sizeof(mz) || !in_file(in, next, 2)) {
        fputs("DOS MZ header. No usable secondary header offset.\n", report);
        return 0;
    }
    DWORD signature_size = bounded(in->length - next, sizeof(coff));
    if (!read_at(in, next, coff, signature_size)) return io_error("Read secondary header");
    const char *legacy = memcmp(coff, "NE", 2) == 0 ? "NE" :
                         memcmp(coff, "LE", 2) == 0 ? "LE" :
                         memcmp(coff, "LX", 2) == 0 ? "LX" : NULL;
    if (legacy) {
        fprintf(report, "DOS MZ / %s signature at 0x%08lX.\n", legacy, (unsigned long)next);
        return 0;
    }
    if (signature_size < 4 || memcmp(coff, "PE\0\0", 4) != 0) {
        fputs("DOS MZ header. Secondary header is unrecognized.\n", report);
        return 0;
    }
    if (signature_size < sizeof(coff)) { fputs("Truncated PE/COFF header.\n", report); return 0; }
    uint16_t machine = le16(coff + 4), count = le16(coff + 6), optional_size = le16(coff + 20);
    uint64_t optional_offset = (uint64_t)next + sizeof(coff);
    fprintf(report, "PE at 0x%08lX; machine 0x%04X (%s); sections %u.\n",
            (unsigned long)next, (unsigned)machine, machine_name(machine), (unsigned)count);
    uint16_t characteristics = le16(coff + 22);
    fprintf(report, "File kind: %s.\n", (characteristics & 0x2000) ? "DLL" :
            (characteristics & 0x0002) ? "EXE image" : "PE image without executable flag");
    if (count > 96 || !in_file(in, optional_offset, optional_size + (uint64_t)count * 40)) {
        fputs("Invalid or truncated PE section table.\n", report);
        return 0;
    }
    DWORD stored = bounded(optional_size, sizeof(optional));
    if (stored && !read_at(in, optional_offset, optional, stored)) return io_error("Read PE optional header");
    unsigned directory = 0;
    if (stored >= 2) {
        uint16_t magic = le16(optional);
        directory = magic == 0x010b ? 96 : magic == 0x020b ? 112 : 0;
        if (directory && stored >= 70) {
            fprintf(report, "%s; entry RVA 0x%08lX; subsystem %u.\n", directory == 96 ? "PE32" : "PE32+",
                    (unsigned long)le32(optional + 16), (unsigned)le16(optional + 68));
        }
    }
    Section sections[96];
    for (unsigned i = 0; i < count; ++i) {
        if (!read_at(in, optional_offset + optional_size + i * sizeof(entry), entry, sizeof(entry)))
            return io_error("Read PE section");
        sections[i].rva = le32(entry + 12);
        sections[i].size = le32(entry + 16);
        sections[i].raw = le32(entry + 20);
        char name[9] = {0};
        for (unsigned j = 0; j < 8 && entry[j]; ++j)
            name[j] = entry[j] >= 32 && entry[j] <= 126 ? (char)entry[j] : '?';
        fprintf(report, "  Section %-8s RVA 0x%08lX; file 0x%08lX; %lu bytes%s\n", name,
                (unsigned long)sections[i].rva, (unsigned long)sections[i].raw,
                (unsigned long)sections[i].size,
                in_file(in, sections[i].raw, sections[i].size) ? "" : " [outside file]");
    }
    if (!directory || stored < directory) return 0;
    uint32_t directory_count = le32(optional + directory - 4);
    const unsigned indexes[] = {0, 1, 2, 14};
    const char *labels[] = {"Exports", "Imports", "Resources", "CLR"};
    for (unsigned i = 0; i < 4; ++i) {
        unsigned field = directory + 8 * indexes[i];
        if (directory_count <= indexes[i] || stored < field + 8) continue;
        uint32_t rva = le32(optional + field), size = le32(optional + field + 4);
        if (!rva || !size) continue;
        fprintf(report, "%s directory: RVA 0x%08lX; %lu bytes.\n", labels[i], (unsigned long)rva, (unsigned long)size);
        if (indexes[i] == 1) list_imports(in, sections, count, rva, size);
    }
    return 0;
}

static const char *signature(const unsigned char *p, unsigned available) {
    switch (p[0]) {
        case 0x7f: return available >= 4 && memcmp(p + 1, "ELF", 3) == 0 ? "ELF" : NULL;
        case 'h': return available >= 4 && memcmp(p + 1, "sqs", 3) == 0 ? "SquashFS" : NULL;
        case 0x27: return available >= 4 && memcmp(p + 1, "\x05\x19\x56", 3) == 0 ? "U-Boot uImage" : NULL;
        case 'U': return available >= 4 && memcmp(p + 1, "BI#", 3) == 0 ? "UBI erase-count header" : NULL;
        case 'P': return available >= 4 && memcmp(p + 1, "K\x03\x04", 3) == 0 ? "ZIP local header" : NULL;
        case 0x1f: return available >= 3 && memcmp(p + 1, "\x8b\x08", 2) == 0 ? "gzip" : NULL;
        case 0xfd: return available >= 6 && memcmp(p + 1, "7zXZ\0", 5) == 0 ? "XZ" : NULL;
        default: return NULL;
    }
}

static void firmware_detail(const unsigned char *p, unsigned available, uint64_t remaining) {
    if (p[0] == 0x7f && available >= 20 && (p[4] == 1 || p[4] == 2) && (p[5] == 1 || p[5] == 2)) {
        unsigned machine = p[5] == 2 ? ((unsigned)p[18] << 8) | p[19] : le16(p + 18);
        fprintf(report, "  %u-bit %s endian; ELF machine %u", p[4] == 1 ? 32u : 64u,
                p[5] == 2 ? "big" : "little", machine);
    } else if (p[0] == 'h' && available >= 96) {
        uint64_t size = le32(p + 40) | ((uint64_t)le32(p + 44) << 32);
        fprintf(report, "  version %u.%u; declared bytes %I64u (%s)", (unsigned)le16(p + 28),
                (unsigned)le16(p + 30), size, size >= 96 && size <= remaining ? "range in file" : "invalid range");
    } else if (p[0] == 0x27 && available >= 64) {
        uint64_t size = (uint64_t)be32(p + 12) + 64;
        uint32_t crc = UINT32_MAX;
        for (unsigned i = 0; i < 64; ++i) {
            crc ^= i >= 4 && i < 8 ? 0 : p[i];
            for (unsigned bit = 0; bit < 8; ++bit) crc = (crc >> 1) ^ (crc & 1 ? 0xedb88320u : 0);
        }
        fprintf(report, "  header CRC %s; declared bytes %I64u (%s)",
                (crc ^ UINT32_MAX) == be32(p + 4) ? "matches" : "mismatch", size,
                size <= remaining ? "range in file" : "outside file");
    }
    fputc('\n', report);
}

static int scan(const Input *in) {
    unsigned found = 0;
    fputs("Firmware signature candidates (header information only):\n", report);
    for (uint64_t offset = 0; offset < in->length;) {
        DWORD bytes = bounded(in->length - offset, sizeof(buffer));
        if (!read_at(in, offset, buffer, bytes)) return io_error("Read scan block");
        DWORD block = bounded(bytes, BLOCK);
        for (DWORD i = 0; i < block; ++i) {
            const char *kind = signature(buffer + i, bytes - i);
            if (!kind) continue;
            fprintf(report, "0x%016I64X  %s", offset + i, kind);
            firmware_detail(buffer + i, bytes - i, in->length - offset - i);
            if (++found == MAX_FINDINGS) {
                if (offset + i + 1 < in->length) fputs("Scan stopped at the 256-result limit.\n", report);
                return 0;
            }
        }
        offset += block;
    }
    if (!found) fputs("No matching signatures found.\n", report);
    return 0;
}

typedef struct {
    uint64_t offset, length;
    uint64_t match_end;
    unsigned matched;
    int hit;
    char preview[PREVIEW + 1];
} TextRun;

typedef struct {
    char text[129];
    unsigned length, fallback[128];
} Query;

static int printable(unsigned c) { return c >= 32 && c <= 126; }
static unsigned fold(unsigned c) { return c >= 'A' && c <= 'Z' ? c + ('a' - 'A') : c; }

static int prepare_query(Query *query, const wchar_t *text) {
    if (!text) return 1;
    while (*text) {
        if (query->length == 128 || !printable(*text)) return 0;
        query->text[query->length++] = (char)fold(*text++);
    }
    for (unsigned i = 1, n = 0; i < query->length; ++i) {
        while (n && query->text[n] != query->text[i]) n = query->fallback[n - 1];
        if (query->text[n] == query->text[i]) ++n;
        query->fallback[i] = n;
    }
    return 1;
}

static unsigned text_byte(TextRun *run, const Query *query, const char *encoding,
                          int character, uint64_t offset) {
    if (character >= 0) {
        if (run->length == 0) run->offset = offset;
        if (run->length < PREVIEW) run->preview[run->length] = (char)character;
        ++run->length;
        if (query->length && !run->hit) {
            unsigned c = fold((unsigned)character);
            while (run->matched && c != (unsigned)query->text[run->matched])
                run->matched = query->fallback[run->matched - 1];
            if (c == (unsigned)query->text[run->matched]) ++run->matched;
            run->hit = run->matched == query->length;
            if (run->hit) run->match_end = run->length;
        }
        return 0;
    }
    unsigned minimum = query->length && query->length < 4 ? query->length : 4;
    int emit = run->length >= minimum && (!query->length || run->hit);
    if (emit) {
        run->preview[bounded(run->length, PREVIEW)] = 0;
        fprintf(report, "0x%016I64X  %-8s  %s%s", run->offset, encoding, run->preview,
                run->length > PREVIEW ? " [preview truncated]" : "");
        if (query->length) {
            unsigned stride = encoding[0] == 'U' ? 2 : 1;
            fprintf(report, " [match at 0x%016I64X]", run->offset + (run->match_end - query->length) * stride);
        }
        fputc('\n', report);
    }
    run->length = 0;
    run->matched = 0;
    run->hit = 0;
    return (unsigned)emit;
}

static int strings(const Input *in, const Query *query) {
    TextRun ascii = {0}, wide[2] = {{0}, {0}};
    unsigned previous = 0, found = 0;
    fputs("Printable ASCII and UTF-16LE strings:\n", report);
    for (uint64_t offset = 0; offset < in->length;) {
        DWORD bytes = bounded(in->length - offset, BLOCK);
        if (!read_at(in, offset, buffer, bytes)) return io_error("Read string block");
        for (DWORD i = 0; i < bytes; ++i) {
            uint64_t position = offset + i;
            unsigned c = buffer[i];
            found += text_byte(&ascii, query, "ASCII", printable(c) ? (int)c : -1, position);
            if (found == MAX_FINDINGS) goto capped;
            if (position) {
                TextRun *pair = &wide[(position - 1) & 1];
                found += text_byte(pair, query, "UTF16-LE", c == 0 && printable(previous) ? (int)previous : -1, position - 1);
                if (found == MAX_FINDINGS) goto capped;
            }
            previous = c;
        }
        offset += bytes;
    }
    found += text_byte(&ascii, query, "ASCII", -1, in->length);
    for (unsigned i = 0; i < 2 && found < MAX_FINDINGS; ++i)
        found += text_byte(&wide[i], query, "UTF16-LE", -1, in->length);
    if (found == MAX_FINDINGS) goto capped;
    if (!found) fputs("No matching strings found.\n", report);
    return 0;
capped:
    fputs("String output capped at 256 results. Use a more specific query.\n", report);
    return 0;
}

static int decimal(const wchar_t *text, uint64_t *value) {
    *value = 0;
    if (!*text) return 0;
    for (; *text; ++text) {
        if (*text < '0' || *text > '9') return 0;
        unsigned digit = *text - '0';
        if (*value > (UINT64_MAX - digit) / 10) return 0;
        *value = *value * 10 + digit;
    }
    return 1;
}

static int carve(const Input *in, uint64_t offset, uint64_t remaining, const wchar_t *path) {
    if (!remaining || !in_file(in, offset, remaining)) {
        fputs("The requested range must be nonempty and within the input.\n", stderr);
        return 2;
    }
    HANDLE output = CreateFileW(path, GENERIC_WRITE, 0, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (output == INVALID_HANDLE_VALUE) return io_error("Create new output");
    int result = 0;
    while (remaining) {
        DWORD bytes = bounded(remaining, BLOCK), written = 0;
        if (!read_at(in, offset, buffer, bytes) || !WriteFile(output, buffer, bytes, &written, NULL) || written != bytes) {
            fputs("Carve failed. The new output may be incomplete.\n", stderr);
            result = 1;
            break;
        }
        offset += bytes;
        remaining -= bytes;
    }
    if (!CloseHandle(output)) result = io_error("Close carve output");
    if (!result) puts("Carve complete.");
    return result;
}

static int usage(void) {
    puts("BoltWorks Mini REA XP 0.1\n"
         "  MINIREA inspect FILE [NEW_REPORT]\n"
         "  MINIREA pe FILE\n"
         "  MINIREA scan FILE\n"
         "  MINIREA strings FILE [ASCII_QUERY]\n"
         "  MINIREA carve FILE DECIMAL_OFFSET DECIMAL_LENGTH NEW_OUTPUT");
    return 2;
}

int wmain(int argc, wchar_t **argv) {
    if (argc == 1 || (argc == 2 && wcscmp(argv[1], L"--help") == 0)) { usage(); return 0; }
    if (argc < 3) return usage();
    const wchar_t *command = argv[1];
    int inspect = wcscmp(command, L"inspect") == 0, pe = wcscmp(command, L"pe") == 0;
    int firmware = wcscmp(command, L"scan") == 0, text = wcscmp(command, L"strings") == 0;
    int copy = wcscmp(command, L"carve") == 0;
    if (!((inspect || text) && (argc == 3 || argc == 4)) &&
        !((pe || firmware) && argc == 3) && !(copy && argc == 6)) return usage();
    Query query = {0};
    uint64_t offset = 0, length = 0;
    if (text && !prepare_query(&query, argc == 4 ? argv[3] : NULL)) {
        fputs("Use a query of up to 128 printable ASCII characters.\n", stderr);
        return 2;
    }
    if (copy && (!decimal(argv[3], &offset) || !decimal(argv[4], &length))) return usage();

    Input in;
    in.handle = CreateFileW(argv[2], GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (in.handle == INVALID_HANDLE_VALUE) return io_error("Open input");
    LARGE_INTEGER bytes;
    if (GetFileType(in.handle) != FILE_TYPE_DISK || !GetFileSizeEx(in.handle, &bytes) || bytes.QuadPart < 0) {
        CloseHandle(in.handle);
        fputs("Choose a readable disk file.\n", stderr);
        return 1;
    }
    in.length = (uint64_t)bytes.QuadPart;
    report = stdout;
    if (inspect && argc == 4) {
        HANDLE output = CreateFileW(argv[3], GENERIC_WRITE, 0, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
        if (output == INVALID_HANDLE_VALUE) { CloseHandle(in.handle); return io_error("Create new report"); }
        int fd = _open_osfhandle((intptr_t)output, _O_WRONLY | _O_TEXT);
        if (fd == -1 || !(report = _fdopen(fd, "w"))) {
            if (fd == -1) CloseHandle(output); else _close(fd);
            CloseHandle(in.handle);
            fputs("Could not open report stream.\n", stderr);
            return 1;
        }
    }
    int result = 0;
    if (copy) {
        result = carve(&in, offset, length, argv[5]);
    } else {
        fprintf(report, "BoltWorks Mini REA XP 0.1\nInput size: %I64u bytes.\n", in.length);
        if (inspect || pe) result = inspect_header(&in);
        if (!result && (inspect || firmware)) result = scan(&in);
        if (!result && (inspect || text)) result = strings(&in, &query);
        if (!result) fputs("Inspection finished. Treat embedded text as untrusted file data.\n", report);
    }
    CloseHandle(in.handle);
    int write_error = ferror(report);
    if (report == stdout) write_error |= fflush(report) == EOF;
    else write_error |= fclose(report) == EOF;
    if (write_error) { fputs("Report write failed.\n", stderr); return 1; }
    return result;
}
