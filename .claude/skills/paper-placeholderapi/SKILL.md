---
name: paper-placeholderapi
description: Use when displaying player data in scoreboards, tab lists, chat, or other plugins via PlaceholderAPI in a Paper plugin
---

# PlaceholderAPI

## Overview

Any player data shown in external contexts (scoreboard, tab, chat format, other plugins) → PlaceholderAPI. Never hardcode display — expose placeholders.

## plugin.yml

```yaml
softdepend: [PlaceholderAPI]
```

Never use `depend` — plugin must work without PAPI installed.

## Check at Runtime

```java
private boolean hasPAPI() {
    return Bukkit.getPluginManager().getPlugin("PlaceholderAPI") != null;
}
```

## Register Extension

```java
@Override
public void onEnable() {
    if (hasPAPI()) {
        new MyPluginExpansion(this).register();
    }
}
```

## Expansion Class

```java
public class MyPluginExpansion extends PlaceholderExpansion {
    private final MyPlugin plugin;

    public MyPluginExpansion(MyPlugin plugin) {
        this.plugin = plugin;
    }

    @Override public String getIdentifier() { return "myplugin"; }
    @Override public String getAuthor() { return "YourName"; }
    @Override public String getVersion() { return plugin.getDescription().getVersion(); }
    @Override public boolean persist() { return true; }

    @Override
    public String onPlaceholderRequest(Player player, String identifier) {
        if (player == null) return "";
        return switch (identifier) {
            case "coins" -> String.valueOf(plugin.getCoins(player));
            case "rank"  -> plugin.getRank(player);
            default      -> null; // null = unknown placeholder
        };
    }
}
```

## Placeholder Format

`%<pluginname>_<value>%`

Examples: `%economy_coins%`, `%shop_purchases%`

## Dependency in build.gradle

```gradle
repositories {
    maven { url = "https://repo.extendedclip.com/content/repositories/placeholderapi/" }
}
dependencies {
    compileOnly("me.clip:placeholderapi:2.11.6")
}
```

**See also:** Use paper-core-standards for base project standards.
