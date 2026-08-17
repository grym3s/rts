---
status: accepted
date: 2026-08-17
---
# 0002 — Presentation engine: Godot 4.x with C#/.NET

Godot 4.7.x .NET build for `game/`. Unity 6 is the credible runner-up; Unreal and Bevy set aside. Reasoning and the evidence that would change this: foundation review §3.

Why: text-first project files, `--headless` CLI, MIT licence, small API surface for agents, one language (C#) across sim/tools/presentation, gdUnit4 for scene tests.

Known caveat: C# web export is not official as of 4.7 — desktop is the v1 target.
