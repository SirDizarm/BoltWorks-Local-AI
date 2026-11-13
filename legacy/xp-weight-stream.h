/* Copyright (c) 2026 Daniel Rydin and Llama Lite contributors. MIT License. */
#pragma once

#include "gguf.h"
#include "llama-model.h"
#include <io.h>
#include <stdexcept>

// CPU-only Qwen2 experiment against the pinned llama.cpp b11669 graph.
// Ordinary tensors keep their original GGML kernels. Matrix inputs get
// one temporary, read-only file view for the duration of their graph op.
class XpWeightStream {
    struct FileMapping {
        HANDLE handle;
        uint64_t length;
        FileMapping(HANDLE mapping, uint64_t bytes) : handle(mapping), length(bytes) {}
        ~FileMapping() { CloseHandle(handle); }
    };
    struct Span {
        const FileMapping *file;
        uint64_t offset;
        size_t bytes;
        ggml_type type;
    };
    struct Slot {
        Span span;
        std::vector<unsigned char> resident;
    };

    std::vector<std::unique_ptr<FileMapping>> files;
    std::unordered_map<std::string, Span> index;
    std::unordered_map<ggml_tensor *, Slot> weights;
    DWORD granularity = 0;
    ggml_tensor *active = nullptr;
    void *view = nullptr;
    size_t largest_view = 0;
    size_t resident_bytes = 0;
    unsigned view_count = 0;

    void load_index(const char *path, uint16_t &parts, uint16_t part) {
        const int count = MultiByteToWideChar(CP_UTF8, 0, path, -1, nullptr, 0);
        if (!count) throw std::runtime_error("Invalid GGUF path.");
        std::wstring wide(count, L'\0');
        if (!MultiByteToWideChar(CP_UTF8, 0, path, -1, wide.data(), count))
            throw std::runtime_error("Invalid GGUF path.");
        std::unique_ptr<FILE, decltype(&std::fclose)> file(_wfopen(wide.c_str(), L"rb"), std::fclose);
        if (!file) throw std::runtime_error(std::string("Could not open GGUF shard: ") + path);
        std::unique_ptr<gguf_context, decltype(&gguf_free)> metadata(
            gguf_init_from_file_ptr(file.get(), {true, nullptr}), gguf_free);
        if (!metadata) throw std::runtime_error("Could not read GGUF metadata.");

        const auto key = gguf_find_key(metadata.get(), "split.count");
        if (key >= 0) {
            const auto no = gguf_find_key(metadata.get(), "split.no");
            if (no < 0 || gguf_get_kv_type(metadata.get(), key) != GGUF_TYPE_UINT16 ||
                gguf_get_kv_type(metadata.get(), no) != GGUF_TYPE_UINT16 ||
                gguf_get_val_u16(metadata.get(), no) != part)
                throw std::runtime_error("Start with the first GGUF shard.");
            uint16_t total = gguf_get_val_u16(metadata.get(), key);
            if (total < 1 || (part && parts != total)) throw std::runtime_error("Mismatched GGUF shards.");
            parts = total;
        } else if (part) {
            throw std::runtime_error("Missing GGUF shard metadata.");
        }

        HANDLE input = (HANDLE)_get_osfhandle(_fileno(file.get()));
        LARGE_INTEGER length;
        if (!GetFileSizeEx(input, &length) || length.QuadPart <= 0)
            throw std::runtime_error("Could not read the GGUF file size.");
        auto mapping = std::make_unique<FileMapping>(
            CreateFileMappingW(input, nullptr, PAGE_READONLY, 0, 0, nullptr), length.QuadPart);
        if (!mapping->handle) throw std::runtime_error("Could not create a read-only GGUF mapping.");
        auto *opened = mapping.get();
        files.push_back(std::move(mapping));
        for (int64_t i = 0; i < gguf_get_n_tensors(metadata.get()); ++i) {
            const uint64_t offset = (uint64_t)gguf_get_data_offset(metadata.get()) +
                gguf_get_tensor_offset(metadata.get(), i);
            const size_t bytes = gguf_get_tensor_size(metadata.get(), i);
            const char *name = gguf_get_tensor_name(metadata.get(), i);
            if (!bytes || offset > opened->length || bytes > opened->length - offset ||
                !index.emplace(name, Span{opened, offset, bytes, gguf_get_tensor_type(metadata.get(), i)}).second)
                throw std::runtime_error(std::string("Invalid or duplicated GGUF tensor: ") + name);
        }
    }

