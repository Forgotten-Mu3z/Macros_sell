---
name: paper-readme-template
description: Use when writing or updating README.md for a Paper plugin
---

# README Template

## Overview

Every plugin ships a README.md that a server admin can read without knowing Java.

## Template

````markdown
# PluginName

Short description of what the plugin does.

## Requirements

- Paper 26.1+
- Java 25+
- Optional: PlaceholderAPI

## Installation

1. Download the latest `PluginName.jar` from releases
2. Drop the jar into your `plugins/` folder
3. Restart the server

## Commands

| Command | Description | Permission |
|---------|-------------|-----------|
| `/command` | Description of what the command does | `pluginname.command` |

## Permissions

| Permission | Description | Default |
|-----------|-------------|---------|
| `pluginname.command` | Allow access to /command | op |

## Config

```yaml
# config.yml
plugin-name:
  enabled: true
  setting: value
```

## Integrations

### PlaceholderAPI

Use `%pluginname_placeholder%` in other plugins' text fields.

### Dialog API

This plugin supports the Dialog API for interactive conversations.

## Developer

Plugin by **YourName**. Issues and PRs welcome.
````

**See also:** Use paper-core-standards for base project standards.
