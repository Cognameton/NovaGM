# NovaGM

An AI-powered tabletop RPG Game Master that runs on your own hardware. NovaGM narrates, manages characters, tracks the world state, and streams the story to every player's device in real time — no cloud required.

---

## A Full GM, Not a Chatbot

NovaGM uses a multi-agent architecture to separate the concerns of game logic and narrative, each handled by a purpose-selected local model.

- **Controller** — lightweight model (Phi, Qwen) that interprets player input, updates game state, and decides what happens next
- **Narrator** — larger, more expressive model (Mistral, Dolphin-LLaMA) that turns game events into prose and streams it live
- **Memory agent** — compact model that distills session events into structured memory entries for long-term continuity
- Models are auto-selected from a local `llm/` directory by role-matched naming convention; any GGUF model can be assigned to any role
- All inference via [LLamaSharp](https://github.com/SciSharp/LLamaSharp) running llama.cpp locally

---

## Every Player on Their Own Device

Players join from any browser on the local network — phone, tablet, laptop — with no app to install.

- Embedded ASP.NET Core (Kestrel) web server serves the player HUD over HTTP
- QR code generated at session start for instant join from mobile
- Server-Sent Events (SSE) stream GM narration to all connected clients simultaneously
- Room code validation on every player input; stale or forged codes are rejected
- LAN binding configurable; defaults to localhost for single-machine play

---

## Full Character System

Characters are created, persisted, and updated through both the GM desktop app and the player browser HUD.

- Stats (STR/DEX/CON/INT/WIS/CHA), race, class, and level
- AI-assisted character generation — random, or by class archetype (Fighter, Rogue, Mage)
- Custom race and class definitions per session
- 49-slot inventory grid with item quantities and per-item IDs
- 12-slot equipment system (Head, Neck, Cloak, Chest, Hands, Belt, Legs, Feet, Main Hand, Off Hand, Ring x2)
- Equipment stat modifiers applied to sheet display in real time
- Equip/unequip from browser HUD without touching the GM desktop

---

## Genre-Aware Narration

The GM's tone and content adapts to the genre of the session.

- Built-in genres: Fantasy, Sci-Fi, Horror
- Custom genre support with user-defined races, classes, and style rules
- GenreStyleGuard enforces thematic consistency in generated output
- Content packs loadable per genre for races, classes, and world lore

---

## Persistent World and Session Memory

The world state survives between sessions.

- Vector store (SQLite + cosine similarity) for semantic retrieval of past events and lore
- Structured memory deltas written after each narrative beat
- Full conversation history accessible to players via /history endpoint
- Mission and scenario save/load from the GM desktop UI

---

## GM Desktop Application

A full-featured desktop app for the GM built with Avalonia UI (.NET 8, cross-platform).

- Live narration output panel with token streaming
- Character sheet view with equipment slot grid and inventory
- Player management window — join status, character overview per player
- Model registry — browse, select, and assign local GGUF models to GM roles
- Content packs browser for genre assets
- Settings panel — port, LAN toggle, model paths, theme
- Dice roller service supporting standard RPG notation (2d6+3, 1d20, etc.)

---

## Tech Stack

| Layer | Technology |
|---|---|
| Desktop UI | C#, .NET 8, Avalonia UI 11 |
| AI inference | LLamaSharp (llama.cpp bindings for .NET) |
| Web server | ASP.NET Core / Kestrel (embedded) |
| Player UI | Vanilla HTML/CSS/JS, Server-Sent Events |
| Vector memory | SQLite with cosine similarity retrieval |
| QR code | ZXing.Net |
| Data | System.Text.Json, custom model layer |

---

## License

Copyright (c) 2025 Jeremy Findley. All rights reserved.
Source code is available for viewing and evaluation only. See [LICENSE](LICENSE).