    const unsigned char *map(const Span &span) {
        const uint64_t start = span.offset - span.offset % granularity;
        const size_t skip = (size_t)(span.offset - start);
        // The first prototype refuses any single matrix that needs a bigger view.
        constexpr size_t view_limit = 256u * 1024 * 1024;
        if (skip >= view_limit || span.bytes > view_limit - skip)
            throw std::runtime_error("A weight matrix exceeds the 256 MiB staged view limit.");
        const size_t length = span.bytes + skip;
        view = MapViewOfFile(span.file->handle, FILE_MAP_READ, (DWORD)(start >> 32),
                             (DWORD)start, length);
        if (!view) throw std::runtime_error("Could not map the next weight matrix. Windows error " +
                                            std::to_string(GetLastError()) + ".");
        largest_view = (std::max)(largest_view, length);
        ++view_count;
        return static_cast<const unsigned char *>(view) + skip;
    }

    void release_view() {
        if (active) {
            active->data = &weights.at(active);
            active = nullptr;
        }
        if (view) {
            UnmapViewOfFile(view);
            view = nullptr;
        }
    }

public:
    XpWeightStream(llama_model *model, const char *path) {
        if (model->arch != LLM_ARCH_QWEN2)
            throw std::runtime_error("Staged weights currently support dense Qwen2/Qwen2.5 GGUF models.");
        SYSTEM_INFO system;
        GetSystemInfo(&system);
        granularity = system.dwAllocationGranularity;
        uint16_t parts = 1;
        load_index(path, parts, 0);
        if (parts > 1) {
            std::vector<char> prefix(std::strlen(path) + 1), filename(std::strlen(path) + 32);
            if (!llama_split_prefix(prefix.data(), prefix.size(), path, 0, parts))
                throw std::runtime_error("Keep the original numbered GGUF filenames.");
            for (uint16_t i = 1; i < parts; ++i) {
                if (!llama_split_path(filename.data(), filename.size(), prefix.data(), i, parts))
                    throw std::runtime_error("Could not construct the GGUF shard name.");
                load_index(filename.data(), parts, i);
            }
        }

        for (const auto &item : model->tensors_by_name) {
            ggml_tensor *tensor = item.second;
            const auto found = index.find(item.first);
            if (found == index.end() || found->second.bytes != ggml_nbytes(tensor) ||
                found->second.type != tensor->type)
                throw std::runtime_error("Missing or incompatible weight: " + item.first);
            const Span &span = found->second;
            auto &slot = weights.emplace(tensor, Slot{span, {}}).first->second;
            if (ggml_is_matrix(tensor) && tensor->ne[1] > 1) {
                // A unique non-null marker keeps the graph allocator from
                // treating this externally supplied weight as a scratch tensor.
                tensor->data = &slot;
            } else {
                slot.resident.resize(span.bytes);
                std::memcpy(slot.resident.data(), map(span), span.bytes);
                release_view();
                tensor->data = slot.resident.data();
                resident_bytes += span.bytes;
            }
        }
        // The loader used a metadata-only pass with CPU buffer descriptors.
        // Inference still allocates its normal KV cache and compute buffers.
        model->hparams.no_alloc = false;
    }

    ~XpWeightStream() {
        release_view();
        std::fprintf(stderr, "Staged weights: peak view %.1f MiB, resident vectors %.1f MiB, %u views.\n",
                     largest_view / 1048576.0, resident_bytes / 1048576.0, view_count);
    }

    static bool evaluate(ggml_tensor *node, bool before, void *opaque) {
        auto &self = *static_cast<XpWeightStream *>(opaque);
        if (!before) { self.release_view(); return true; }
        for (int i = 0; i < GGML_MAX_SRC; ++i) {
            auto *source = node->src[i];
            if (!source) continue;
            auto *base = source->view_src ? source->view_src : source;
            auto it = self.weights.find(base);
            if (it == self.weights.end() || !it->second.resident.empty()) continue;
            if (i != 0 || base != source || (node->op != GGML_OP_MUL_MAT && node->op != GGML_OP_GET_ROWS))
                throw std::runtime_error("This GGUF needs an unsupported staged weight operation.");
            self.active = source;
            source->data = const_cast<unsigned char *>(self.map(it->second.span));
        }
        return self.active != nullptr;
    }
};
