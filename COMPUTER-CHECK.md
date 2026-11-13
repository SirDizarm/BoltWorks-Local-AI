# Older-computer checks in 0.6.19

The CPU comparison includes two short exact-format instruction tasks. Candidates must pass at least one and meet the existing speed and response-length requirements to receive a recommendation. Passing two trivial tasks does not establish general reasoning or coding quality. Responses are included for review. Recognizable projector and image-generation filenames are reported with their proper roles and skipped before loading; this filename heuristic cannot identify every renamed model.

For an 8 GB laptop, test a compact chat model with a 2048-token context first. Compare a 4-bit quantization of a somewhat larger model against an 8-bit quantization of a smaller model using the same task. Results must be measured on the target machine. Disk-backed loading and CPU operation remain available for separate experiments.
# BoltWorks Local AI computer check

Open **Check this computer in the left model panel**, add compatible local chat GGUF models, then choose **Run CPU check**. The test unloads the current chat model, tests each selected model in sequence, and releases its runtime. Use **Stop** to cancel and **Save report** to keep the results.

The check records Windows RAM, model file size, loading time, time until the first text, generated token count, output tokens per second, and the runtime process's peak resident memory. It recommends the fastest usable tested model that produced at least eight tokens at one token per second or better. The response is included so you can judge basic behavior yourself. A short speed test cannot establish reasoning or coding quality.

All candidates use CPU processing, Low RAM mode, 2048 context and a 96-token reply limit. Larger contexts need a separate test. A candidate is skipped when its files exceed 65% of currently available physical RAM. This conservative check leaves working space for Windows but cannot guarantee a model will fit. Disk-backed or CUDA configurations may behave differently.

Choose the first shard of a complete split model, or any shard with its original filename. The checker resolves the full set and counts all parts. Missing or invalid files and failed or timed-out loads appear in the report. Each candidate has a 150-second budget.

The checker uses models already stored locally. Projector files and image-generation checkpoints are unsuitable chat candidates. Recommendations do not change locked model slots. After checking, choose and lock your preferred Chat model.

The app is now named **BoltWorks Local AI**. Existing model slots, settings, chat tools and data retain their previous storage paths for continuity. llama.cpp and other bundled component license files are included with the portable app.
