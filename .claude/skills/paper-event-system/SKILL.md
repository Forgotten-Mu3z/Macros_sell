---
name: paper-event-system
description: Use when implementing event listeners, handling async events, setting event priorities, or registering and unregistering listeners in a Paper plugin
---

# Event System

## Overview

Bukkit's event system has async traps that cause server crashes. Know the rules before writing a listener.

## Basic Listener

```java
public class MyListener implements Listener {

    @EventHandler(priority = EventPriority.NORMAL, ignoreCancelled = true)
    public void onPlayerJoin(PlayerJoinEvent event) {
        Player player = event.getPlayer();
        // safe — this is a sync event
    }
}
```

## Registration

```java
// In onEnable()
getServer().getPluginManager().registerEvents(new MyListener(this), this);
```

## Unregistration

```java
// In onDisable() or when listener is no longer needed
HandlerList.unregisterAll(myListener); // specific listener
// or
HandlerList.unregisterAll(this);       // all listeners for this plugin
```

## Event Priorities

| Priority | Use for |
|---|---|
| `LOWEST` | First to process; rarely needed |
| `LOW` | Early processing |
| `NORMAL` | Default — use this unless you have a reason not to |
| `HIGH` | Override other plugins' decisions |
| `HIGHEST` | Last word before monitors |
| `MONITOR` | **Read-only.** Never modify event state here. Logging only. |

## ignoreCancelled

Always set `ignoreCancelled = true` unless you explicitly need to handle cancelled events:

```java
@EventHandler(priority = EventPriority.NORMAL, ignoreCancelled = true)
```

Without it, your handler runs even after another plugin cancelled the event.

## Async Events — Critical Rule

Async events (marked `isAsynchronous() == true`, e.g. `AsyncPlayerChatEvent`) run off the main thread.

**Never touch world state in async context:**

```java
// ❌ CRASH — modifying world from async thread
@EventHandler
public void onAsyncChat(AsyncPlayerChatEvent event) {
    event.getPlayer().teleport(location); // WRONG
}

// ✅ Schedule back to main thread
@EventHandler
public void onAsyncChat(AsyncPlayerChatEvent event) {
    Player player = event.getPlayer();
    Bukkit.getScheduler().runTask(plugin, () -> {
        player.teleport(location); // safe
    });
}
```

Safe in async: reading player data, database queries, HTTP requests, sending messages.

## Paper-Specific Events

Prefer Paper events over Bukkit equivalents — they carry more data and fire more efficiently:

| Bukkit | Paper alternative |
|---|---|
| `PlayerMoveEvent` | `PlayerMoveEvent` (Paper version has delta) |
| `EntityDamageEvent` | `EntityDamageEvent` (Paper has damage source details) |
| `BlockBreakEvent` | `BlockBreakEvent` (Paper adds drop details) |

Check `io.papermc.paper.event.*` for Paper-exclusive events before using Bukkit ones.

**See also:** Use paper-core-standards for base project standards.
