---
name: paper-data-persistence
description: Use when storing custom data on items, entities, blocks, or chunks in a Paper plugin without external files or databases
---

# Data Persistence

## Overview

`PersistentDataContainer` (PDC) is the Paper-native way to attach custom data to game objects. No NMS, no external files, survives restarts.

## Supported Objects

`ItemStack` (via `ItemMeta`), `Entity`, `Block` (via `BlockState`), `Chunk`, `World`

## Setup — NamespacedKey

Always create keys from your plugin instance:

```java
// Store as a field — reuse, don't recreate every call
private final NamespacedKey coinKey = new NamespacedKey(plugin, "coins");
private final NamespacedKey ownerKey = new NamespacedKey(plugin, "owner-uuid");
```

Key format becomes: `pluginname:coins` — unique to your plugin.

## Read / Write / Check

```java
PersistentDataContainer pdc = entity.getPersistentDataContainer();

// Write
pdc.set(coinKey, PersistentDataType.INTEGER, 100);

// Read (returns null if absent)
Integer coins = pdc.get(coinKey, PersistentDataType.INTEGER);

// Read with default
int coins = pdc.getOrDefault(coinKey, PersistentDataType.INTEGER, 0);

// Check existence
boolean hasCoins = pdc.has(coinKey, PersistentDataType.INTEGER);

// Remove
pdc.remove(coinKey);
```

## Items (via ItemMeta)

```java
ItemStack item = new ItemStack(Material.DIAMOND_SWORD);
ItemMeta meta = item.getItemMeta();
meta.getPersistentDataContainer().set(ownerKey, PersistentDataType.STRING, player.getUniqueId().toString());
item.setItemMeta(meta);
```

## Complex Data — JSON String

For objects/lists, serialize to JSON and store as STRING:

```java
// Write
String json = new Gson().toJson(myDataObject);
pdc.set(dataKey, PersistentDataType.STRING, json);

// Read
String json = pdc.get(dataKey, PersistentDataType.STRING);
MyData data = new Gson().fromJson(json, MyData.class);
```

## Available PersistentDataTypes

`BYTE`, `SHORT`, `INTEGER`, `LONG`, `FLOAT`, `DOUBLE`, `STRING`, `BYTE_ARRAY`, `INTEGER_ARRAY`, `LONG_ARRAY`, `TAG_CONTAINER`, `TAG_CONTAINER_ARRAY`

## When NOT to Use PDC

- Large datasets (thousands of players) → use SQLite/H2 (don't shade JDBC)
- Cross-plugin shared data → use a dedicated API plugin

**See also:** Use paper-core-standards for base project standards.
