# BoltWorks XP Code Lab 0.4

This is a CPU-only experiment for 32-bit Windows XP with SSE2. The
model drafts C source offline. The earlier Mini REA runner passed on
a 512 MB XP laptop. This code mode was tested on Windows 11 in 32-bit
mode and is ready for its first XP test.

## Try it

1. Extract the ZIP into a writable folder.
2. Copy Qwen2.5-Coder-1.5B-Instruct-IQ4_XS.gguf,
   Qwen2.5-Coder-0.5B-Instruct-IQ4_XS.gguf, or
   SmolLM2-360M-Instruct-IQ4_XS.gguf beside REAAI.EXE.
   For split Qwen, copy the full set of numbered shards into that folder.
3. Double-click Test-XP-Coder.bat. It saves DRAFT.C, MODEL.LOG and
   RESULTS.TXT in a new XP-CODE folder beside the launcher.
   The launcher prefers the 1.5B model and picks its split copy first
   when present. It uses staged loading for Qwen and ordinary mapping
   for SmolLM2. To try a different model or shard count, drag the
   original GGUF or first shard onto Test-XP-Coder.bat. The launcher
   selects staged mode for Qwen2/Qwen2.5 and ordinary mapping for
   other architectures.

The log records elapsed time and peak process working set. This is
the AI process's resident memory. Windows and its file cache also
need memory. The staged Qwen runner maps one matrix at a time and
releases the view after computing with it. Small weight vectors,
the model vocabulary, the KV cache and compute buffers remain in memory.
An individual file view is limited to 256 MiB. Windows may cache more
file data system-wide. Reading weights again can be slow on a hard drive
or USB stick.

## Split a model on XP

Drag the original GGUF onto Split-XP-Model.bat. The splitter creates
numbered shards beside the original and keeps the original intact.
Allow about as much free disk space as the size of the original GGUF.
The splitter selects a target based on file size, tensor sizes, and
the destination. It counts metadata and padding in every shard. It
keeps tensors whole and fills each shard in model order until the next
tensor would exceed the target. Automatic sizing can raise the target
slightly to avoid an extra small shard. The 1.5B test model still
produces three shards.

The splitter reads the model metadata, then copies tensor data with a
1 MiB buffer. It can read source offsets beyond 4 GiB. Each output
is capped at 3800 MB, or 2000 MB on a volume reported as FAT or when
the file system cannot be identified. For example, use an NTFS source
and a FAT32 destination for a source GGUF larger than FAT32 allows.
The splitter reports when an individual tensor cannot fit.

```bat
GGUF-SPLIT.EXE "MODEL.GGUF" --plan
GGUF-SPLIT.EXE "MODEL.GGUF" 500 --out "E:\Models"
```

The destination folder must exist. A plan writes no files. Keep the
full numbered set together and pass its first shard to the runner.
Splitting is format-based. Running a model still depends on the
inference engine's architecture, tensor-view and memory limits.

## Build the example

The sample draft implements c_to_f. Review DRAFT.C and drag it
onto Build-XP-Sample.bat. It compiles the draft with the included
console template and TinyCC, and saves CONVERT.EXE and BUILD.LOG
beside the draft. From the lab folder in Command Prompt:

```bat
XP-CODE-XXXX-YYYY\CONVERT.EXE
```

Use the actual output folder name and enter -40. The expected
result is -40. Also check that 0 yields 32 and 100
yields 212. The launcher never executes the generated code.

## Draft a different program

```bat
REAAI.EXE --code MODEL.GGUF "SHORT C TASK" > DRAFT.C 2> MODEL.LOG
```

Use a fresh output filename. Exit code 3 means the draft reached the
240-token limit. The optional --buffered argument is for comparing
eager loading on a machine with enough memory. Use --staged to
enable the experimental matrix views for a dense Qwen2/Qwen2.5 GGUF.
Pass the first shard if using a split model.

## Desktop validation

The 32-bit splitter produced four shards from the 1.74 GB Qwen
Coder 3B GGUF. A synthetic 5 GiB GGUF also split successfully,
including a tensor starting beyond the 4 GiB source offset.

Qwen2.5-Coder 1.5B IQ4_XS used about 245 MiB of process working
set in staged mode and about 912 MiB with ordinary mapping. The
staged run used three shards and produced the same C source.
Qwen 0.5B used about 190 MiB staged and 381 MiB with ordinary
mapping. These are desktop measurements, and XP disk performance
and available memory have yet to be measured.

The focused prompt generated a correct conversion function.
Earlier broader prompts for the whole program produced drafts
that missed requirements. Test generated code against expected behavior.

The model files are distributed separately. The bundled TinyCC
0.9.27 build emits base relocations for its executables, which lets
them load with forced address randomization on the test PC. The
upstream source, modified linker source and build instructions are
included in compiler-source.
