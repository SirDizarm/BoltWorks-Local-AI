/* Copyright (c) 2026 Daniel Rydin and Llama Lite contributors. MIT License. */
#include "llama.h"
#include <windows.h>
#include <psapi.h>

#include <cstdio>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

#include "xp-weight-stream.h"

namespace {
enum class LoadMode { mapped, buffered, staged, automatic };
constexpr int context_tokens = 512;
constexpr int report_reply_tokens = 80;
constexpr int code_reply_tokens = 240;
constexpr int batch_tokens = 32;
constexpr size_t evidence_bytes = 960;

bool starts_with(const char *line, const char *prefix) {
    return std::strncmp(line, prefix, std::strlen(prefix)) == 0;
}

bool evidence_line(const char *line) {
    const char *prefixes[] = {"Input size:", "PE at ", "PE32", "File kind:", "DOS MZ", "No DOS MZ",
                             "Exports directory:", "Imports directory:", "Resources directory:",
                             "CLR directory:", "  DLL:", "0x"};
    for (auto prefix : prefixes) if (starts_with(line, prefix)) return true;
    return false;
}

bool read_evidence(const wchar_t *path, std::string &evidence) {
    std::unique_ptr<FILE, decltype(&std::fclose)> file(_wfopen(path, L"rb"), std::fclose);
    if (!file) return false;
    char line[512];
    if (!std::fgets(line, sizeof(line), file.get()) || !starts_with(line, "BoltWorks Mini REA XP "))
        return false;

    for (unsigned i = 0; i < 256 && std::fgets(line, sizeof(line), file.get()); ++i) {
        if (starts_with(line, "Printable ASCII")) break;
        if (!evidence_line(line)) continue;
        size_t length = std::strcspn(line, "\r\n");
        if (length + 1 > evidence_bytes - evidence.size()) break;
        for (size_t j = 0; j < length; ++j) {
            unsigned char c = line[j];
            evidence += c >= 32 && c <= 126 && c != '<' && c != '>' ? (char)c : ' ';
        }
        evidence += '\n';
    }
    return !evidence.empty() && !std::ferror(file.get());
}

std::string utf8(const wchar_t *value) {
    int size = WideCharToMultiByte(CP_UTF8, 0, value, -1, nullptr, 0, nullptr, nullptr);
    if (size <= 0) return {};
    std::string result(size, '\0');
    if (!WideCharToMultiByte(CP_UTF8, 0, value, -1, result.data(), size, nullptr, nullptr)) return {};
    result.pop_back();
    return result;
}

void write_code(std::string draft) {
    const auto first = draft.find_first_not_of(" \t\r\n");
    const auto last = draft.find_last_not_of(" \t\r\n");
    if (first != std::string::npos && draft.compare(first, 3, "```") == 0 &&
        last >= first + 5 && draft.compare(last - 2, 3, "```") == 0) {
        const auto body = draft.find('\n', first);
        const auto fence = draft.rfind('\n', last - 2);
        if (body != std::string::npos && fence != std::string::npos && fence >= body &&
            draft.find_first_not_of(" \t", fence + 1) == last - 2) {
            draft = draft.substr(body + 1, fence - body);
        }
    }
    std::fwrite(draft.data(), 1, draft.size(), stdout);
}

int generate(const char *path, const std::string &evidence, const char *question, LoadMode &mode) {
    const bool code = evidence.empty();
    const int reply_tokens = code ? code_reply_tokens : report_reply_tokens;
    auto model_params = llama_model_default_params();
    model_params.n_gpu_layers = 0;
    model_params.load_mode = mode == LoadMode::mapped ? LLAMA_LOAD_MODE_MMAP : LLAMA_LOAD_MODE_NONE;
    model_params.lazy_mode = mode == LoadMode::mapped ? LLAMA_LAZY_MODE_ON : LLAMA_LAZY_MODE_OFF;
    model_params.no_alloc = mode == LoadMode::staged || mode == LoadMode::automatic;
    model_params.use_extra_bufts = false;
    std::unique_ptr<llama_model, decltype(&llama_model_free)> model(
        llama_model_load_from_file(path, model_params), llama_model_free);
    if (!model || llama_model_has_encoder(model.get())) {
        std::fputs("Use a compatible decoder-only GGUF. For a split model, choose its first shard.\n", stderr);
        return 1;
    }
    if (mode == LoadMode::automatic) {
        if (model->arch == LLM_ARCH_QWEN2) {
            mode = LoadMode::staged;
        } else {
            model.reset();
            mode = LoadMode::mapped;
            return generate(path, evidence, question, mode);
        }
    }
    std::unique_ptr<XpWeightStream> weights;
    if (mode == LoadMode::staged) weights = std::make_unique<XpWeightStream>(model.get(), path);

    const auto *vocab = llama_model_get_vocab(model.get());
    const auto *chat_template = llama_model_chat_template(model.get(), nullptr);
    std::string message = code ? question : "Mini REA evidence:\n" + evidence + "\nQuestion: " + question;
    const llama_chat_message chat[] = {
        {"system", code ?
            "Write short C99 code that implements the request. Use only the standard C library. If asked for one function, return just that function. Output raw C source with no explanation." :
            "You explain binary inspection reports. Copy the file kind and architecture from the evidence. Imported DLLs are dependencies. Use only facts in the report."},
        {"user", message.c_str()}
    };
    char prompt[4096];
    int size = chat_template ? llama_chat_apply_template(chat_template, chat, 2, true, prompt, sizeof(prompt)) : -1;
    if (size <= 0 || size >= (int)sizeof(prompt)) {
        std::fputs("This runner needs a supported instruction-model chat template.\n", stderr);
        return 1;
    }
    std::vector<llama_token> tokens(context_tokens - reply_tokens);
    int token_count = llama_tokenize(vocab, prompt, size, tokens.data(), (int)tokens.size(), true, true);
    if (token_count <= 0) {
        std::fputs(code ? "The coding task does not fit. Use a shorter task.\n" :
            "The report excerpt and question do not fit. Try a shorter question or a PE-only report.\n", stderr);
        return 2;
    }

    auto params = llama_context_default_params();
    params.n_ctx = context_tokens;
    params.n_batch = batch_tokens;
    params.n_ubatch = batch_tokens;
    params.n_threads = 1;
    params.n_threads_batch = 1;
    if (weights) {
        params.cb_eval = XpWeightStream::evaluate;
        params.cb_eval_user_data = weights.get();
    }
    std::unique_ptr<llama_context, decltype(&llama_free)> context(llama_init_from_model(model.get(), params), llama_free);
    if (!context) return 1;
    std::unique_ptr<llama_batch_ext, decltype(&llama_batch_ext_free)> batch(llama_batch_ext_init(context.get()), llama_batch_ext_free);
    std::unique_ptr<llama_sampler, decltype(&llama_sampler_free)> sampler(llama_sampler_init_greedy(), llama_sampler_free);
    if (!batch || !sampler) return 1;

    auto decode = [&](const llama_token *input, int count, int position, bool logits) {
        llama_batch_ext_clear(batch.get());
        for (int i = 0; i < count; ++i) {
            int index = llama_batch_ext_add_token(batch.get(), 0, input[i]);
            llama_pos token_position = position + i;
            if (index < 0 || !llama_batch_ext_set_pos(batch.get(), index, &token_position)) return false;
        }
        if (logits && !llama_batch_ext_set_output_logits(batch.get(), count - 1, true)) return false;
        return llama_process(context.get(), LLAMA_PROCESS_TYPE_DECODE, batch.get()) == 0;
    };
    for (int position = 0; position < token_count;) {
        int count = token_count - position;
        if (count > batch_tokens) count = batch_tokens;
        if (!decode(tokens.data() + position, count, position, position + count == token_count)) return 1;
        position += count;
    }

    if (!code) {
        std::fputs("Mini REA evidence:\n", stdout);
        std::fputs(evidence.c_str(), stdout);
        std::puts("\nAI interpretation. Check it against the Mini REA report.\n");
    }
    bool finished = false;
    std::string draft;
    for (int i = 0; i < reply_tokens; ++i) {
        llama_token token = llama_sampler_sample(sampler.get(), context.get(), -1);
        if (llama_vocab_is_eog(vocab, token)) { finished = true; break; }
        char piece[256];
        int length = llama_token_to_piece(vocab, token, piece, sizeof(piece), 0, false);
        if (length < 0) return 1;
        if (code) {
            draft.append(piece, (size_t)length);
        } else {
            std::fwrite(piece, 1, (size_t)length, stdout);
            std::fflush(stdout);
        }
        if (i + 1 < reply_tokens && !decode(&token, 1, token_count + i, true)) return 1;
    }
    if (code) write_code(draft);
    std::putchar('\n');
    if (code && !finished) {
        std::fputs("The code draft reached the reply limit and may be incomplete.\n", stderr);
        return 3;
    }
    return std::ferror(stdout) ? 1 : 0;
}

void code_log(enum ggml_log_level level, const char *message, void *) {
    if (level == GGML_LOG_LEVEL_WARN || level == GGML_LOG_LEVEL_ERROR)
        std::fputs(message, stderr);
}
}

