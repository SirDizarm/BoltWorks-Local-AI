# Low-memory models, GGUF splits and legacy systems

## What this build implements

The model controls on the left offer **Normal**, **Low RAM**, and **Disk-backed CPU (experimental)**. The selected memory mode is saved; processing still starts on CPU each launch. Normal preserves the existing batches and context behavior. The separate picture-generation engine is unchanged.

| Mode | Effective context | Runtime behavior |
|---|---|---|
| Normal | Existing settings; coding normally uses 16K | Existing 128/64 processing batches; CPU or explicitly selected CUDA |
| Low RAM | At most 4096 tokens | Explicit memory mapping; 64/32 batches; CPU or explicitly selected CUDA |
| Disk-backed CPU | At most 4096 tokens | CPU only; memory mapping; supported lazy tensor reads; no warmup; 32/16 batches |

Coding measures the formatted prompt with the running model's tokenizer, reserving reply space. Older exchanges and large tool excerpts can be shortened; the original task is retained. Oversized task instructions produce an error asking for a shorter task or Normal mode. Full tool results remain in the job folder. A short context cannot hold an entire large codebase; use focused tasks and read files in sections. Chat jobs also receive the context cap but do not automatically rewrite oversized instructions.

Disk-backed mode lets Windows page memory-mapped weights from storage and enables the runtime's limited lazy-reading mechanism for supported tensors. It does **not** implement a fixed RAM budget, force every layer to unload after use, make SSD as fast as RAM, or guarantee an oversized model will load. Runtime scratch buffers, context, image processing and Windows still need physical/committed memory. Disk mode allows up to 30 minutes for startup (other modes retain five minutes); Stop remains available. All inference remains local.

## Create and load split files

**Create GGUF split files…** opens an offline splitter. Select an unsplit GGUF, destination folder and target part size. It creates a new subfolder and leaves the original file untouched. Stop may leave incomplete output; do not use that as a model set. A tensor is indivisible, so a part-size target smaller than a tensor may fail; retry with larger parts in a new output folder.

Select any numbered part in a Chat slot or pass it to the CLI. The app checks that every named part exists and starts inference from part 1. The runtime checks model metadata and tensors. Keep original names and all parts in the same directory. **Inspect GGUF / split files** reports the total model-file size without loading the weights.

**File shards are not smaller independent models.** They hold the same model weights. Splitting by itself does not lower inference RAM, reduce model precision, or turn a model into separate specialists. The experimental paging behavior comes from the selected memory mode.

CLI examples (PowerShell; replace paths):

```powershell
.\BoltWorks-Local-AI-CLI.exe split-model --model 'D:\Models\model.gguf' --output-dir 'D:\Models\new-splits' --part-mb 512 --json
.\BoltWorks-Local-AI-CLI.exe model-info --model 'D:\Models\new-splits\model-00001-of-00003.gguf' --json
.\BoltWorks-Local-AI-CLI.exe chat --model 'D:\Models\new-splits\model-00002-of-00003.gguf' --memory disk --context 4096 --max-tokens 512 --timeout 3600 --prompt 'Explain this small function.'
```

The example's part count is illustrative; use the filenames the splitter actually returns. CLI disk mode rejects CUDA explicitly. CLI timeouts still apply, so long experiments should set `--timeout` deliberately. Use CPU mode for machines without NVIDIA GPUs.

## Evidence and outstanding experiments

The bundled runtime is llama.cpp build 11669, commit `620d2225c`. Its own help advertises the memory flags used here. The matching official CPU splitter was acquired from the upstream b11669 release, with SHA-256 `37b3efcf962847ca869745fd4ccf7f5e7e8dacfeff9fa3523b4ca8cdc66d632b` verified against the GitHub release asset digest. It is included under `runtime` with the existing llama.cpp license.

Tests exercised profile caps, CPU/CUDA argument preservation, missing/corrupt shard rejection, complete-set size, token budgeting and retention of task instructions. Real Qwen3-0.6B-Q8_0 was split into three GGUF parts using a 256 MB target. Selecting the middle part successfully loaded the complete model in both Low RAM and Disk-backed CPU modes, measured prompt tokens, generated a response and saved that response to a file. Both used a 4096-token context. Unsplit loading was also tested in both modes.

This was **not** a model-larger-than-physical-RAM benchmark, an 8 GB laptop test, a Windows XP test or proof that a small model can reliably write complex programs. Output quality still depends on the model, available context and verification. The next hardware experiment should run a chosen larger model on the actual laptop and record peak working set, available RAM, disk reads, time to first token, generation speed and compiled/tested output. No performance guarantee is attached to the experimental mode.

## Windows XP and older computers

The main app uses .NET Framework 4.8. The experimental Windows 10 x86 build and the separate native XP demos have their own requirements and documented test results. See [Windows 10 x86](WINDOWS-X86.md) and the [XP demo guide](legacy/XP-DEMO.md).

## Legacy file inspection

Use **Import legacy file** to create a static inspection project for the local coding model. The **Legacy code / assembly** preset can also work with local source and disassembly exports. Changes need a compatible compiler and tests on the target.
