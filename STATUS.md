# Prototype Status

## Built

- Third-person controller: WASD movement, Left Shift run, Space jump.
- Flight: hold `F` while airborne. Flight has 6.0 seconds of fuel and refills at 2.5 fuel/second while grounded.
- Super-strength punch: left mouse or `E`. It has 3 charges, a 0.45-second cooldown, and recovers a charge every 1.25 seconds.
- Tiny physics arena generated at play time with crates, barrels, and stacked objects. Punches use `Rigidbody.AddForce` / `AddExplosionForce`; props retain physics and can break apart from sufficiently hard impacts.
- HUD provides live numbers for fuel, charges, cooldown, force applied, and last punch outcome.

## Verification controls

Enter Play Mode, then press `V` to run the built-in deterministic validation sequence. It prints concrete telemetry to the Console and shows it in the HUD: flight fuel is sampled at 6.000, 4.000, and 0.000 seconds; it then demonstrates ground recharge. The punch test records three successful charged punches, a rejected zero-charge punch, a rejected cooldown attempt, then a recovered successful punch. The force test prints the exact configured force (1350 N) and affected rigidbody count.

The verifier drives the same resource and punch methods used by gameplay; it is deliberately available in the running prototype so the values can be checked alongside the actual physics response.
