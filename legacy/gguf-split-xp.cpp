/* Copyright (c) 2026 Daniel Rydin and Llama Lite contributors. MIT License. */
#define NOMINMAX
#include <windows.h>
#include "gguf.h"

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

uint64_t xp_gguf_tensor_offset(const gguf_context *, int64_t);
size_t xp_gguf_unpadded_meta_size(const gguf_context *);

namespace {
using Metadata = std::unique_ptr<gguf_context, decltype(&gguf_free)>;
using TensorContext = std::unique_ptr<ggml_context, decltype(&ggml_free)>;
using Input = std::unique_ptr<FILE, decltype(&std::fclose)>;
constexpr size_t copy_buffer_size = 1024 * 1024;
constexpr uint64_t mb = 1000000;
constexpr uint64_t max_target = 3800 * mb;

struct Shard {
    int64_t begin, end;
    uint64_t header_bytes, bytes;
    std::wstring name, temporary;
    bool written = false, published = false;
};

struct Options {
    std::wstring input, directory;
    uint64_t target = 0;
    bool plan = false;
};

// Publish only after all shards are complete. Keep track of exactly which
// paths this invocation created so a failed run can remove its own files.
struct OutputSet {
    std::vector<Shard> shards;
    bool complete = false;

    ~OutputSet() {
        if (complete) return;
        for (const auto &shard : shards) {
            if (shard.published) DeleteFileW(shard.name.c_str());
            else if (shard.written) DeleteFileW(shard.temporary.c_str());
        }
    }
};

class OutputFile {
    HANDLE handle;

public:
    explicit OutputFile(Shard &shard) : handle(CreateFileW(shard.temporary.c_str(),
        GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr)) {
        if (handle == INVALID_HANDLE_VALUE)
            throw std::runtime_error("Could not create a new temporary shard. Windows error " +
                                     std::to_string(GetLastError()) + ".");
        shard.written = true;
    }

    ~OutputFile() { CloseHandle(handle); }

    void write(const void *data, DWORD bytes) {
        DWORD written = 0;
        if (!WriteFile(handle, data, bytes, &written, nullptr) || written != bytes)
            throw std::runtime_error("Could not write the shard. Check free disk space.");
    }

