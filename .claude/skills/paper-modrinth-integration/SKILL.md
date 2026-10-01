---
name: paper-modrinth-integration
description: Use before starting implementation of any Paper plugin feature — searches Modrinth for existing plugins to fork or depend on instead of building from scratch
---

# Modrinth Integration

## Overview

Search before building. If a plugin with a usable API exists on Modrinth, fork or depend on it rather than reimplementing from scratch.

## Search API

```
GET https://api.modrinth.com/v2/search?query=<term>&facets=[["project_type:plugin"],["categories:paper"]]&limit=10
```

Example — search for an economy plugin:
```
https://api.modrinth.com/v2/search?query=economy&facets=[["project_type:plugin"],["categories:paper"]]&limit=5
```

Response fields to check: `title`, `description`, `downloads`, `source_url`, `versions`.

## Decision Flow

```
Search Modrinth
    ↓
Found plugin with Paper API + active maintenance?
    ├── YES, has Java API → depend on it (softdepend if optional)
    ├── YES, no API but open source → propose a fork to the user
    └── NO → build from scratch, follow paper-core-standards
```

## Proposing to User

Present findings before writing any code:

> "Found **EconomyPlus** (50k downloads, last updated 2026-03, MIT license) with a public API. Options:
> 1. **Depend on it** — less code, maintained by community
> 2. **Fork it** — full control, we own the codebase
> 3. **Build from scratch** — if neither fits
>
> Which approach?"

## When to Skip Search

- User explicitly says "build from scratch"
- Plugin is core to the project's identity (not generic)
- Feature is highly paper-specific

**See also:** Use paper-core-standards for base project standards.
