param(
    [Parameter(Mandatory=$true)][string]$LlamaSource,
    [Parameter(Mandatory=$true)][string]$ToolchainBin,
    [Parameter(Mandatory=$true)][string]$BuildDirectory,
    [ValidateRange(1,32)][int]$Jobs = 4
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $LlamaSource).Path
$toolchain = (Resolve-Path -LiteralPath $ToolchainBin).Path
$loader = Join-Path $source 'src\llama-model-loader.cpp'
$oldThreshold = 'constexpr size_t auto_min_size = 4ull * 1024 * 1024 * 1024;'
$newThreshold = 'constexpr uint64_t auto_min_size = 4ull * 1024 * 1024 * 1024;'
$loaderText = [IO.File]::ReadAllText($loader)
if ($loaderText.Contains($oldThreshold)) {
    # Keep the upstream 4 GiB threshold from wrapping to zero on 32-bit hosts.
    [IO.File]::WriteAllText($loader,$loaderText.Replace($oldThreshold,$newThreshold))
} elseif (!$loaderText.Contains($newThreshold)) {
    throw 'Use the llama.cpp b11669 source tree.'
}

# A native CMake invocation can report the build host's AMD64 architecture.
$buildInfo = Join-Path $source 'cmake\build-info.cmake'
$buildInfoText = [IO.File]::ReadAllText($buildInfo)
$hostLabel = 'set(BUILD_TARGET "${CMAKE_SYSTEM_NAME} ${CMAKE_SYSTEM_PROCESSOR}")'
if ($buildInfoText.Contains($hostLabel)) {
    [IO.File]::WriteAllText($buildInfo,$buildInfoText.Replace($hostLabel,'set(BUILD_TARGET "Windows x86")'))
}

$previousPath = $env:Path
try {
    $env:Path = "$toolchain;$previousPath"
    $compilerTarget = & (Join-Path $toolchain 'gcc.exe') -dumpmachine
    if ($LASTEXITCODE -ne 0 -or $compilerTarget -ne 'i686-w64-mingw32') {
        throw 'The toolchain must target 32-bit x86 Windows.'
    }

    $cmake = Join-Path $toolchain 'cmake.exe'
    $flags = '-march=pentium4 -mfpmath=sse -D_WIN32_WINNT=0x0A00 -DWINVER=0x0A00'
    $configure = @(
        '-S',$source,'-B',$BuildDirectory,'-G','Ninja',
        '-DCMAKE_BUILD_TYPE=Release','-DCMAKE_C_COMPILER=gcc','-DCMAKE_CXX_COMPILER=g++',
        "-DCMAKE_C_FLAGS=$flags","-DCMAKE_CXX_FLAGS=$flags",
        '-DCMAKE_EXE_LINKER_FLAGS=-static -Wl,--large-address-aware',
        '-DBUILD_SHARED_LIBS=OFF','-DGGML_NATIVE=OFF','-DGGML_SSE42=OFF',
        '-DGGML_AVX=OFF','-DGGML_AVX2=OFF','-DGGML_BMI2=OFF','-DGGML_FMA=OFF',
        '-DGGML_F16C=OFF','-DGGML_OPENMP=OFF','-DGGML_LLAMAFILE=OFF','-DGGML_CCACHE=OFF',
        '-DLLAMA_OPENSSL=OFF','-DLLAMA_BUILD_TESTS=OFF','-DLLAMA_BUILD_EXAMPLES=OFF',
        '-DLLAMA_BUILD_APP=OFF','-DLLAMA_BUILD_UI=OFF',
        '-DLLAMA_BUILD_NUMBER=11669','-DLLAMA_BUILD_COMMIT=620d2225c-x86'
    )
    & $cmake @configure
    if ($LASTEXITCODE -ne 0) { throw 'x86 runtime configuration failed.' }
    & $cmake --build $BuildDirectory --target llama-server llama-gguf-split --parallel $Jobs
    if ($LASTEXITCODE -ne 0) { throw 'x86 runtime build failed.' }
} finally {
    $env:Path = $previousPath
}
