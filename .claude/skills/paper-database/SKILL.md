---
name: paper-database
description: Use when storing player data at scale (hundreds of players), setting up SQLite or H2 databases, connection pooling, or running async database queries in a Paper plugin
---

# Database

## Overview

Use PDC for simple per-object data. Use SQLite (single server) or H2 (embedded, fast) when you need queries, joins, or data for hundreds of players. Always query async — never block the main thread.

## When to Use

| Scenario | Solution |
|---|---|
| Custom data on 1 item/entity | PersistentDataContainer (see paper-data-persistence) |
| Player stats, balances, history | SQLite / H2 + HikariCP |
| Cross-server data | MySQL / MariaDB + HikariCP |

## build.gradle

Paper bundles HikariCP — use it directly. Include only the JDBC driver:

```gradle
dependencies {
    // SQLite driver — include, don't shade (Paper 26.1+ loads JDBC drivers)
    implementation("org.xerial:sqlite-jdbc:3.45.3.0")
    // OR H2 (faster, in-memory option)
    implementation("com.h2database:h2:2.2.224")
}
```

## HikariCP Setup

```java
public class DatabaseManager {
    private final HikariDataSource dataSource;

    public DatabaseManager(Plugin plugin) {
        HikariConfig config = new HikariConfig();
        config.setJdbcUrl("jdbc:sqlite:" + plugin.getDataFolder() + "/data.db");
        config.setMaximumPoolSize(5);
        config.setMinimumIdle(1);
        config.setConnectionTimeout(30_000);
        config.setIdleTimeout(600_000);
        this.dataSource = new HikariDataSource(config);
    }

    public Connection getConnection() throws SQLException {
        return dataSource.getConnection();
    }

    public void close() {
        dataSource.close();
    }
}
```

## Schema Creation (onEnable)

```java
public void createTables() {
    String sql = """
        CREATE TABLE IF NOT EXISTS player_data (
            uuid        TEXT PRIMARY KEY,
            coins       INTEGER NOT NULL DEFAULT 0,
            last_seen   INTEGER NOT NULL
        )
        """;
    try (Connection conn = db.getConnection();
         Statement stmt = conn.createStatement()) {
        stmt.execute(sql);
    } catch (SQLException e) {
        plugin.getLogger().severe("Failed to create tables: " + e.getMessage());
    }
}
```

## Async Read / Sync Apply Pattern

Never query on the main thread. Always schedule back to sync for world operations:

```java
public CompletableFuture<Integer> getCoins(UUID playerUuid) {
    return CompletableFuture.supplyAsync(() -> {
        String sql = "SELECT coins FROM player_data WHERE uuid = ?";
        try (Connection conn = db.getConnection();
             PreparedStatement ps = conn.prepareStatement(sql)) {
            ps.setString(1, playerUuid.toString());
            ResultSet rs = ps.executeQuery();
            return rs.next() ? rs.getInt("coins") : 0;
        } catch (SQLException e) {
            plugin.getLogger().severe("DB error: " + e.getMessage());
            return 0;
        }
    });
}

// Usage — async fetch, sync apply
getCoins(player.getUniqueId()).thenAccept(coins -> {
    Bukkit.getScheduler().runTask(plugin, () -> {
        player.sendMessage(MM.deserialize("<gold>Coins: <white>" + coins));
    });
});
```

## Write Pattern

```java
public CompletableFuture<Void> setCoins(UUID uuid, int coins) {
    return CompletableFuture.runAsync(() -> {
        String sql = """
            INSERT INTO player_data (uuid, coins, last_seen)
            VALUES (?, ?, ?)
            ON CONFLICT(uuid) DO UPDATE SET coins = excluded.coins, last_seen = excluded.last_seen
            """;
        try (Connection conn = db.getConnection();
             PreparedStatement ps = conn.prepareStatement(sql)) {
            ps.setString(1, uuid.toString());
            ps.setInt(2, coins);
            ps.setLong(3, System.currentTimeMillis());
            ps.executeUpdate();
        } catch (SQLException e) {
            plugin.getLogger().severe("DB write error: " + e.getMessage());
        }
    });
}
```

## Lifecycle

```java
@Override
public void onEnable() {
    db = new DatabaseManager(this);
    db.createTables();
}

@Override
public void onDisable() {
    db.close();
}
```

**See also:** Use paper-core-standards for base project standards. Use paper-data-persistence for simple per-object storage. Use paper-scheduler for async patterns.
