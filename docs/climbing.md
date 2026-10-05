# Universal climbing

Open `Assets/Scenes/Home.unity`, enter Play Mode, choose a hero, and walk or jump toward a broad building wall while holding the movement direction toward it. The hero latches automatically when the wall is within reach. Climbing works with any hero and does not consume a power slot or stamina.

While attached, **W/S** climb up/down, **A/D** move sideways along the wall, **Space** jumps away and upward, and **Left Ctrl** releases the wall into normal airborne movement. Keep holding **W** near a clear roof lip to top out. A wall latch or wall jump does not restore the one air jump; actual ground contact does. The camera still uses its normal mouse look and **B** switches between third and first person. Flight remains the equipped **F** secondary: holding F while airborne transfers from a wall into Flight and spends fuel.

Suitable mostly vertical, tall, wide, static structural colliders are detected from geometry, so generated facades need no tags. Floors, ceilings, characters, moving cars, small clutter, and breakable props do not qualify. Top-out checks for a walkable landing and enough room for the standing CharacterController, including the path over a parapet. If the roof is obstructed, the hero remains on the wall rather than passing through it.

The movement and geometry values are in `Assets/Resources/GameTuning.asset` under Movement. Climbing is currently unlimited. The visual uses the existing Air animation as a placeholder; the player's upright physics root is moved only by the existing CharacterController. Attacks are conservatively unavailable while attached; Force Field can stay active. Human playtesting is still needed for latch responsiveness, speed, top-out feel, wall jump, and both camera views.
