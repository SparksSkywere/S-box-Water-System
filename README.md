# S&box Water System

Going with the flow! Volumes, rivers, swimming, buoyancy, all for s&box.

Any issues please open an issue

## Features

- **Water Volumes** — box/sphere volumes with swim triggers and surface visuals, audio can also be configured
- **Rivers** — editable spline rivers in segment design
- **Water Presence** — player swim wiring, splash, edge/buoyant/underwater audio, underwater post-process + fog
- **Water Buoyant** — optional bouyancy for props, players and rigidbodies
- **Editor tools** — spawn window + River Path scene overlay for ease of use
 
## Folder layout

**NOTE** Sounds are not added in this code due to copywrite, you will need to provide your own sounds

```
Code/WaterSystem/     Runtime components
Editor/Water/         Editor tools and spawn menu
Assets/shaders/       Generic Water surface + underwater post-process
Assets/materials/     Generic water material
```

## Setup

1. Copy the folders "Assets", "Code" and "Editor" into your s&box project (top level).

<img src="https://github.com/SparksSkywere/S-box-Water-System/blob/main/README/WaterSystem_Copy_Top_Level.png" width="50%">

2. Open **Tools → Water**.

<img src="https://github.com/SparksSkywere/S-box-Water-System/blob/main/README/WaterSystem_Tools_Water.png" width="50%">

3. Spawn a **Manager** for the world, and add **Water Presence** to your player.

<img src="https://github.com/SparksSkywere/S-box-Water-System/blob/main/README/WaterSystem_Select_Manager_and_Presence.png" width="50%">

1. Attach sounds according to the player and water type chosen

<img src="https://github.com/SparksSkywere/S-box-Water-System/blob/main/README/WaterSystem_Player_Sound.png" width="50%">

5. Spawn a volume or river of your choosing

<img src="https://github.com/SparksSkywere/S-box-Water-System/blob/main/README/WaterSystem_Select_WaterType.png" width="50%">

6. Alter the water as you would like, in the editor you can choose how you alter the water, rivers are good to use with points to add segments, this is the screenshot for rivers, other 

<img src="https://github.com/SparksSkywere/S-box-Water-System/blob/main/README/WaterSystem_Water_Edit.png" width="50%">

<img src="https://github.com/SparksSkywere/S-box-Water-System/blob/main/README/WaterSystem_Water_Edit2.png" width="50%">

7. Enter play mode and walk into the water.

<img src="https://github.com/SparksSkywere/S-box-Water-System/blob/main/README/WaterSystem_Water_Scene.png" width="50%">

<img src="https://github.com/SparksSkywere/S-box-Water-System/blob/main/README/WaterSystem_Underwater.png" width="50%">

## Notes

1. When selecting rivers it will keep the first point selected to show where the first position is
2. You can move water volumes around; they do not have to be under the manager

<img src="https://github.com/SparksSkywere/S-box-Water-System/blob/main/README/WaterSystem_Water_Hierarchy.png">


## License

This repository is licensed under the MIT License as provided in the LICENSE file included with the project.