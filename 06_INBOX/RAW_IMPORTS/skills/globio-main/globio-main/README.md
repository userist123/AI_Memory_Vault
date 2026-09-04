<p align="center">
  <img src="https://cloud.stanlink.online/globio/images/brand/globio-icon-transparent.png" alt="Globio Logo" width="100" />
</p>

<h1 align="center">Globio</h1>

<p align="center">
  <strong>The Operating System for Games</strong><br/>
  Replace Firebase and PlayFab with a unified platform that's 99% cheaper,<br/>globally distributed, and built specifically for games.
</p>

<p align="center">
  <a href="https://globio.stanl.ink">globio.stanl.ink</a> •
  <a href="https://globio.stanl.ink/docs">Docs</a> •
  <a href="https://globio.stanl.ink/pricing">Pricing</a> •
  <a href="https://discord.gg/globio">Discord</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/status-in%20development-orange" />
  <img src="https://img.shields.io/badge/locations-300%2B-blue" />
  <img src="https://img.shields.io/badge/cold%20starts-zero-brightgreen" />
  <img src="https://img.shields.io/badge/savings-up%20to%2099%25-green" />
</p>

---

## The Problem

Every game developer knows the pain. You pick Firebase to move fast, and it works — until your game gets any real traction. Then the bill arrives. Database reads, storage egress, function invocations — it all adds up fast. Firebase was never designed for high-volume gaming workloads, and neither was PlayFab. You end up paying a **success tax** just for growing.

Globio is built differently. A globally distributed network optimized for the patterns games actually use — real-time sync, high read volume, global low latency, player identity — and priced so that growth is something to celebrate, not fear.

---

## The Platform

Globio is made up of 10 services, split into the essentials and the power features:

### Core 5 — The Foundation

**GlobalDoc** — A Firebase-compatible document database with global distribution. Automatic indexing and a familiar API. Migrate from Firestore in hours.

**GlobalSync** — Real-time multiplayer infrastructure with stateful rooms. Players connect to the nearest node automatically. No desync. No dropped state.

**GlobalVault** — Infinite cloud storage for player saves, replays, screenshots, and user-generated content. Built on R2 with edge delivery.

**GlobalPulse** — Live configuration and feature flags. Push changes to every player instantly, without an app update. Kill switches, A/B tests, game balance tweaks — all live.

**Globio ID** — Cross-platform authentication. Anonymous, email, social, and custom auth flows. One identity that follows the player across every platform they play on.

### Power 5 — The Edge

**GlobalBrain** — AI/ML inference close to your players. Bring your NPCs to life, run content moderation, power recommendation systems — without a round-trip to a distant inference server.

**GlobalScope** — High-cardinality analytics built for games. Track everything — sessions, funnels, economy metrics, player behavior — without the enterprise price tag.

**GlobalSignal** — Push notifications and in-game messaging. Re-engage players, deliver events, and send messages that actually get seen.

**GlobalCode** — Serverless functions with 0ms cold start. Write your game logic once, run it close to every player.

**GlobalMart** — Virtual economy infrastructure. Item catalogs, currency management, IAP receipt validation, transaction integrity — all handled.

---

## Quick Start

```bash
npm install @globio/sdk
```

```typescript
import Globio from '@globio/sdk';

// Initialize
await Globio.connect({ gameId: 'your-game-id' });

// Auth
const user = await Globio.ID.loginAnonymously();

// Save player data
await Globio.Doc.set(`players/${user.id}`, {
  level: 1,
  score: 0,
});

// Join a multiplayer room
const room = await Globio.Sync.join('arena_1');
room.sync(playerTransform);
```

**Unity (C#)**
```csharp
using Globio.SDK;

public class GameManager : MonoBehaviour {
    async void Start() {
        await Globio.Connect();

        var user = await Globio.ID.LoginAnonymously();
        var room = await Globio.Sync.Join("level_1");

        room.Sync(transform); // Auto-syncs position

        await Globio.Vault.Save("stats", new {
            Level = 5,
            Score = 12000
        });
    }
}
```

---

## Pricing

| Plan | Price | MAU | Storage | Bandwidth |
|---|---|---|---|---|
| **Indie** | Free | 100k | 1GB | 10GB |
| **Pro** | $29/mo | Unlimited | 10GB | 1TB |
| **Enterprise** | Custom | Unlimited | Custom | Custom |

No surprise bills. No success tax. [See full pricing →](https://globio.stanl.ink/pricing)

---

## Why Globio is Fast

Globio runs across 300+ distributed locations worldwide. This means:

- Player traffic is automatically routed to the **nearest node** — no region selection needed
- **Average global latency of 36ms**
- **Zero cold starts** — functions are always warm
- **99.99% uptime SLA**

---

## SDK Support

| Platform | Status |
|---|---|
| Unity (C#) | 🚧 In Development |
| JavaScript / TypeScript | 🚧 In Development |
| Unreal (C++) | 📅 Planned |
| Godot (GDScript) | 📅 Planned |
| Python | 📅 Planned |

---

## Roadmap

We're building toward a full platform launch in **Q4 2026**. Services will be released progressively — follow this repo and watch for updates.

- [x] Platform architecture finalized
- [ ] GlobalDoc alpha
- [ ] Globio ID alpha
- [ ] GlobalSync alpha
- [ ] GlobalVault alpha
- [ ] Full SDK release
- [ ] Public beta
- [ ] v1.0 Launch

---

## Contributing

Globio is currently in active development. Contributions, feedback, and ideas are welcome. Open an issue to start a conversation.

---

## License

StaNLink — see [LICENSE](./LICENSE) for details.

---

<p align="center">
  <sub>Built for the world · © 2026 Globio Technologies</sub>
</p>
