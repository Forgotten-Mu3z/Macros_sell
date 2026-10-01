---
name: paper-adventure-text
description: Use when sending messages, titles, action bars, or any text to players in a Paper plugin, or when working with MiniMessage formatting, colors, or gradients
---

# Adventure Text

## Overview

Paper bundles Adventure API — use it directly, no shading needed. Never use `ChatColor` or `&` codes in Java code.

## Core Imports

```java
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.minimessage.MiniMessage;
import net.kyori.adventure.title.Title;
import net.kyori.adventure.text.format.NamedTextColor;
```

## MiniMessage — Send Text

```java
private static final MiniMessage MM = MiniMessage.miniMessage();

// From string
player.sendMessage(MM.deserialize("<red>You don't have permission."));
player.sendMessage(MM.deserialize("<gold><bold>Welcome</bold> to the server, <green>" + player.getName()));

// From config (recommended)
String raw = config.getString("messages.no-permission");
player.sendMessage(MM.deserialize(raw));
```

## MiniMessage Syntax Reference

| Syntax | Result |
|---|---|
| `<red>text</red>` | Red text |
| `<bold>text</bold>` | Bold |
| `<#FF5733>text</#FF5733>` | Hex color |
| `<gradient:#ff0000:#0000ff>text</gradient>` | Gradient |
| `<gold><bold>text</bold></gold>` | Nested tags |
| `<click:run_command:/heal>Click me</click>` | Clickable |
| `<hover:show_text:'<red>Tooltip'>text</hover>` | Hover tooltip |

## Titles

```java
Component title    = MM.deserialize("<gold><bold>Welcome!");
Component subtitle = MM.deserialize("<gray>Glad you're here.");
Title.Times times  = Title.Times.times(Duration.ofMillis(500), Duration.ofSeconds(3), Duration.ofMillis(500));

player.showTitle(Title.title(title, subtitle, times));
```

## Action Bar

```java
player.sendActionBar(MM.deserialize("<yellow>⚔ Combo x5"));
```

## Boss Bar

```java
BossBar bar = BossBar.bossBar(
    MM.deserialize("<red>Boss Fight"),
    0.75f,
    BossBar.Color.RED,
    BossBar.Overlay.PROGRESS
);
player.showBossBar(bar);
```

## Never Use

```java
// ❌ Never
player.sendMessage("§aHello " + player.getName());
player.sendMessage(ChatColor.GREEN + "Hello");

// ✅ Always
player.sendMessage(MM.deserialize("<green>Hello <white>" + player.getName()));
```

## Config Values

Config can store raw MiniMessage strings:
```yaml
messages:
  welcome: "<green>Welcome, <white><player>!"
  no-permission: "<red>✗ No permission."
```

If server admins are used to `&` codes, accept both in config — but convert in code:
```java
String raw = config.getString("messages.welcome");
// Support legacy & codes from config if needed:
raw = LegacyComponentSerializer.legacyAmpersand().deserialize(raw)
    // then re-serialize or just use MM directly if you control the config
```

**See also:** Use paper-core-standards for base project standards.
