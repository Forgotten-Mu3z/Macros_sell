---
name: paper-scheduler
description: Use when scheduling delayed or repeating tasks, running async work, or writing Folia-compatible task scheduling in a Paper plugin
---

# Scheduler

## Overview

Never use `BukkitRunnable` — use Paper's scheduler API directly. For Folia compatibility, use `RegionScheduler` / `GlobalRegionScheduler` instead of `BukkitScheduler`.

## Standard Paper Scheduler

```java
// Run once, next tick (sync)
Bukkit.getScheduler().runTask(plugin, () -> {
    player.teleport(location);
});

// Run once, delayed (sync) — 20 ticks = 1 second
Bukkit.getScheduler().runTaskLater(plugin, () -> {
    player.sendMessage(MM.deserialize("<red>Time's up!"));
}, 20L);

// Run repeatedly (sync) — delay 0, repeat every 20 ticks
ScheduledTask task = Bukkit.getScheduler().runTaskTimer(plugin, () -> {
    updateScoreboard();
}, 0L, 20L);

// Cancel later
task.cancel();

// Async — for I/O, HTTP, DB queries (never touch world state here)
Bukkit.getAsyncScheduler().runNow(plugin, scheduledTask -> {
    String data = fetchFromDatabase(playerId);
    // Schedule sync callback to use result
    Bukkit.getScheduler().runTask(plugin, () -> applyData(player, data));
});
```

## Paper Async Scheduler (preferred over BukkitScheduler async)

```java
// Run async once
plugin.getServer().getAsyncScheduler().runNow(plugin, task -> {
    doAsyncWork();
});

// Run async delayed — duration-based, not tick-based
plugin.getServer().getAsyncScheduler().runDelayed(plugin, task -> {
    doAsyncWork();
}, 1, TimeUnit.SECONDS);

// Run async repeating
plugin.getServer().getAsyncScheduler().runAtFixedRate(plugin, task -> {
    doAsyncWork();
}, 0, 1, TimeUnit.SECONDS);
```

## Folia-Compatible Scheduling

Folia uses regionalized threading — `BukkitScheduler` does not work. Use these APIs which work on both Paper and Folia:

```java
// Sync task for a specific entity's region
entity.getScheduler().run(plugin, task -> {
    entity.teleport(location);
}, null);

// Sync task for a specific location's region
location.getWorld().getRegionScheduler().run(plugin, location, task -> {
    spawnParticle(location);
});

// Global sync task (not tied to region — use sparingly)
plugin.getServer().getGlobalRegionScheduler().run(plugin, task -> {
    broadcastMessage();
});

// Global async task
plugin.getServer().getAsyncScheduler().runNow(plugin, task -> {
    fetchData();
});
```

## Cancel All Plugin Tasks on Disable

```java
@Override
public void onDisable() {
    Bukkit.getScheduler().cancelTasks(this);
}
```

## Decision: Which Scheduler?

```
Need to touch world/entity state?
    YES → sync scheduler (runTask / RegionScheduler)
    NO  → async scheduler (I/O, DB, HTTP)

Need Folia compatibility?
    YES → RegionScheduler / EntityScheduler / GlobalRegionScheduler
    NO  → BukkitScheduler is fine (Paper-only servers)
```

## Never Use

- `new BukkitRunnable() { ... }.runTask(plugin)` — verbose, no benefit over lambda
- `Thread.sleep()` — blocks the thread, never in plugin code
- `synchronized` blocks on main thread — causes lag spikes

**See also:** Use paper-core-standards for base project standards. Use paper-event-system for async event handling.
