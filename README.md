# NovaGM 🎲

**AI-Powered Tabletop RPG Game Master Tool**

NovaGM is a hybrid Game Master tool and multiplayer host app for tabletop RPGs like D&D. It features local LLM integration, browser-based player clients, and comprehensive campaign management tools.

## ✨ Features

### 🖥️ Desktop Host App (GM)
- **Cross-platform**: Windows & Linux support with distributable binaries
- **AI Game Master**: Fully automated GM, narrator, and controller roles using local LLMs
- **Campaign Management**: Character sheets, journal, glossary, story inventory
- **Session Controls**: Start/end sessions, manage players, load scenarios
- **Model Management**: Drop GGUF models into `/llm` folder, selectable in UI
- **Dark Mode UI**: Clean, modern interface built with Avalonia

### 📱 Browser Player Clients
- **Jackbox-style Join**: 4-character room codes (e.g., "BCS3") + QR codes
- **Multi-device**: Phones, tablets, PCs on same Wi-Fi network
- **Character Creation**: Full character builder with auto-generation shortcuts
- **Player Tools**: Inventory, character sheet, dice rolls, prompt input
- **Real-time Updates**: Live narration streaming via Server-Sent Events

### 🤖 AI Integration
- **Local LLM**: LLamaSharp 0.25 with llama.cpp backend
- **CPU First**: Optimized for CPU inference (GPU toggle for future)
- **Multi-role AI**: Separate models for controller, narrator, and memory
- **Extensible**: Plugin architecture for non-LLM modules

### 🔧 Technical Features
- **Embedded Server**: ASP.NET Core server inside desktop app
- **SQLite Storage**: Persistent player and session data
- **JSON/YAML Config**: All settings in `/config` directory
- **Modular Architecture**: Clean separation of concerns
- **Cross-platform Build**: Single command builds for Windows/Linux

## 🚀 Quick Start

### Prerequisites
- .NET 8.0 SDK
- Ubuntu 24.04+ or Windows 10+

### Build from Source

```bash
# Clone repository
git clone https://github.com/novagm/novagm.git
cd novagm

# Test build (Ubuntu)
chmod +x scripts/test-build.sh
./scripts/test-build.sh

# Cross-platform build
chmod +x build.sh
./build.sh
```

### Install Models

1. Download GGUF models (e.g., from Hugging Face)
2. Place in `NovaGM/bin/Release/net8.0/llm/` directory
3. Launch NovaGM and select models in Settings → Models

### Run NovaGM

```bash
# From build directory
cd NovaGM/bin/Release/net8.0
./NovaGM

# Or install .deb package (Linux)
sudo dpkg -i dist/deb/novagm_1.0.0_amd64.deb
novagm
```

## 🎮 How to Play

### For Game Masters
1. Launch NovaGM desktop app
2. Configure AI models in Settings → Models
3. Start new game or continue existing session
4. Share room code with players
5. Monitor player connections and manage session

### For Players
1. Connect to same Wi-Fi as GM
2. Visit `http://GM_IP:5055` in browser
3. Enter 4-character room code or scan QR code
4. Create character and join the adventure!

## 📁 Project Structure

```
NovaGM/
├── Services/           # Core business logic
│   ├── AgentOrchestrator.cs    # AI coordination
│   ├── Multiplayer/            # Player management
│   ├── Streaming/              # Real-time updates
│   └── State/                  # Game state management
├── Views/              # UI components
├── Models/             # Data models
├── Themes/             # UI styling
├── config/             # Configuration files
└── llm/                # AI model directory
```

## 🔧 Configuration

### App Settings (`config/app-settings.json`)
```json
{
  "NovaGM": {
    "Server": {
      "DefaultPort": 5055,
      "AllowLAN": false,
      "MaxPlayers": 8
    },
    "AI": {
      "UseGPU": false,
      "GPULayers": 0,
      "ContextSize": 2048
    }
  }
}
```

### Environment Variables
- `NOVAGM_GPU_LAYERS`: GPU layers for LLM inference (-1 for max)
- `NOVAGM_ALLOW_LAN`: Enable LAN access (true/false)

## 🐳 Docker Support

```bash
# Build and run with Docker Compose
docker-compose up --build

# Access at http://localhost:5055
```

## 🛠️ Development

### Build Requirements
- .NET 8.0 SDK
- Ubuntu 24.04+ or Windows 10+
- Git

### IDE Setup
- Visual Studio 2022+ or VS Code
- C# extension for VS Code
- Avalonia extension for XAML support

### Testing
```bash
# Run unit tests
dotnet test

# Test cross-platform build
./scripts/test-build.sh
```

## 📦 Distribution

### Windows
- Single-file executable: `dist/win-x64/NovaGM.exe`
- Portable, no installation required

### Linux
- Binary: `dist/linux-x64/NovaGM`
- Debian package: `dist/deb/novagm_1.0.0_amd64.deb`

## 🤝 Contributing

1. Fork the repository
2. Create feature branch (`git checkout -b feature/amazing-feature`)
3. Commit changes (`git commit -m 'Add amazing feature'`)
4. Push to branch (`git push origin feature/amazing-feature`)
5. Open Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- [Avalonia UI](https://avaloniaui.net/) - Cross-platform .NET UI framework
- [LLamaSharp](https://github.com/SciSharp/LLamaSharp) - .NET bindings for llama.cpp
- [ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/) - Web framework
- [SQLite](https://www.sqlite.org/) - Embedded database
- [QRCoder](https://github.com/codebude/QRCoder) - QR code generation

---

**Ready to revolutionize your tabletop RPG experience? Download NovaGM and let AI be your Game Master! 🎲✨**
