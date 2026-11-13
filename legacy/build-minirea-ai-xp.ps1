param(
    [Parameter(Mandatory=$true)][string]$LlamaSource,
    [Parameter(Mandatory=$true)][string]$WorkDirectory,
    [string]$ToolchainBin = (Join-Path $PSScriptRoot '..\tools\win-x86-build\w64devkit\bin'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist\BoltWorks-Mini-REA-XP-AI-0.2'),
    [ValidateRange(1,32)][int]$Jobs = 4
)

$ErrorActionPreference = 'Stop'
$upstream = (Resolve-Path -LiteralPath $LlamaSource).Path
$toolchain = (Resolve-Path -LiteralPath $ToolchainBin).Path
New-Item -ItemType Directory -Path $WorkDirectory,$OutputDirectory -Force | Out-Null
$work = (Resolve-Path -LiteralPath $WorkDirectory).Path
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$source = Join-Path $work 'llama-xp-source'
$build = Join-Path $work 'llama-xp-core'

function Replace-UpstreamText([string]$File,[string]$Before,[string]$After) {
    $path = Join-Path $source $File
    $text = [IO.File]::ReadAllText($path).Replace("`r`n","`n")
    $Before = $Before.Replace("`r`n","`n")
    $After = $After.Replace("`r`n","`n")
    if (!$text.Contains($Before)) { throw "Unexpected upstream text in $File. Use llama.cpp b11669." }
    [IO.File]::WriteAllText($path,$text.Replace($Before,$After))
}

if (Test-Path -LiteralPath $source) { throw 'Choose a fresh work directory for the patched runtime source.' }
Copy-Item -LiteralPath $upstream -Destination $source -Recurse
$cpu = 'ggml/src/ggml-cpu/ggml-cpu.c'
Replace-UpstreamText $cpu 'typedef HANDLE pthread_t;' @'
#if _WIN32_WINNT < 0x0600
#include <pthread.h>
#include <sched.h>
typedef void * thread_ret_t;
#else
typedef HANDLE pthread_t;
'@
Replace-UpstreamText $cpu "    Sleep (0);`n    return 0;`n}`n#else" "    Sleep (0);`n    return 0;`n}`n#endif`n#else"
Replace-UpstreamText $cpu "#if defined(_WIN32)`n`ntypedef CONDITION_VARIABLE" "#if defined(_WIN32) && _WIN32_WINNT >= 0x0600`n`ntypedef CONDITION_VARIABLE"
Replace-UpstreamText 'ggml/src/ggml.c' @'
void ggml_time_init(void) {
    static INIT_ONCE once = INIT_ONCE_STATIC_INIT;
    InitOnceExecuteOnce(&once, ggml_time_init_once, NULL, NULL);
}
'@ @'
#if defined(__MINGW32__) && _WIN32_WINNT < 0x0600
#include <pthread.h>
static void ggml_time_init_xp(void) {
    ggml_time_init_once(NULL, NULL, NULL);
}
void ggml_time_init(void) {
    static pthread_once_t once = PTHREAD_ONCE_INIT;
    pthread_once(&once, ggml_time_init_xp);
}
#else
void ggml_time_init(void) {
    static INIT_ONCE once = INIT_ONCE_STATIC_INIT;
    InitOnceExecuteOnce(&once, ggml_time_init_once, NULL, NULL);
}
#endif
'@
Replace-UpstreamText 'src/llama-model-loader.cpp' 'constexpr size_t auto_min_size = 4ull * 1024 * 1024 * 1024;' 'constexpr uint64_t auto_min_size = 4ull * 1024 * 1024 * 1024;'

$previousPath = $env:Path
try {
    $env:Path = "$toolchain;$previousPath"
    $compilerTarget = & (Join-Path $toolchain 'gcc.exe') -dumpmachine
    if ($LASTEXITCODE -ne 0 -or $compilerTarget -ne 'i686-w64-mingw32') { throw 'Use the x86 MinGW-w64 compiler.' }
    $cmake = Join-Path $toolchain 'cmake.exe'
    $flags = '-march=pentium4 -mfpmath=sse -D_WIN32_WINNT=0x0501 -DWINVER=0x0501'
    $options = @(
        '-S',$source,'-B',$build,'-G','Ninja','-DCMAKE_BUILD_TYPE=Release',
        '-DCMAKE_C_COMPILER=gcc','-DCMAKE_CXX_COMPILER=g++',"-DCMAKE_C_FLAGS=$flags","-DCMAKE_CXX_FLAGS=$flags",
        '-DBUILD_SHARED_LIBS=OFF','-DGGML_NATIVE=OFF','-DGGML_SSE42=OFF','-DGGML_AVX=OFF','-DGGML_AVX2=OFF',
        '-DGGML_BMI2=OFF','-DGGML_FMA=OFF','-DGGML_F16C=OFF','-DGGML_OPENMP=OFF','-DGGML_LLAMAFILE=OFF','-DGGML_CCACHE=OFF',
        '-DLLAMA_BUILD_COMMON=OFF','-DLLAMA_BUILD_TOOLS=OFF','-DLLAMA_BUILD_SERVER=OFF','-DLLAMA_BUILD_TESTS=OFF',
        '-DLLAMA_BUILD_EXAMPLES=OFF','-DLLAMA_BUILD_APP=OFF','-DLLAMA_OPENSSL=OFF'
    )
    & $cmake @options
    if ($LASTEXITCODE -ne 0) { throw 'XP llama.cpp configuration failed.' }
    & $cmake --build $build --target llama --parallel $Jobs
    if ($LASTEXITCODE -ne 0) { throw 'XP llama.cpp core build failed.' }
    & (Join-Path $toolchain 'g++.exe') -std=c++17 -Os -Wall -Wextra -march=pentium4 -mfpmath=sse `
        -D_WIN32_WINNT=0x0501 -DWINVER=0x0501 -static -s -municode `
        '-Wl,--major-os-version,5,--minor-os-version,1,--major-subsystem-version,5,--minor-subsystem-version,1' `
        "-I$source/include" "-I$source/ggml/include" "-I$source/src" (Join-Path $PSScriptRoot 'minirea-ai.cpp') `
        "$build/src/libllama.a" "$build/ggml/src/ggml.a" "$build/ggml/src/ggml-cpu.a" "$build/ggml/src/ggml-base.a" `
        -lwinpthread -lpsapi -o (Join-Path $output 'REAAI.EXE')
    if ($LASTEXITCODE -ne 0) { throw 'REAAI link failed.' }
    & (Join-Path $PSScriptRoot 'build-gguf-split-xp.ps1') -LlamaSource $source `
        -GgmlBase "$build/ggml/src/ggml-base.a" -WorkDirectory $build -OutputDirectory $output -ToolchainBin $toolchain
    & (Join-Path $PSScriptRoot 'build-minirea-xp.ps1') -ToolchainBin $toolchain -OutputDirectory $output
    & (Join-Path $toolchain 'gcc.exe') -std=c11 -Os -Wall -Wextra -march=i586 -mfpmath=387 -mno-sse -mno-sse2 `
        -D_WIN32_WINNT=0x0501 -DWINVER=0x0501 -static -s -municode -mwindows `
        '-Wl,--major-os-version,5,--minor-os-version,1,--major-subsystem-version,5,--minor-subsystem-version,1' `
        (Join-Path $PSScriptRoot 'minirea-xp-ui.c') -lcomdlg32 -lgdi32 -o (Join-Path $output 'BoltWorks-Mini-REA-XP.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Mini REA window compilation failed.' }
} finally {
    $env:Path = $previousPath
}
