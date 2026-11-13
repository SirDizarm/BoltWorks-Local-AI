/* Copyright (c) 2026 Daniel Rydin and Llama Lite contributors. MIT License. */
#include <windows.h>
#include <commdlg.h>
#include <stdio.h>
#include <wchar.h>

enum { PICK_FILE = 100, PICK_MODEL, INSPECT, SEARCH, ASK, STOP, FILE_PATH, MODEL_PATH,
       QUERY, QUESTION, OUTPUT, STATUS };
enum { POLL_TIMER = 1, PREVIEW_BYTES = 96000 };

static HWND window, file_box, model_box, query_box, question_box, output_box, status;
static HINSTANCE instance;
static HANDLE child;
static int operation;
static unsigned run_number;
static wchar_t home[MAX_PATH], report[MAX_PATH], run[MAX_PATH];
static wchar_t pending_report[MAX_PATH], result_path[MAX_PATH], log_path[MAX_PATH];

static int path_join(wchar_t *result, const wchar_t *directory, const wchar_t *name) {
    if (wcslen(directory) + wcslen(name) + 2 > MAX_PATH) return 0;
    swprintf(result, MAX_PATH, L"%ls\\%ls", directory, name);
    return 1;
}

static int file_exists(const wchar_t *path) {
    DWORD attributes = GetFileAttributesW(path);
    return attributes != INVALID_FILE_ATTRIBUTES && !(attributes & FILE_ATTRIBUTE_DIRECTORY);
}

static void error(const wchar_t *message, DWORD code) {
    wchar_t text[320];
    if (code) swprintf(text, 320, L"%ls Windows error %lu.", message, (unsigned long)code);
    else lstrcpynW(text, message, 320);
    SetWindowTextW(status, text);
}

static HWND control(const wchar_t *kind, const wchar_t *label, DWORD style, DWORD ex,
                    int x, int y, int w, int h, int id) {
    HWND item = CreateWindowExW(ex, kind, label, WS_CHILD | WS_VISIBLE | style,
        x, y, w, h, window, (HMENU)(INT_PTR)id, instance, NULL);
    SendMessageW(item, WM_SETFONT, (WPARAM)GetStockObject(DEFAULT_GUI_FONT), TRUE);
    return item;
}

static HWND edit(int y, DWORD style, int id) {
    return control(L"EDIT", L"", WS_TABSTOP | ES_AUTOHSCROLL | style,
        WS_EX_CLIENTEDGE, 104, y, 320, 23, id);
}

static void update_buttons(void) {
    const int ids[] = { PICK_FILE, PICK_MODEL, INSPECT, SEARCH, ASK, STOP };
    for (unsigned i = 0; i < sizeof(ids) / sizeof(ids[0]); ++i) {
        int id = ids[i];
        BOOL enabled = id == STOP ? child != NULL : child == NULL;
        if (id == ASK && !report[0]) enabled = FALSE;
        EnableWindow(GetDlgItem(window, id), enabled);
    }
}

static void pick(HWND field, int model) {
    wchar_t path[MAX_PATH] = L"";
    OPENFILENAMEW dialog = {0};
    dialog.lStructSize = sizeof(dialog);
    dialog.hwndOwner = window;
    dialog.lpstrFilter = model ? L"GGUF models\0*.gguf\0All files\0*.*\0" :
        L"Executables and libraries\0*.exe;*.dll\0All files\0*.*\0";
    dialog.lpstrFile = path;
    dialog.nMaxFile = MAX_PATH;
    dialog.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
    if (!GetOpenFileNameW(&dialog)) return;
    SetWindowTextW(field, path);
    if (!model) {
        report[0] = L'\0';
        SetWindowTextW(output_box, L"");
        SetWindowTextW(status, L"File selected. Inspect it or search its embedded text.");
    }
    update_buttons();
}

/* Quote one argument for the Windows C runtime, without using a command shell. */
static wchar_t *append_argument(wchar_t *out, const wchar_t *arg) {
    *out++ = L' ';
    *out++ = L'"';
    while (*arg) {
        unsigned slashes = 0;
        while (*arg == L'\\') { ++slashes; ++arg; }
        if (*arg == L'"' || !*arg) slashes *= 2;
        while (slashes--) *out++ = L'\\';
        if (*arg == L'"') *out++ = L'\\';
        if (*arg) *out++ = *arg++;
    }
    *out++ = L'"';
    *out = L'\0';
    return out;
}

static HANDLE output_file(const wchar_t *path, SECURITY_ATTRIBUTES *security) {
    return CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, security, CREATE_NEW,
                       FILE_ATTRIBUTE_NORMAL, NULL);
}

