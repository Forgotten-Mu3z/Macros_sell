---
name: paper-scoreboard
description: Use when creating sidebars, tab list displays, name tags, or team-based scoreboards in a Paper plugin
---

# Scoreboard

## Overview

Use per-player scoreboards — never the server's main scoreboard. Paper 26.1 scoreboard API is component-based (Adventure).

## Per-Player Sidebar

```java
public void createSidebar(Player player) {
    ScoreboardManager manager = Bukkit.getScoreboardManager();
    Scoreboard board = manager.getNewScoreboard();

    Objective obj = board.registerNewObjective(
        "sidebar",                        // internal name (unique per board)
        Criteria.DUMMY,                   // criteria
        MM.deserialize("<gold><bold>MyServer") // display name (Component)
    );
    obj.setDisplaySlot(DisplaySlot.SIDEBAR);

    // Add lines — higher score = higher on sidebar
    obj.getScore(plainText("<white>Coins: <gold>100")).setScore(3);
    obj.getScore(plainText("<gray>Rank: <green>VIP")).setScore(2);
    obj.getScore(plainText("<dark_gray>play.myserver.net")).setScore(1);

    player.setScoreboard(board);
}

private String plainText(String miniMessage) {
    return PlainTextComponentSerializer.plainText().serialize(
        MM.deserialize(miniMessage)
    );
}
```

## Update a Sidebar Line

```java
public void updateCoins(Player player, int coins) {
    Scoreboard board = player.getScoreboard();
    Objective obj = board.getObjective("sidebar");
    if (obj == null) return;

    // Remove old entry, add updated one at same score
    board.getEntries().stream()
        .filter(e -> board.getScore(e).getScore() == 3)
        .forEach(board::resetScores);

    obj.getScore(plainText("<white>Coins: <gold>" + coins)).setScore(3);
}
```

## Teams — Name Tags and Colors

```java
public void setPlayerTeam(Player player, String teamName, String prefix) {
    Scoreboard board = player.getScoreboard();

    Team team = board.getTeam(teamName);
    if (team == null) {
        team = board.registerNewTeam(teamName);
    }

    team.prefix(MM.deserialize(prefix));          // "<red>[Admin] "
    team.color(NamedTextColor.RED);
    team.addPlayer(player);
}
```

## Tab List Header/Footer

```java
player.sendPlayerListHeaderAndFooter(
    MM.deserialize("<gold><bold>MyServer"),
    MM.deserialize("<gray>Players online: <white>" + Bukkit.getOnlinePlayers().size())
);
```

## Display Name in Tab List

```java
player.playerListName(MM.deserialize("<red>[Admin] <white>" + player.getName()));
```

## Cleanup on Quit

```java
@EventHandler
public void onQuit(PlayerQuitEvent event) {
    // Per-player scoreboards are GC'd when player disconnects.
    // If you hold references, clear them here.
    scoreboards.remove(event.getPlayer().getUniqueId());
}
```

## Common Mistakes

| Mistake | Fix |
|---|---|
| Using `Bukkit.getScoreboardManager().getMainScoreboard()` | Use `getNewScoreboard()` per player |
| Setting scores with legacy `§` color strings | Use `PlainTextComponentSerializer` to strip formatting for score keys |
| Forgetting to call `player.setScoreboard(board)` | Always assign the new board to the player |

**See also:** Use paper-core-standards for base project standards. Use paper-adventure-text for MiniMessage formatting.
