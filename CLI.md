# BoltWorks Local AI CLI

## Static legacy-file import

`import-legacy --file FILE --output-parent EXISTING_FOLDER [--json]` creates a new inspection project without running the target. The parent must already exist; the app creates a unique subfolder. Maximum input size is 256 MiB. Reports include SHA-256 and executable-header information, ASCII strings from at most 4 MiB (2000 strings, 160 characters each), and the first 4096 bytes as hex. It does not disassemble or decompile. Select the resulting folder in Work on code to use the local model. No REA/provider installation is implied.

Use the console program for scripts and integration with other software; use the desktop EXE for interactive editing and previews. Both use the same local inference engines. The CLI opens no windows and needs no Ollama, ComfyUI, API key, internet service or paid inference.

Build the app and set up the runtimes using [BUILDING.md](BUILDING.md). Keep `BoltWorks-Local-AI-CLI.exe`, `runtime`, and `image-runtime` together. Windows x64 and .NET Framework 4.8 are required, just as for the desktop app. No PATH changes or installation are needed. Models are separate local files, not included in the package.

Examples below are PowerShell commands run from the extracted application folder. Replace paths for another computer.

## Chat

```powershell
.\BoltWorks-Local-AI-CLI.exe chat --model 'C:\models\Qwen3-0.6B-Q8_0.gguf' --prompt 'Förklara hur man gör potatissallad.'
```

Use `--stream` to print pieces of the reply as they arrive. Default output waits for the whole reply. These are single-turn requests: no previous chat is included or saved automatically.

## Piped text and JSON

```powershell
'Översätt till svenska: Boil the potatoes for 15 minutes.' |
  .\BoltWorks-Local-AI-CLI.exe chat --model 'C:\models\Qwen3-0.6B-Q8_0.gguf' --json
```

Or use `--input 'recipe.txt'` to read a UTF-8 file. Choose one input source: `--prompt`, `--input`, or redirected standard input. Input is limited to 32,000 characters. Pipe encoding must be UTF-8; PowerShell 7 does this by default. With legacy Windows PowerShell 5.1, set `$OutputEncoding = [Text.UTF8Encoding]::new($false)` before piping non-ASCII text, or use a UTF-8 input file.

Successful `--json` chat/read output is a single JSON object:

```json
{"version":1,"task":"chat","text":"Model reply","model":"C:\\models\\model.gguf","theme":null,"skill":null}
```

Without `--json`, stdout contains only reply text. `--stream` and `--json` cannot be combined, so callers never have to parse partial JSON. Runtime logs are captured internally; they are included in stderr errors when needed, never mixed with successful stdout.

## Read an image

```powershell
.\BoltWorks-Local-AI-CLI.exe read --model 'C:\models\Swift-Qwen3.8-27B-Q4_K_M.gguf' --projector 'C:\models\mmproj-Swift-Qwen3.8-27B-F16.gguf' --image 'C:\pictures\recipe.jpg' --prompt 'Read this recipe. Preserve all amounts.' --json
```

Use a smaller supported vision model and its matching projector on a low-memory laptop. If `--projector` is omitted, the CLI can discover one matching projector beside the main model. An absent or ambiguous match requires an explicit path. `chat` also accepts `--image` and `--projector`. Image preprocessing/limits match the desktop app (JPEG/PNG/BMP, 20 MB, 40 megapixels, resized to at most 1280 pixels).

## Generate a picture

```powershell
.\BoltWorks-Local-AI-CLI.exe generate --model 'C:\models\sd_xl_turbo_1.0_fp16.safetensors' --preset sdxl-turbo --backend vulkan --prompt 'A red cottage beside a lake, watercolor' --output-dir 'C:\pictures\generated' --json
```

Successful output includes absolute paths to the generated PNG and its JSON settings record:

```json
{"version":1,"task":"generate","image":"C:\\pictures\\generated\\unique-name.png","metadata":"C:\\pictures\\generated\\unique-name.json","model":"C:\\models\\model.safetensors","theme":null}
```

Without `--json`, only the PNG path is printed. Files get unique names; existing files are not overwritten. The default output directory is `generated` under the caller's current directory. No generation settings are changed in the desktop app.

