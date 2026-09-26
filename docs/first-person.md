# First-person view

Press **B** during play to toggle the existing gameplay camera. Third-person remains the
default for fresh/older saves. Both Hero and Villain use the same camera implementation;
no scene or Inspector wiring is required. Menu, pause, defeat and ended-session states
reject the toggle.

`GameTuning.asset > Camera` owns the new configuration: B, eye height 1.62m, near clip
0.03m, eye collision radius 0.15m, pitch -85..85 degrees, and forward-flight FOV +5 degrees
at 8m/s with exponential response 5/s. These are fields in the existing CameraSettings.
The camera follows the physics capsule, not animated head bob/flight/landing bones.
Thus the view stays upright and stable in flight, flips and landing squash. Forward flight
widens FOV; hover does not. Synergy FOV kick adds to this in the camera rather than competing
with another writer.

Only renderers below the player's existing humanoid visual root become ShadowsOnly.
Animator, colliders, NPC renderers and presentation events are unchanged. Toggle back or
disable the camera component restores the exact prior shadow-rendering settings and near
clip. There are no first-person hands or weapon models in this pass.

The eye is swept from the capsule center to its requested height. Its collision sphere
contains the near-plane corners at the configured FOV and current aspect ratio; triggers
and the player are excluded. The unchanged CharacterController keeps the capsule center
out of walls. This is not an arbitrary teleport-out-of-solid-geometry solver.

First-person PowerUser uses the camera position as aim origin and the viewport-center
ray as look direction. Third-person retains shoulder-to-crosshair convergence. Ice and
Telekinesis share FindTarget. Fire resolves aim before spawning its own collider and
sweeps the projectile radius along its muzzle offset, so a wall closer than the previous
1.7m spawn offset cannot be skipped. If the origin itself is obstructed, it refuses the
cast before resource payment. This close-wall safety also applies to third-person.
Trigger-only objects no longer intercept aimed powers.

`ProgressSave.FirstPerson` is an additive boolean in the existing version-1 atomic JSON
save. PlayerProgression.SetFirstPerson saves immediately; no new preferences file or
PlayerPrefs framework. Missing fields default false. Scene changes and process restarts
load through the normal profile path.

Verification: run `FirstPersonVerification.Run`, followed by `FirstPersonVerification.Reload`
in a separate Unity process, without `-quit`. The runner uses isolated save paths under
Verification/FirstPerson; no normal player save is touched. Tests call ToggleView (the
B handler's entry point), not a simulated hardware key. Aiming uses real resource-gated
casts, projectile collisions, FrozenBody and telekinetic ownership, with off-axis and
wall-blocked controls. Rendering captures composite the existing HUD with the actual
game camera, including the crosshair. FPS uses that one enabled camera without extra
Camera.Render calls. See STATUS and Verification/FirstPerson for actual output and limits.
