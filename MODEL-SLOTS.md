# Model slots

| Slot | Local model | Use |
| --- | --- | --- |
| Chat | Text GGUF | Chat, coding tasks, and rewriting image prompts |
| Read images | Vision GGUF and matching mmproj GGUF | Answering questions about attached images |
| Create pictures | Compatible Stable Diffusion checkpoint | Generating images |

Choose a local file and click **Lock** to save the assignment. Unlock the slot to change its file. The app loads a model when the selected task needs it. The vision slot requires its companion projector.

Assignments are stored in model-slots.json under the app's local data folder. See [Computer Check](COMPUTER-CHECK.md) for comparing models and [memory settings](MEMORY-AND-LEGACY.md) for context and disk-backed loading.
