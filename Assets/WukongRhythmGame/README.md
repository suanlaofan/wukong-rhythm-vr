# Wukong Rhythm Game

The active battle is built into `Assets/Scenes/SampleScene.unity` through:

`Tools > Wukong > Build PICO Rhythm Battle`

Runtime controls:

- PICO/XR: the right-hand controller pose directly drives the staff endpoint; swing the controller through incoming rocks.
- Editor simulation: move the mouse to position the striking tip across the view, left-click or Space to perform a short swing, and `R` to replay from results.

The combat uses Unity's generic XR device pose and haptics APIs so it remains playable in the Editor without an XR loader and can bind to a PICO right-hand controller when the PICO/OpenXR loader is enabled.

The scene builder is idempotent. Re-running it replaces only the generated `Wukong Rhythm Game` root and preserves the environment, terrain, LavaElemental, and staff source assets.
