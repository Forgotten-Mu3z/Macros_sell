---
name: paper-architecture
description: Use when a plugin is growing large, deciding whether to split features into separate plugins, or designing a multi-plugin system
---

# Architecture

## Overview

Default to monolith. Split only when modules are genuinely autonomous — different lifecycle, different audiences, or different dependencies.

## Split vs Monolith

**Keep as monolith when:**
- Features share data models or player state
- Features are deployed together always
- Splitting would require an inter-plugin API

**Split into separate plugins when:**
- Feature > 2000 lines AND is independently useful
- Feature can be disabled without breaking others
- Feature has different dependencies (e.g., one needs WorldGuard, one doesn't)

## Naming Split Plugins

```
<domain>-<feature>
```

Examples:
- `economy` (core) + `economy-shop` (optional shop module)
- `combat` + `combat-ranking`

## Always Offer Choice

When uncertain, present the tradeoff to the user before deciding:

> "This feature could live in `economy` or be extracted to `economy-shop`.
>
> **Monolith:** simpler, shared codebase, always installed together
> **Split:** `economy-shop` is optional, can be disabled, smaller jars
>
> Which fits your server setup?"

## Inter-Plugin Communication

When plugins need to share data:
1. **API plugin** — a dedicated `-api` plugin provides interfaces; consumers depend on it via `softdepend`
2. **Shared database** — separate DB plugin owns schema; others query it
3. **Events** — custom Bukkit events for loose coupling

Never directly access another plugin's internal classes.

**See also:** Use paper-core-standards for base project standards. Use paper-modrinth-integration before building a new plugin.
