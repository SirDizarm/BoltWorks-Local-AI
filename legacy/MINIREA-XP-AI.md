# BoltWorks Mini REA XP AI 0.2

Mini REA inspects Windows executables and writes a text report. The optional REAAI command uses a local GGUF language model to explain selected structural facts from that report. Both commands run on the CPU and work offline. The model reads text and generates an answer. The inspector reads the target file without executing it.

## Run from a USB drive

Extract the ZIP into a writable folder and keep its three executables together. Double-click **BoltWorks-Mini-REA-XP.exe** to open the window. The tested SmolLM2-135M-Instruct-Q4_K_M.gguf model is distributed separately. Put it beside the executables or use **Browse** next to **GGUF model**.

- Choose an EXE or DLL and click **Inspect** to see its report.
- Enter printable ASCII text and click **Search** to find embedded text in the chosen file. This works without a model.
- After inspecting, enter a question and click **Ask AI** to get an answer based on selected structural facts. The model does not receive the embedded strings. Use **Search** for those.
- Each action saves its output in a new MINIREA folder beside the application. Use **Stop** to end a running action.

## Command line

```bat
MINIREA.EXE inspect "C:\OLDAPP\APP.EXE" "REPORT.TXT"
REAAI.EXE "SmolLM2-135M-Instruct-Q4_K_M.gguf" "REPORT.TXT"
```

Use a new report filename for each inspection. You can pass a short question as the third argument to REAAI. For a split GGUF, keep all shards together with their original names and pass the first shard.

## Compatibility and test results

REAAI targets 32-bit Windows XP with an SSE2-capable CPU. The window and inspector can run independently of the AI on older x86 processors. The earlier 0.1 inspector and model runner passed an offline test on a 512 MB Windows XP laptop. The new 0.2 window and updated model runner still need a Windows XP hardware test.

The 135M model used about 134 MiB of sampled process working set on the test PC, with equivalent results for the single file and the two-shard copy. Windows also needs its own memory. The 512 MB laptop is the first XP test target.

The answer correctly recognized the test EXE as x86 but omitted one DLL import and added an unrelated question. Use the inspector report to verify any model claims.

## Build

Run source/build-minirea-ai-xp.ps1 with a fresh extraction of the llama.cpp b11669 source, a fresh build directory, and the bin directory of the x86 w64devkit toolchain. The script builds the CPU runtime and both commands. The upstream runtime is available at https://github.com/ggml-org/llama.cpp/tree/b11669.
