---
name: paper-dialog-system
description: Use when creating menus, GUIs, chest inventory interfaces, dialog flows, or player input forms in a Paper plugin
---

# Dialog System

## Overview

Prefer Dialog API over inventory GUIs and raw `sendMessage()`. Dialog API provides native Minecraft UI without fake chest inventories.

Reference: https://minecraft.wiki/w/Dialog

## When to Use Dialog API

| Old approach | New approach |
|---|---|
| Chest inventory GUI | Dialog API |
| `sendMessage()` for multi-step interaction | Dialog API |
| Fake sign/book input | Dialog API input form |
| NPC conversation | Dialog API |

## Dialog Storage

Store all dialog definitions in `dialogues.yml` — not hardcoded in Java.

`dialogues.yml` example:
```yaml
confirm-purchase:
  title: "<gold>Confirm Purchase"
  body: "Buy <item> for <price> coins?"
  buttons:
    - label: "Confirm"
      action: CONFIRM_PURCHASE
    - label: "Cancel"
      action: CLOSE
```

## Java Usage Pattern

> **Note:** Dialog API is new in Paper 26.1. Before writing implementation code, verify exact method signatures at:
> `https://jd.papermc.io/paper/1.21/` → search `Dialog`

```java
// Load dialog from config
FileConfiguration dialogs = YamlConfiguration.loadConfiguration(dialogsFile);

// Build and show dialog — verify exact builder API in Paper 26.1 javadocs
Dialog dialog = Dialog.builder()
    .title(MiniMessage.miniMessage().deserialize(dialogs.getString("confirm-purchase.title")))
    .body(MiniMessage.miniMessage().deserialize(dialogs.getString("confirm-purchase.body")))
    .build();

player.showDialog(dialog);
```

## Listening to Dialog Response

```java
// Verify event class name against current Paper javadocs
@EventHandler
public void onDialogResponse(PlayerDialogResponseEvent event) {
    if (event.getDialog().getId().equals("confirm-purchase")) {
        String action = event.getButton().action();
        if (action.equals("CONFIRM_PURCHASE")) {
            processPurchase(event.getPlayer());
        }
    }
}
```

## Fallback

If Dialog API is unavailable (old Paper build), fall back to `sendMessage()` with clickable components via Adventure API. Never fall back to chest GUIs.

**See also:** Use paper-core-standards for base project standards. Use paper-adventure-text for MiniMessage text formatting.
