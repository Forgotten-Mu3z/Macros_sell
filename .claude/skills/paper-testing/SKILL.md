---
name: paper-testing
description: Use when writing tests for a Paper plugin — MockBukkit enables unit testing without a running server
---

# Testing

## Overview

Use MockBukkit to unit test Paper plugins without a real server. Test commands, events, and logic in isolation.

## build.gradle

```gradle
dependencies {
    testImplementation("com.github.seeseemelk:MockBukkit-v1.21:3.93.2")
    testImplementation("org.junit.jupiter:junit-jupiter:5.10.2")
}

test {
    useJUnitPlatform()
}
```

Check latest MockBukkit version: https://github.com/MockBukkit/MockBukkit/releases

## Basic Test Setup

```java
@ExtendWith(MockitoExtension.class)
class EconomyTest {
    private ServerMock server;
    private MyPlugin plugin;

    @BeforeEach
    void setUp() {
        server = MockBukkit.mock();
        plugin = MockBukkit.load(MyPlugin.class);
    }

    @AfterEach
    void tearDown() {
        MockBukkit.unmock();
    }
}
```

Always call `MockBukkit.unmock()` in `@AfterEach` — leaked mocks cause flaky test suites.

## Player Mocks

```java
@Test
void playerGetsCoinsOnJoin() {
    PlayerMock player = server.addSimplePlayer();

    // Simulate PlayerJoinEvent
    server.getPluginManager().callEvent(new PlayerJoinEvent(player, Component.empty()));

    // Assert
    assertEquals(100, plugin.getEconomy().getCoins(player.getUniqueId()));
}
```

## Command Testing

```java
@Test
void healCommandRestoresHealth() {
    PlayerMock player = server.addSimplePlayer();
    player.setHealth(1.0);

    // Execute command as player
    boolean result = server.dispatchCommand(player, "heal");

    assertTrue(result);
    assertEquals(player.getMaxHealth(), player.getHealth(), 0.01);
}

@Test
void healCommandRequiresPermission() {
    PlayerMock player = server.addSimplePlayer();
    // player has no permissions by default

    server.dispatchCommand(player, "heal");

    player.assertSaid(PlainTextComponentSerializer.plainText().serialize(
        MiniMessage.miniMessage().deserialize("<red>No permission.")
    ));
}
```

## Event Testing

```java
@Test
void customEventFiresOnPurchase() {
    PlayerMock player = server.addSimplePlayer();

    plugin.getShop().purchase(player, "sword");

    // Verify a custom event was fired
    server.getPluginManager().assertEventFired(PurchaseEvent.class,
        event -> event.getPlayer().equals(player)
    );
}
```

## Testing Scheduled Tasks

```java
@Test
void rewardTickRunsEverySecond() {
    PlayerMock player = server.addSimplePlayer();

    // Advance server ticks
    server.getScheduler().performTicks(20L); // 1 second

    assertEquals(10, plugin.getEconomy().getCoins(player.getUniqueId()));
}
```

## What MockBukkit Cannot Test

- NMS / internals (use Paper API instead — it's testable)
- Database I/O — mock the `DatabaseManager` with Mockito
- Network / HTTP — mock with WireMock or Mockito
- Actual client rendering (Dialog API, resource packs)

## Run Tests

```bash
./gradlew test
```

**See also:** Use paper-core-standards for base project standards.
