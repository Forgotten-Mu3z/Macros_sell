---
name: paper-brigadier
description: Use when adding commands to a Paper plugin — Brigadier is the modern command API in Paper 26.1+ and should be used instead of CommandExecutor
---

# Brigadier Commands

## Overview

Paper 26.1+ uses Brigadier for commands. Brigadier gives native client-side autocomplete, typed arguments, and tree-structured commands. Use it instead of `CommandExecutor`.

## Registration (Paper 26.1+)

```java
@Override
public void onEnable() {
    LifecycleEventManager<Plugin> manager = this.getLifecycleManager();
    manager.registerEventHandler(LifecycleEvents.COMMANDS, event -> {
        Commands commands = event.registrar();
        registerCommands(commands);
    });
}

private void registerCommands(Commands commands) {
    commands.register(
        Commands.literal("heal")
            .requires(sender -> sender.getSender().hasPermission("myplugin.heal"))
            .executes(ctx -> {
                CommandSender sender = ctx.getSource().getSender();
                if (sender instanceof Player player) {
                    player.setHealth(player.getMaxHealth());
                    player.sendMessage(MiniMessage.miniMessage().deserialize("<green>Healed!"));
                }
                return Command.SINGLE_SUCCESS;
            })
            .then(Commands.argument("target", ArgumentTypes.player())
                .requires(sender -> sender.getSender().hasPermission("myplugin.heal.other"))
                .executes(ctx -> {
                    PlayerSelectorArgumentResolver resolver = ctx.getArgument("target", PlayerSelectorArgumentResolver.class);
                    List<Player> targets = resolver.resolve(ctx.getSource());
                    targets.forEach(p -> p.setHealth(p.getMaxHealth()));
                    return Command.SINGLE_SUCCESS;
                })
            )
            .build(),
        "Heal yourself or another player",
        List.of("h")  // aliases
    );
}
```

## Paper Argument Types

Paper provides built-in argument types via `ArgumentTypes`:

| Method | Type returned |
|---|---|
| `ArgumentTypes.player()` | `PlayerSelectorArgumentResolver` |
| `ArgumentTypes.players()` | `PlayerSelectorArgumentResolver` (multi) |
| `ArgumentTypes.world()` | `World` |
| `ArgumentTypes.namespacedKey()` | `NamespacedKey` |
| `ArgumentTypes.component()` | `Component` |

Standard Brigadier types (from `com.mojang.brigadier.arguments`):
- `StringArgumentType.word()` — single word
- `StringArgumentType.greedyString()` — rest of input
- `IntegerArgumentType.integer(min, max)`
- `DoubleArgumentType.doubleArg()`
- `BoolArgumentType.bool()`

## Permission Check

Always use `.requires()` — this hides the command from players without permission (client-side too):

```java
Commands.literal("admin")
    .requires(sender -> sender.getSender().hasPermission("myplugin.admin"))
```

## plugin.yml

You still need the command declared in plugin.yml for the description and usage message:

```yaml
commands:
  heal:
    description: Heal a player
    usage: /heal [player]
    permission: myplugin.heal
```

## vs CommandExecutor

| CommandExecutor | Brigadier |
|---|---|
| Manual argument parsing | Typed arguments, parsed automatically |
| String-based tab complete | Native client-side autocomplete |
| No type safety | Compile-time argument types |
| Simple to set up | More verbose but more powerful |

Use **Brigadier** for new commands. Only use `CommandExecutor` when migrating old code.

**See also:** Use paper-core-standards for base project standards. Use paper-adventure-text for sending messages.
