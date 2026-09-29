# Side-on push door

The working example is `DoorRoot` in `Assets/Scenes/SampleScene.unity`, beneath the existing `Square (7)` at about X -4.34. Enter Play mode and walk into it from either side. There is no E interaction.

Duplicate the whole `DoorRoot`, or drag `SideViewDoor.prefab` into another room. Its three child references remain internal to the duplicate. Position the root at the vertical hinge edge, halfway up the doorway. Keep the root upright with unit scale; change Doorway Size on the component to resize the panel, blocker and sensor together. Hinge On Right puts the panel on the other side of the root.

Hierarchy:

```
DoorRoot          SideViewDoor (hinge pivot)
  DoorVisual      cube mesh + MeshRenderer; no physics components
  DoorBlocker     solid BoxCollider2D
  DoorSensor      trigger BoxCollider2D
```

Only DoorVisual moves: its centre orbits DoorRoot and its orientation rotates around local Y. DoorRoot and both 2D colliders stay upright. Closed is 0 degrees. Maximum Angle defaults to 83, Pass Through Angle to 60, Solid Again Angle to 10. The door uses angular velocity, Push Strength, Damping and Closing Spring Strength. Sensor Size controls how close a player must approach to push. Clearance Margin prevents the blocker returning while a player occupies the doorway. The visual holds while occupied, including when the player stops moving.

The player needs a simulated Rigidbody2D, a solid Collider2D, and the Player tag on the body or collider. Player1 already has these. SideViewDoor runs after PlayerMovement's default-order FixedUpdate, reading `linearVelocity` before the collision solver stops it at the closed door. No changes to PlayerMovement or PlayerInteraction are needed. Keep the door children off the Interactable layer so they cannot intercept candle interactions.

The plain brown material is URP Unlit, with no downloaded or generated artwork. No 3D rigidbody, joint or collider is needed. Do not add HingeJoint2D or rotate DoorRoot around Y.

The optional Tools > Nightguard > Configure Selected Side-on Door menu converts a new, unconfigured DoorRoot with a cube DoorVisual child. For ordinary reuse, duplicate the configured root or use the prefab instead.

Verified in Unity 6000.6.2f1 using isolated Play-mode copies of the actual player, candle and camera: both directions of travel; stationary/retreating player does not push; release at 60 degrees; 83-degree limit; fixed hinge edge; stationary doorway occupancy; automatic safe closing; independent duplicate; existing candle E interaction, timer depletion and extinguishing; camera X/Y bounds and depth. PlayerMovement, PlayerInteraction, CandleInteraction, CameraXYFollow and their metadata were unchanged. Before/after scene comparison found changes only within the door hierarchy; existing unsaved candle-light settings were retained.
