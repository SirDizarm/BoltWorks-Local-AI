param(
    [Parameter(Mandatory=$true)][string]$LlamaSource,
    [Parameter(Mandatory=$true)][string]$GgmlBase,
    [Parameter(Mandatory=$true)][string]$WorkDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$ToolchainBin = (Join-Path $PSScriptRoot '..\tools\win-x86-build\w64devkit\bin')
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $LlamaSource).Path
$library = (Resolve-Path -LiteralPath $GgmlBase).Path
$toolchain = (Resolve-Path -LiteralPath $ToolchainBin).Path
New-Item -ItemType Directory -Path $WorkDirectory,$OutputDirectory -Force | Out-Null
$generated = Join-Path (Resolve-Path -LiteralPath $WorkDirectory).Path 'gguf-split-large.cpp'
$output = Join-Path (Resolve-Path -LiteralPath $OutputDirectory).Path 'GGUF-SPLIT.EXE'
$script:ggufSource = [IO.File]::ReadAllText((Join-Path $source 'ggml/src/gguf.cpp')).Replace("`r`n", "`n")

function Replace-GgufText([string]$Before, [string]$After) {
    $Before = $Before.Replace("`r`n", "`n")
    $After = $After.Replace("`r`n", "`n")
    if (!$script:ggufSource.Contains($Before)) { throw 'Unexpected GGUF source. Use llama.cpp b11669.' }
    $script:ggufSource = $script:ggufSource.Replace($Before, $After)
}

# Only the splitter links this version of the GGUF reader. Its metadata-only
# pass uses 64-bit tensor offsets and never allocates the full data section.
Replace-GgufText 'size_t size      = 0; // size of `data` in bytes' 'uint64_t size    = 0; // size of `data` in bytes'
Replace-GgufText 'has offset %" PRIu64 ", expected %zu' 'has offset %" PRIu64 ", expected %" PRIu64 "'
Replace-GgufText 'if (SIZE_MAX - ctx->size < padded_size)' 'if (UINT64_MAX - ctx->size < padded_size)'
Replace-GgufText 'cannot accumulate size %zu + %zu' 'cannot accumulate size %" PRIu64 " + %zu'
Replace-GgufText 'const char * gguf_get_tensor_name(const struct gguf_context * ctx, int64_t tensor_id) {' @'
uint64_t xp_gguf_tensor_offset(const struct gguf_context * ctx, int64_t tensor_id) {
    return ctx->info.at(tensor_id).offset;
}

const char * gguf_get_tensor_name(const struct gguf_context * ctx, int64_t tensor_id) {
'@
Replace-GgufText 'writer_t & gw, bool only_meta) {' 'writer_t & gw, bool only_meta, bool align = true) {'
Replace-GgufText '    gw.pad(ctx->alignment);' '    if (align) gw.pad(ctx->alignment);'
Replace-GgufText 'size_t gguf_get_meta_size(const struct gguf_context * ctx) {' @'
size_t xp_gguf_unpadded_meta_size(const struct gguf_context * ctx) {
    std::vector<int8_t> buf;
    gguf_writer_buf gw(buf);
    gguf_write_out(ctx, gw, true, false);
    return buf.size();
}

size_t gguf_get_meta_size(const struct gguf_context * ctx) {
'@
[IO.File]::WriteAllText($generated, $script:ggufSource)

$previousPath = $env:Path
try {
    $env:Path = "$toolchain;$previousPath"
    & (Join-Path $toolchain 'g++.exe') -std=c++17 -Os -Wall -Wextra -march=pentium4 -mfpmath=sse `
        -D_WIN32_WINNT=0x0501 -DWINVER=0x0501 -static -s -municode `
        '-Wl,--major-os-version,5,--minor-os-version,1,--major-subsystem-version,5,--minor-subsystem-version,1' `
        "-I$source/ggml/include" "-I$source/ggml/src" (Join-Path $PSScriptRoot 'gguf-split-xp.cpp') `
        $generated $library -lwinpthread -o $output
    if ($LASTEXITCODE -ne 0) { throw 'XP GGUF splitter link failed.' }
} finally {
    $env:Path = $previousPath
}