    void flush() {
        if (!FlushFileBuffers(handle))
            throw std::runtime_error("Could not flush the shard to disk.");
    }
};

uint64_t padded(uint64_t bytes) {
    return (bytes + GGUF_DEFAULT_ALIGNMENT - 1) & ~(uint64_t)(GGUF_DEFAULT_ALIGNMENT - 1);
}

uint64_t target_bytes(const wchar_t *value) {
    unsigned size = 0;
    for (; *value >= L'0' && *value <= L'9'; ++value) {
        size = size * 10 + (*value - L'0');
        if (size > max_target / mb) break;
    }
    if (*value || !size || size > max_target / mb) throw std::runtime_error("Use a target from 1 to 3800 MB.");
    return size * mb;
}

Metadata shard_header(const gguf_context *original, unsigned part, unsigned total) {
    Metadata metadata(gguf_init_empty(), gguf_free);
    if (!metadata) throw std::runtime_error("Could not allocate shard metadata.");
    if (!part) {
        gguf_set_kv(metadata.get(), original);
        // Output tensors use default GGUF alignment. Input offsets still
        // come from the parsed source, including any custom alignment.
        if (gguf_get_alignment(original) != GGUF_DEFAULT_ALIGNMENT)
            gguf_remove_key(metadata.get(), "general.alignment");
    }
    gguf_set_val_u16(metadata.get(), "split.no", (uint16_t)part);
    gguf_set_val_i32(metadata.get(), "split.tensors.count", (int32_t)gguf_get_n_tensors(original));
    gguf_set_val_u16(metadata.get(), "split.count", (uint16_t)total);
    return metadata;
}

void write_shard(FILE *input, const gguf_context *original, ggml_context *tensors,
                 Shard &shard, unsigned part, unsigned total, std::vector<unsigned char> &buffer) {
    Metadata metadata = shard_header(original, part, total);
    for (int64_t i = shard.begin; i < shard.end; ++i)
        gguf_add_tensor(metadata.get(), ggml_get_tensor(tensors, gguf_get_tensor_name(original, i)));

    OutputFile output(shard);
    const size_t header_size = gguf_get_meta_size(metadata.get());
    if (header_size != shard.header_bytes) throw std::runtime_error("Shard metadata did not match the split plan.");
    {
        std::vector<unsigned char> header(header_size);
        gguf_get_meta_data(metadata.get(), header.data());
        output.write(header.data(), (DWORD)header.size());
    }
    for (int64_t i = shard.begin; i < shard.end; ++i) {
        const uint64_t offset = (uint64_t)gguf_get_data_offset(original) + xp_gguf_tensor_offset(original, i);
        const size_t bytes = gguf_get_tensor_size(original, i);
        if (_fseeki64(input, offset, SEEK_SET) != 0)
            throw std::runtime_error("Could not seek to a tensor in the original GGUF.");
        size_t remaining = bytes;
        while (remaining) {
            const size_t chunk = (std::min)(remaining, buffer.size());
            if (std::fread(buffer.data(), 1, chunk, input) != chunk)
                throw std::runtime_error("Could not read a complete tensor from the original GGUF.");
            output.write(buffer.data(), (DWORD)chunk);
            remaining -= chunk;
        }
        const unsigned char zeros[GGUF_DEFAULT_ALIGNMENT] = {};
        const DWORD padding = (DWORD)(padded(bytes) - bytes);
        if (padding) output.write(zeros, padding);
    }
    output.flush();
}

std::wstring absolute(const std::wstring &path) {
    wchar_t result[MAX_PATH];
    const DWORD length = GetFullPathNameW(path.c_str(), MAX_PATH, result, nullptr);
    if (!length || length >= MAX_PATH) throw std::runtime_error("Use a shorter, valid path.");
    return result;
}

uint64_t destination_limit(const std::wstring &directory) {
    wchar_t volume[MAX_PATH], filesystem[32];
    if (!GetVolumePathNameW(directory.c_str(), volume, MAX_PATH) ||
        !GetVolumeInformationW(volume, nullptr, 0, nullptr, nullptr, nullptr, filesystem, 32)) {
        std::puts("Destination file system could not be identified. Using a conservative 2000 MB file limit.");
        return 2000 * mb;
    }
    if (!_wcsicmp(filesystem, L"FAT")) return 2000 * mb;
    // The application maximum is already below FAT32's maximum file size.
    return max_target;
}

std::vector<Shard> plan_shards(const std::vector<uint64_t> &descriptors,
                              const std::vector<uint64_t> &weights,
                              uint64_t first_header, uint64_t next_header, uint64_t target) {
    std::vector<Shard> shards;
    int64_t begin = 0;
    uint64_t header = first_header, data = 0;
    auto finish = [&](int64_t end) {
        shards.push_back({begin, end, padded(header), padded(header) + data, {}, {}});
    };
    for (int64_t i = 0; i < (int64_t)weights.size(); ++i) {
        if (padded(header + descriptors[i]) + data + weights[i] > target) {
            finish(i);
            begin = i;
            header = next_header;
            data = 0;
        }
        header += descriptors[i];
        data += weights[i];
    }
    finish(weights.size());
    return shards;
}

void split(const Options &options) {
    const auto path = absolute(options.input);
    if (path.size() < 6 || _wcsicmp(path.c_str() + path.size() - 5, L".gguf"))
        throw std::runtime_error("Choose an original GGUF file.");
    const auto separator = path.find_last_of(L"\\/");
    const auto directory = options.directory.empty() ? path.substr(0, separator + 1) : absolute(options.directory);
    const DWORD attributes = GetFileAttributesW(directory.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES || !(attributes & FILE_ATTRIBUTE_DIRECTORY))
        throw std::runtime_error("Choose an existing destination folder.");
    const uint64_t limit = destination_limit(directory);
    if (options.target > limit) throw std::runtime_error("The requested target exceeds the destination file limit.");

    Input input(_wfopen(path.c_str(), L"rb"), std::fclose);
    if (!input) throw std::runtime_error("Could not open the original GGUF.");
    if (_fseeki64(input.get(), 0, SEEK_END) != 0) throw std::runtime_error("Could not seek in the original GGUF.");
    const int64_t file_bytes = _ftelli64(input.get());
    if (file_bytes <= 0 || _fseeki64(input.get(), 0, SEEK_SET) != 0)
        throw std::runtime_error("Could not read the original GGUF size.");

    ggml_context *tensor_data = nullptr;
    Metadata metadata(gguf_init_from_file_ptr(input.get(), {true, &tensor_data}), gguf_free);
    TensorContext tensors(tensor_data, ggml_free);
    if (!metadata || !tensors) throw std::runtime_error("Could not read GGUF metadata.");
    if (gguf_find_key(metadata.get(), "split.count") >= 0)
        throw std::runtime_error("Choose the original GGUF instead of an existing shard.");
    const int64_t count = gguf_get_n_tensors(metadata.get());
    if (count < 2 || count > INT32_MAX) throw std::runtime_error("This GGUF cannot be split.");

    const auto first_header = xp_gguf_unpadded_meta_size(shard_header(metadata.get(), 0, 0).get());
    const auto next_header = xp_gguf_unpadded_meta_size(shard_header(metadata.get(), 1, 0).get());
    std::vector<uint64_t> descriptors, weights;
    uint64_t largest = padded(first_header);
    for (int64_t i = 0; i < count; ++i) {
        const uint64_t offset = (uint64_t)gguf_get_data_offset(metadata.get()) + xp_gguf_tensor_offset(metadata.get(), i);
        const size_t bytes = gguf_get_tensor_size(metadata.get(), i);
        if (!bytes || offset > (uint64_t)file_bytes || bytes > (uint64_t)file_bytes - offset)
            throw std::runtime_error("A tensor falls outside the original GGUF file.");
        const char *name = gguf_get_tensor_name(metadata.get(), i);
        const auto *tensor = ggml_get_tensor(tensors.get(), name);
        // Name length, dimension count, dimensions, type and 64-bit offset.
        const uint64_t description = 24 + std::strlen(name) + 8 * ggml_n_dims(tensor);
        descriptors.push_back(description);
        weights.push_back(padded(bytes));
        largest = (std::max)(largest, padded(next_header + description) + weights.back());
    }
    if (largest > limit) throw std::runtime_error("A whole tensor or model metadata exceeds the destination file limit.");
    uint64_t target = options.target;
    auto plan_at = [&](uint64_t cap) {
        return plan_shards(descriptors, weights, first_header, next_header, cap);
    };
    if (!target) {
        const uint64_t preferred = (std::min)(1000 * mb, (std::max)(350 * mb, ((uint64_t)file_bytes + 3) / 4));
        const uint64_t first_tensor = padded(first_header + descriptors.front()) + weights.front();
        if (first_tensor <= limit) largest = (std::max)(largest, first_tensor);
        target = (std::max)(preferred, largest);
        const auto desired = ((uint64_t)file_bytes + preferred - 1) / preferred;
        const auto initial_count = plan_at(target).size();
        if (initial_count > desired) {
            uint64_t low = target, high = (std::min)(limit, (std::max)(1000 * mb, largest));
            const auto goal = (std::max)(desired, (uint64_t)plan_at(high).size());
            // Find the smallest size that avoids an extra shard while
            // preserving model order and keeping whole tensors together.
            if (goal < initial_count) {
                while (low < high) {
                    const auto mid = low + (high - low) / 2;
                    if (plan_at(mid).size() <= goal) high = mid;
                    else low = mid + 1;
                }
                target = low;
            }
        }
    }
    if (target < largest) throw std::runtime_error("A whole tensor or model metadata exceeds this target. Use a larger target or automatic sizing.");

    OutputSet output;
    output.shards = plan_at(target);
    const auto total = output.shards.size();
    if (total < 2) {
        std::puts("The model fits in one file at this target. Choose a smaller target to split it.");
        return;
    }
    if (total > UINT16_MAX) throw std::runtime_error("Too many shards. Choose a larger target.");

    std::wstring prefix = directory;
    if (prefix.back() != L'\\' && prefix.back() != L'/') prefix += L'\\';
    prefix += path.substr(separator + 1, path.size() - separator - 6);
    uint64_t required = 0;
    std::printf("%s target: %.2f MB per shard. %u shards planned.\n",
                options.target ? "Requested" : "Automatic", target / (double)mb, (unsigned)total);
    for (size_t i = 0; i < total; ++i) {
        auto &shard = output.shards[i];
        required += shard.bytes;
        std::printf("  %05u: %.2f MB, %u tensors\n", (unsigned)i + 1, shard.bytes / (double)mb,
                    (unsigned)(shard.end - shard.begin));
        wchar_t suffix[40];
        swprintf(suffix, 40, L"-%05u-of-%05u.gguf", (unsigned)i + 1, (unsigned)total);
        shard.name = prefix + suffix;
        shard.temporary = shard.name + L".partial";
        if (shard.temporary.size() >= MAX_PATH) throw std::runtime_error("Move the model or use a shorter destination path.");
        if (!options.plan && (GetFileAttributesW(shard.name.c_str()) != INVALID_FILE_ATTRIBUTES ||
            GetFileAttributesW(shard.temporary.c_str()) != INVALID_FILE_ATTRIBUTES))
            throw std::runtime_error("A shard or partial file already exists. No existing file was overwritten.");
    }
    std::printf("Required free space: %.2f MB.\n", required / (double)mb);
    if (options.plan) return;
    ULARGE_INTEGER free;
    if (GetDiskFreeSpaceExW(directory.c_str(), &free, nullptr, nullptr) && free.QuadPart < required)
        throw std::runtime_error("The destination does not have enough free disk space.");

    std::vector<unsigned char> buffer(copy_buffer_size);
    for (size_t i = 0; i < total; ++i) {
        std::printf("Writing shard %u of %u...\n", (unsigned)i + 1, (unsigned)total);
        std::fflush(stdout);
        write_shard(input.get(), metadata.get(), tensors.get(), output.shards[i],
                    (unsigned)i, (unsigned)total, buffer);
    }
    for (auto &shard : output.shards) {
        if (!MoveFileW(shard.temporary.c_str(), shard.name.c_str()))
            throw std::runtime_error("Could not publish the complete shard set. Windows error " +
                                     std::to_string(GetLastError()) + ".");
        shard.published = true;
    }
    output.complete = true;
    std::printf("Created %u shards. The original model was kept.\n", (unsigned)total);
}
}

int wmain(int argc, wchar_t **argv) {
    if (argc < 2 || std::wcscmp(argv[1], L"--help") == 0) {
        std::puts("GGUF-SPLIT MODEL.GGUF [target-MB] [--out FOLDER] [--plan]\n"
                  "Omit target-MB for automatic sizing. Shard limits include the GGUF headers.");
        return 0;
    }
    try {
        Options options;
        options.input = argv[1];
        for (int i = 2; i < argc; ++i) {
            if (!std::wcscmp(argv[i], L"--plan")) options.plan = true;
            else if (!std::wcscmp(argv[i], L"--out") && i + 1 < argc) options.directory = argv[++i];
            else if (i == 2) options.target = target_bytes(argv[i]);
            else throw std::runtime_error("Unknown option. See GGUF-SPLIT --help.");
        }
        split(options);
        return 0;
    } catch (const std::exception &error) {
        std::fprintf(stderr, "%s\n", error.what());
        return 1;
    }
}
