# SocialValley

AI-powered conversations with Stardew Valley NPCs. Talk naturally with the characters you love using Player2, OpenRouter, Google Gemini, or OpenAI.

---

## Features

- **Natural conversations** with any NPC in Pelican Town
- **Multiple AI providers** — Player2 (recommended), Ollama (advanced local option), OpenRouter, Google Gemini, OpenAI
- **Automatic Player2 detection** — just open the Player2 desktop app and the mod connects automatically
- **In-game sign-in** for Player2 — generate your API key directly without leaving the game
- **Conversation memory** — NPCs remember what you talked about across sessions
- **Custom personalities** — write your own system prompt for any NPC
- **Global instructions** — set rules that apply to all NPCs at once (e.g. "Always respond in two sentences")
- **Player name setting** — choose what name NPCs use to refer to you
- **Language support** — English, Spanish, French, German, Italian, Portuguese, Japanese, Chinese, Korean
- **Edit mode** — edit, delete, or regenerate any message in the conversation
- **CJK text support** — proper line wrapping for Japanese, Chinese, and Korean text

---

## Installation

1. Install [SMAPI](https://smapi.io/)
2. Download the latest release from [Nexus Mods](https://www.nexusmods.com/stardewvalley)
3. Extract into your `Stardew Valley/Mods` folder
4. Launch the game through SMAPI

---

## Setup

### Using Player2 (recommended — free)
1. Download the [Player2 desktop app](https://player2.game)
2. Log in to Player2
3. Launch Stardew Valley — the mod will detect the app automatically
4. Alternatively, click **Sign in with Player2 Account** in the mod's Settings menu to generate an API key without the desktop app

### Using Ollama (advanced - fully local)
1. Download and install the [Ollama desktop app](https://ollama.com/download)
2. Download a local chat model. I recommend using a .gguf file type as these are safer than alternatives. For consumer grade hardware (Less than 24GB VRAM), we also recommend using a quantized model to improve performance.
3. Move the model file to a directory, and create a file called 'Modelfile' (ensure there is no file extension). Open this file in notepad and enter 'FROM model_file_name.gguf', save and exit. This accepts paths if the Modelfile is located in a different directory to the model data files.
4. Open a terminal window, and navigate to the location the Modelfile is in, and run 'ollama create your-model-name', replacing your-model-name with the name you want to call this model.
5. Launch Stardew Valley. Open a chat window with any NPC, click **Config** → go to the **AI Provider** tab
6. Select Ollama, test the connection, and fetch models. If the connection fails, check Ollama is running in the background.
7. Select your model, and save

### Using other providers (OpenRouter, Gemini, OpenAI)
1. Open the chat window with any NPC
2. Click **Config** → go to the **AI Provider** tab
3. Select your provider, enter your API key, and fetch models

---

## How to use

- **Left-click** on an NPC to open the chat window
- **Config** button — open settings (language, AI provider, player name, global instructions)
- **Prompt** button — customize the personality of a specific NPC
- **Edit Mode** button — edit, delete, or regenerate messages

---

## Requirements

- Stardew Valley 1.6+
- SMAPI 4.0.0+

---

## Contributing

Contributions are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

---

## License

MIT — see [LICENSE](LICENSE) for details.
