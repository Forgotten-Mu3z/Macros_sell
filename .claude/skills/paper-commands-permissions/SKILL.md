---
name: paper-commands-permissions
description: Use when adding commands, implementing CommandExecutor, setting up permissions, or adding TabCompletion to a Paper plugin
---

# Commands & Permissions

## Overview

Every command gets a permission. Every permission is checked before execution. Every argument gets tab completion.

## plugin.yml

```yaml
commands:
  heal:
    description: Heal a player
    usage: /heal [player]
    permission: myplugin.heal

permissions:
  myplugin.heal:
    description: Allows using /heal
    default: op
  myplugin.*:
    description: All myplugin permissions
    default: op
    children:
      myplugin.heal: true
```

## Permission Format

`<pluginname>.<command>` — all lowercase, dots as separators.

Examples: `economy.balance`, `shop.buy`, `myplugin.admin.reload`

## CommandExecutor

```java
public class HealCommand implements CommandExecutor {
    @Override
    public boolean onCommand(CommandSender sender, Command cmd, String label, String[] args) {
        if (!sender.hasPermission("myplugin.heal")) {
            sender.sendMessage(MiniMessage.miniMessage().deserialize("<red>No permission."));
            return true;
        }
        // execute
        return true;
    }
}
```

Always return `true` after handling — returning `false` shows the usage string from plugin.yml.

## TabCompleter

```java
public class HealCommand implements CommandExecutor, TabCompleter {
    @Override
    public List<String> onTabComplete(CommandSender sender, Command cmd, String alias, String[] args) {
        if (args.length == 1) {
            return Bukkit.getOnlinePlayers().stream()
                .map(Player::getName)
                .filter(name -> name.toLowerCase().startsWith(args[0].toLowerCase()))
                .collect(Collectors.toList());
        }
        return List.of();
    }
}
```

## Registration

```java
PluginCommand cmd = getCommand("heal");
HealCommand handler = new HealCommand();
cmd.setExecutor(handler);
cmd.setTabCompleter(handler);
```

**See also:** Use paper-core-standards for base project standards.
