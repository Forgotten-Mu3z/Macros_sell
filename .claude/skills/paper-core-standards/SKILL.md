---
name: paper-core-standards
description: Use when creating or working on a Paper Minecraft plugin — scaffolding, build setup, naming conventions, config structure, and code quality rules
---

# Core Standards

## Overview

These standards apply to any Paper plugin. When in doubt, check Paper API docs first — not Bukkit/Spigot.

## API & Runtime

- **Paper API 26.1+** — verify latest version: `https://api.papermc.io/v3/projects/paper/versions`
- **Java 25+** — use records, sealed classes, pattern matching, Stream API, CompletableFuture
- **Build:** Gradle 8.10+ (preferred) or Maven 3.9+

## Naming

Pick one convention per project and keep it consistent:

- Package: `<your.namespace>.<pluginname>` (lowercase, no hyphens) — e.g. `dev.example.economy`
- Jar artifact: the plugin name, optionally with a project prefix — e.g. `Economy` or `myproject-Economy`
- Main class: `<PluginName>Plugin` or `<PluginName>` inside the package — e.g. `EconomyPlugin`
- Example: plugin `Economy` → `dev.example.economy.EconomyPlugin`

If the project already uses a prefix (jar, package, permissions, placeholders), follow the existing one instead of inventing a new one.

## What NOT to Use

| Avoid | Use instead |
|---|---|
| `CustomModelData` (deprecated) | `ItemMeta` components API |
| `BukkitRunnable` | Paper async scheduler / CompletableFuture |
| `ChatColor` / `&` codes in code | Adventure API + MiniMessage (see paper-adventure-text) |
| Deprecated Bukkit methods | Paper API equivalents |
| NMS (net.minecraft.server) | Paper API; ask first before touching NMS |
| JDBC shading | Paper caches JDBC automatically — never shade it |

## Dependencies

- `softdepend` over `depend` in plugin.yml — plugin should work without optional deps
- Check optional deps at runtime: `getServer().getPluginManager().getPlugin("Name") != null`
- Gradle 8.10+ / Maven 3.9+

## Config

- Everything in `config.yml`: messages, prices, timers, coordinates, feature flags
- No hardcoded values in code
- Text in config: MiniMessage format by default (`<red>text</red>`, `<gold><bold>title</bold></gold>`)
- `&` legacy codes: only if server admins explicitly expect them; never in Java code
- All YAML keys and comments: English only

## Project Files

- `README.md` — required, English (see paper-readme-template)
- All source files, configs, comments — English only

**See also:** paper-adventure-text · paper-data-persistence · paper-event-system · paper-commands-permissions