The default preset is `auto`: recognized filenames select the correct family; unknown names need an explicit preset, and known mismatches are rejected. Presets: `sdxl-turbo` (four steps, guidance 1), `sd1` or `sdxl` (20 steps, guidance 7). Choose the preset matching the checkpoint. Optional `--steps 2`, `--size 384` (256–768, multiples of 64), `--seed 42` (-1 for random), and `--reference 'starting-picture.png'` for img2img. Starting images require at least two steps. Turbo ignores negative prompts. Only the same all-in-one SD 1.x/SDXL model families as the desktop app are wired up.

`--backend cpu` is the default. `vulkan` uses the first Vulkan GPU for diffusion and the CPU for the text encoder and VAE. It does not use CUDA. Generation runs a short-lived local process, not an image server.

## Themes and skills

```powershell
.\BoltWorks-Local-AI-CLI.exe themes --json
.\BoltWorks-Local-AI-CLI.exe skills --json
.\BoltWorks-Local-AI-CLI.exe chat --model 'C:\models\model.gguf' --input 'recipe.txt' --skill 'Organize a recipe' --json
.\BoltWorks-Local-AI-CLI.exe generate --model 'C:\models\sdxl.safetensors' --preset sdxl --prompt 'A woodland cottage' --theme 'Watercolor forest' --json
```

Create and save named themes/skills in the desktop app first. Listing returns explicitly saved profiles; built-ins do not appear until the desktop app has saved its skills file. `--skill-file 'instructions.md'` can supply plain UTF-8 instructions directly without GUI setup. Use it instead of `--skill`, not together. Instruction files are limited to 12,000 characters and are not executed.

The CLI reads the same per-user files under `%LOCALAPPDATA%\BoltWorks\LlamaLite`. It does not modify them. It applies a profile only when explicitly named, regardless of the GUI's enabled/selected state, so another person's GUI settings do not silently change a script's behavior. Skills apply to chat/read; themes can apply to all three inference commands. Themes/skills guide prompts, not model training or guaranteed behavior.

## Other software calling the CLI

Start the EXE directly with an argument array, without a shell. Redirect stdout/stderr, send UTF-8 text to stdin, then **close stdin** to signal the end of input. Read both output streams concurrently to avoid pipe deadlocks. Check the exit code before consuming stdout as a successful result. The CLI's own `--timeout` also bounds waiting for stdin.

Python example (the CLI itself does not require Python):

```python
import json
import subprocess

result = subprocess.run(
    [r"C:\apps\BoltWorks-Local-AI\BoltWorks-Local-AI-CLI.exe", "chat",
     "--model", r"C:\models\model.gguf", "--json", "--timeout", "120"],
    input="Translate to Swedish: 2 eggs and 100 g flour.",
    text=True, encoding="utf-8", capture_output=True, timeout=130,
)
if result.returncode != 0:
    raise RuntimeError(result.stderr)
reply = json.loads(result.stdout)["text"]
```

Every invocation loads, runs and unloads its model. This keeps the lifetime explicit and releases memory afterward, but repeated large-model requests pay loading overhead. A persistent API/batch worker is not included in this version. The CLI and GUI are independent processes; avoid simultaneous model runs on a 4 GB laptop. There is no automatic cross-process memory scheduler.

Chat defaults: context 2048, maximum reply 256 tokens, CPU threads half the logical processors capped at eight. Override with `--context`, `--max-tokens` (at most half the context) and `--threads`. Use `hardware --json` to inspect the thread choice. Large inputs may exceed the selected context; the runtime reports an error rather than silently removing text.

## Errors and cancellation

Exit codes: **0** success, **1** runtime/model/file failure, **2** invalid arguments, **130** cancellation/timeout.

Errors go only to stderr. With `--json`, the error is `{"version":1,"error":{"code":"usage|runtime|cancelled","message":"..."}}`. Without it, the error is plain text. Streaming text already emitted before failure remains partial; check the exit code.

Ctrl+C cancels. `--timeout` defaults to 600 seconds and accepts 1–86400 seconds. Cancellation disposes the runtime. A Windows job object terminates child runtimes if the CLI process is killed. If a caller's external timeout terminates the CLI, it must reap that process normally as well. Normal cleanup removes temporary generation prompt/reference files; forced termination can leave temporary files. Generated PNGs/settings are local files and may contain prompt metadata.

Use `--help` for the full option summary or `--version` for the CLI version.





Text and vision commands: --backend cpu (default) or --backend cuda --gpu-layers 16. CUDA uses the separate runtime-cuda add-on; CPU needs no CUDA files. The GUI processing choice never changes CLI defaults.
