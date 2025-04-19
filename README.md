# Resonite Hand Aligner

A [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod for [Resonite](https://resonite.com/) that adds more accurate auto alignment of hands when importing avatars. 
This mod is practically essential for setting up avatars while in desktop mode, and is a great quality of life improvement in VR. When symmetery is turned off, the avatar creator can accurately positions hands in any orientation and location with perfect accuracy. (or at least very good accuracy)

Credit to the code used to position the hands goes to Phylliida. The original code used in this project can be found below.
https://github.com/Phylliida/ResoniteUnityExporter/blob/main/ImportFromUnityLib/ImportAvatar.cs

## Screenshots
![image](https://github.com/user-attachments/assets/25433e6e-94de-4021-b50a-e8346b70b87f)
![image](https://github.com/user-attachments/assets/cd77329c-290f-4579-869b-425a4bd4cfc4)

*even with asymetric hand placement and irregular rotations, the hands align perfectly*

## Installation
1. Install [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader).
1. Place [HandAligner.dll](https://github.com/AwesomeTornado/Resonite-Hand-Aligner/releases/latest/download/HandAligner.dll) into your `rml_mods` folder. This folder should be at `C:\Program Files (x86)\Steam\steamapps\common\Resonite\rml_mods` for a default install. You can create it if it's missing, or if you launch the game once with ResoniteModLoader installed it will create this folder for you.
1. Start the game. If you want to verify that the mod is working you can check your Resonite logs.



fixes: Yellow-Dog-Man/Resonite-Issues#3939, Yellow-Dog-Man/Resonite-Issues#230