static void launch(int action) {
    wchar_t target[MAX_PATH], model[MAX_PATH], input[241], exe[MAX_PATH], name[80];
    wchar_t command[4096] = L"", *end = command;
    GetWindowTextW(file_box, target, MAX_PATH);
    GetWindowTextW(model_box, model, MAX_PATH);
    GetWindowTextW(action == SEARCH ? query_box : question_box, input, 241);
    if (!file_exists(action == ASK ? report : target)) {
        error(action == ASK ? L"Inspect the file first." : L"Choose a readable file first.", 0);
        return;
    }
    if (action == ASK && !file_exists(model)) { error(L"Choose a GGUF model first.", 0); return; }
    if (action == SEARCH && !input[0]) { error(L"Enter the text to find.", 0); return; }
    if (!path_join(exe, home, action == ASK ? L"REAAI.EXE" : L"MINIREA.EXE") || !file_exists(exe)) {
        error(L"Keep MINIREA.EXE and REAAI.EXE beside this program.", 0);
        return;
    }
    swprintf(name, 80, L"MINIREA-%08lX-%lu-%u", (unsigned long)GetTickCount(),
             (unsigned long)GetCurrentProcessId(), ++run_number);
    if (!path_join(run, home, name) || !path_join(pending_report, run, L"REPORT.TXT") ||
        !path_join(result_path, run, L"RESULTS.TXT") || !path_join(log_path, run, L"MODEL.LOG")) {
        error(L"Move Mini REA to a shorter folder path.", 0);
        return;
    }
    if (!CreateDirectoryW(run, NULL)) {
        error(L"Could not create a results folder beside Mini REA.", GetLastError());
        return;
    }
    end = append_argument(end, exe);
    if (action == ASK) {
        end = append_argument(end, model);
        end = append_argument(end, report);
        if (input[0]) append_argument(end, input);
    } else {
        end = append_argument(end, action == SEARCH ? L"strings" : L"inspect");
        end = append_argument(end, target);
        append_argument(end, action == SEARCH ? input : pending_report);
    }

    SECURITY_ATTRIBUTES security = {sizeof(security), NULL, TRUE};
    HANDLE out = output_file(result_path, &security);
    HANDLE err = action == ASK ? output_file(log_path, &security) : out;
    HANDLE in = CreateFileW(L"NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                           &security, OPEN_EXISTING, 0, NULL);
    DWORD failure = 0;
    if (out == INVALID_HANDLE_VALUE || err == INVALID_HANDLE_VALUE || in == INVALID_HANDLE_VALUE) {
        failure = ERROR_OPEN_FAILED;
    } else {
        STARTUPINFOW startup = {0};
        PROCESS_INFORMATION process;
        startup.cb = sizeof(startup);
        startup.dwFlags = STARTF_USESTDHANDLES;
        startup.hStdInput = in;
        startup.hStdOutput = out;
        startup.hStdError = err;
        if (CreateProcessW(exe, command + 1, NULL, NULL, TRUE, CREATE_NO_WINDOW,
                           NULL, home, &startup, &process)) {
            child = process.hProcess;
            CloseHandle(process.hThread);
        } else {
            failure = GetLastError();
        }
    }
    if (in != INVALID_HANDLE_VALUE) CloseHandle(in);
    if (err != INVALID_HANDLE_VALUE && err != out) CloseHandle(err);
    if (out != INVALID_HANDLE_VALUE) CloseHandle(out);
    if (failure) { error(L"Could not start the test or open its logs.", failure); return; }

    operation = action;
    if (action == INSPECT) report[0] = L'\0';
    SetWindowTextW(output_box, L"");
    SetWindowTextW(status, action == ASK ? L"Running the local model. This may take a few minutes."
                                        : L"Reading the selected file...");
    update_buttons();
    if (!SetTimer(window, POLL_TIMER, 200, NULL)) {
        TerminateProcess(child, 2);
        CloseHandle(child);
        child = NULL;
        update_buttons();
        error(L"Could not start the status timer.", 0);
    }
}

static void show_output(const wchar_t *path) {
    static char bytes[PREVIEW_BYTES];
    static wchar_t text[PREVIEW_BYTES + 100];
    FILE *file = _wfopen(path, L"rb");
    if (!file) { SetWindowTextW(output_box, L"Could not open the output file."); return; }
    size_t length = fread(bytes, 1, sizeof(bytes), file);
    int more = fgetc(file) != EOF;
    fclose(file);
    int count = MultiByteToWideChar(CP_UTF8, 0, bytes, (int)length, text, PREVIEW_BYTES);
    text[count] = L'\0';
    if (more) wcscat(text, L"\r\n[Preview shortened. Open the saved file for the full output.]");
    SetWindowTextW(output_box, text);
}

static void complete(void) {
    DWORD code = 1;
    if (WaitForSingleObject(child, 0) == WAIT_TIMEOUT) return;
    GetExitCodeProcess(child, &code);
    CloseHandle(child);
    child = NULL;
    KillTimer(window, POLL_TIMER);
    if (!code && operation == INSPECT && file_exists(pending_report)) lstrcpyW(report, pending_report);
    const wchar_t *view = !code && operation == INSPECT ? pending_report :
        code && operation == ASK ? log_path : result_path;
    show_output(view);
    wchar_t message[360];
    swprintf(message, 360, L"Exit code %lu. Saved beside Mini REA in %ls",
             (unsigned long)code, wcsrchr(run, L'\\') + 1);
    SetWindowTextW(status, message);
    update_buttons();
}

static LRESULT CALLBACK handle_message(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam) {
    switch (message) {
    case WM_CREATE: {
        window = hwnd;
        control(L"STATIC", L"File to inspect", 0, 0, 12, 15, 90, 18, 0);
        file_box = edit(12, ES_READONLY, FILE_PATH);
        control(L"BUTTON", L"Browse...", WS_TABSTOP, 0, 434, 12, 88, 24, PICK_FILE);
        control(L"BUTTON", L"Inspect", WS_TABSTOP, 0, 530, 12, 86, 24, INSPECT);
        control(L"STATIC", L"Search text", 0, 0, 12, 48, 90, 18, 0);
        query_box = edit(45, 0, QUERY);
        SendMessageW(query_box, EM_SETLIMITTEXT, 128, 0);
        control(L"BUTTON", L"Search", WS_TABSTOP, 0, 434, 45, 88, 24, SEARCH);
        control(L"STATIC", L"GGUF model", 0, 0, 12, 81, 90, 18, 0);
        model_box = edit(78, ES_READONLY, MODEL_PATH);
        control(L"BUTTON", L"Browse...", WS_TABSTOP, 0, 434, 78, 88, 24, PICK_MODEL);
        control(L"STATIC", L"Question", 0, 0, 12, 114, 90, 18, 0);
        question_box = edit(111, 0, QUESTION);
        SendMessageW(question_box, EM_SETLIMITTEXT, 240, 0);
        control(L"BUTTON", L"Ask AI", WS_TABSTOP, 0, 434, 111, 88, 24, ASK);
        control(L"BUTTON", L"Stop", WS_TABSTOP, 0, 530, 111, 86, 24, STOP);
        output_box = control(L"EDIT", L"", WS_TABSTOP | ES_MULTILINE | ES_READONLY |
            ES_AUTOVSCROLL | ES_AUTOHSCROLL | WS_VSCROLL | WS_HSCROLL, WS_EX_CLIENTEDGE, 12, 147, 604, 290, OUTPUT);
        SendMessageW(output_box, WM_SETFONT, (WPARAM)GetStockObject(ANSI_FIXED_FONT), TRUE);
        SendMessageW(output_box, EM_SETLIMITTEXT, PREVIEW_BYTES + 100, 0);
        status = control(L"STATIC", L"Choose a file, then inspect it or search its embedded text.",
            0, 0, 12, 447, 604, 20, STATUS);
        wchar_t path[MAX_PATH];
        if (path_join(path, home, L"VAULT.EXE") && file_exists(path)) SetWindowTextW(file_box, path);
        if (path_join(path, home, L"SmolLM2-135M-Instruct-Q4_K_M.gguf") && file_exists(path))
            SetWindowTextW(model_box, path);
        update_buttons();
        return 0;
    }
    case WM_SIZE:
        if (output_box && wparam != SIZE_MINIMIZED) {
            int width = LOWORD(lparam), height = HIWORD(lparam);
            MoveWindow(output_box, 12, 147, width - 24, height - 177, TRUE);
            MoveWindow(status, 12, height - 24, width - 24, 20, TRUE);
        }
        return 0;
    case WM_GETMINMAXINFO:
        ((MINMAXINFO *)lparam)->ptMinTrackSize = (POINT){636, 335};
        return 0;
    case WM_COMMAND:
        switch (LOWORD(wparam)) {
        case STOP: if (child) TerminateProcess(child, 3); return 0;
        case PICK_FILE: if (!child) pick(file_box, 0); return 0;
        case PICK_MODEL: if (!child) pick(model_box, 1); return 0;
        case INSPECT: case SEARCH: case ASK: if (!child) launch(LOWORD(wparam)); return 0;
        }
        break;
    case WM_TIMER: if (wparam == POLL_TIMER && child) complete(); return 0;
    case WM_DESTROY:
        if (child) { TerminateProcess(child, 3); CloseHandle(child); }
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, message, wparam, lparam);
}

int WINAPI wWinMain(HINSTANCE module, HINSTANCE previous, PWSTR args, int show) {
    (void)previous;
    (void)args;
    instance = module;
    DWORD length = GetModuleFileNameW(NULL, home, MAX_PATH);
    if (!length || length >= MAX_PATH) return 1;
    wchar_t *slash = wcsrchr(home, L'\\');
    if (!slash) return 1;
    *slash = L'\0';
    WNDCLASSW kind = {0};
    kind.lpfnWndProc = handle_message;
    kind.hInstance = module;
    kind.hCursor = LoadCursorW(NULL, IDC_ARROW);
    kind.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
    kind.lpszClassName = L"BoltWorksMiniREAXP";
    if (!RegisterClassW(&kind)) return 1;
    HWND main_window = CreateWindowW(kind.lpszClassName, L"BoltWorks Mini REA XP",
        WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN, CW_USEDEFAULT, CW_USEDEFAULT, 680, 500,
        NULL, NULL, module, NULL);
    if (!main_window) return 1;
    ShowWindow(main_window, show);
    MSG msg;
    int result;
    while ((result = GetMessageW(&msg, NULL, 0, 0)) > 0) {
        if (!IsDialogMessageW(main_window, &msg)) {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
    return result < 0 ? 1 : (int)msg.wParam;
}