int wmain(int argc, wchar_t **argv) {
    if (argc == 1 || (argc == 2 && std::wcscmp(argv[1], L"--help") == 0)) {
        std::puts("REAAI MODEL.GGUF REPORT.TXT [\"QUESTION\"]\nFor split GGUF, supply the first shard and keep all shards together.");
        std::puts("REAAI --code MODEL.GGUF \"SHORT C TASK\" [--buffered | --staged | --auto]");
        return 0;
    }
    if (std::wcscmp(argv[1], L"--code") == 0) {
        const bool buffered = argc == 5 && std::wcscmp(argv[4], L"--buffered") == 0;
        const bool staged = argc == 5 && std::wcscmp(argv[4], L"--staged") == 0;
        const bool automatic = argc == 5 && std::wcscmp(argv[4], L"--auto") == 0;
        if (!(argc == 4 || buffered || staged || automatic)) {
            std::fputs("Use REAAI --code MODEL.GGUF \"SHORT C TASK\" [--buffered | --staged | --auto].\n", stderr);
            return 2;
        }
        const std::string path = utf8(argv[2]), task = utf8(argv[3]);
        if (path.empty() || task.empty() || task.size() > 600) {
            std::fputs("Choose a GGUF model and enter a task up to 600 UTF-8 bytes.\n", stderr);
            return 2;
        }
        const DWORD started = GetTickCount();
        llama_log_set(code_log, nullptr);
        llama_backend_init();
        auto mode = automatic ? LoadMode::automatic : staged ? LoadMode::staged :
            buffered ? LoadMode::buffered : LoadMode::mapped;
        int result = 1;
        try {
            result = generate(path.c_str(), {}, task.c_str(), mode);
        } catch (const std::exception &failure) {
            std::fprintf(stderr, "%s\n", failure.what());
        }
        llama_backend_free();
        PROCESS_MEMORY_COUNTERS memory = {};
        memory.cb = sizeof(memory);
        if (GetProcessMemoryInfo(GetCurrentProcess(), &memory, sizeof(memory))) {
            std::fprintf(stderr, "Peak process working set: %.1f MiB\n", memory.PeakWorkingSetSize / 1048576.0);
        }
        const char *mode_name = mode == LoadMode::staged ? "staged" : mode == LoadMode::buffered ? "buffered" :
            mode == LoadMode::mapped ? "mapped" : "auto (unresolved)";
        std::fprintf(stderr, "Load mode: %s. Elapsed: %.1f seconds. Exit: %d\n",
                     mode_name, (GetTickCount() - started) / 1000.0, result);
        return result;
    }
    const std::string question = argc == 4 ? utf8(argv[3]) :
        "In one sentence, state the file kind, architecture, and imported DLL names shown.";
    if (argc < 3 || argc > 4 || question.size() > 240) {
        std::fputs("Use REAAI MODEL.GGUF REPORT.TXT [\"QUESTION\"] with a question up to 240 bytes.\n", stderr);
        return 2;
    }
    std::string evidence;
    if (!read_evidence(argv[2], evidence)) {
        std::fputs("Provide a text report from Mini REA XP's inspect or pe command.\n", stderr);
        return 2;
    }
    const std::string model = utf8(argv[1]);
    if (model.empty()) {
        std::fputs("Provide a valid model path.\n", stderr);
        return 2;
    }
    llama_backend_init();
    auto mode = LoadMode::mapped;
    int result = generate(model.c_str(), evidence, question.c_str(), mode);
    llama_backend_free();
    return result;
}
