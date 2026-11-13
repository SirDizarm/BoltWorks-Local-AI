# Integrate with BoltWorks Local AI

An editor plugin, desktop app, or script can call `BoltWorks-Local-AI-CLI.exe` as a local subprocess. Pass arguments as separate values using your language's subprocess API. For chat and vision requests, provide a local GGUF and use `--prompt`, `--input`, or UTF-8 standard input. Use `--json` for a complete result that your integration can parse.

```powershell
'Summarize this build failure: compiler exit code 1' |
    .\BoltWorks-Local-AI-CLI.exe chat --model 'C:\Models\assistant.gguf' --json
```

Read the exit code, stdout, and stderr as described in the [CLI reference](CLI.md). Each CLI invocation owns its model process. The calling plugin decides how to interpret the response and which actions to expose in its host application.
