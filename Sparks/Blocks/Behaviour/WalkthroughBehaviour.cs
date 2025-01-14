﻿/*
    Copyright 2010 MCSharp team (Modified for use with MCZall/MCLawl/MCGalaxy)
    
    Dual-licensed under the Educational Community License, Version 2.0 and
    the GNU General Public License, Version 3 (the "Licenses"); you may
    not use this file except in compliance with the Licenses. You may
    obtain a copy of the Licenses at
    
    http://www.opensource.org/licenses/ecl2.php
    http://www.gnu.org/licenses/gpl-3.0.html
    
    Unless required by applicable law or agreed to in writing,
    software distributed under the Licenses are distributed on an "AS IS"
    BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
    or implied. See the Licenses for the specific language governing
    permissions and limitations under the Licenses.
 */
using GoldenSparks.Blocks.Extended;
using GoldenSparks.Blocks.Physics;
using GoldenSparks.Network;
using BlockID = System.UInt16;

namespace GoldenSparks.Blocks {
    public static class WalkthroughBehaviour {

        public static bool Door(Player p, BlockID block, ushort x, ushort y, ushort z) {
            if (p.level.physics == 0) return true;

            BlockID physForm;
            PhysicsArgs args = ActivateablePhysics.GetDoorArgs(block, out physForm);
            p.level.Blockchange(x, y, z, physForm, false, args);
            return true;
        }

        public static bool Train(Player p, BlockID block, ushort x, ushort y, ushort z) {
            if (!p.trainInvulnerable && p.level.Config.KillerBlocks) p.HandleDeath(Block.Train);
            return true;
        }

        public static bool DoPortal(Player p, BlockID block, ushort x, ushort y, ushort z) {
            if (p.level.PosToInt(x, y, z) == p.lastWalkthrough) return true;
            return Portal.Handle(p, x, y, z);
        }

        public static bool DoMessageBlock(Player p, BlockID block, ushort x, ushort y, ushort z) {
            if (p.level.PosToInt(x, y, z) == p.lastWalkthrough) return true;
            return MessageBlock.Handle(p, x, y, z, false);
        }

        public static bool Checkpoint(Player p, BlockID block, ushort x, ushort y, ushort z) {
            p.useCheckpointSpawn = true;
            p.checkpointX = x; p.checkpointY = (ushort)(y + 1); p.checkpointZ = z;
            p.checkpointRotX = p.Rot.RotY; p.checkpointRotY = p.Rot.HeadX;
            
            int index = p.level.PosToInt(x, y, z);
            if (index != p.lastCheckpointIndex) {
                Position pos = p.Pos;
                pos.X = x * 32 + 16; pos.Z = z * 32 + 16;
                if (Server.Config.CheckpointsRespawnClientside) {
                    if (p.Supports(CpeExt.SetSpawnpoint)) {
                        p.Send(Packet.SetSpawnpoint(pos, p.Rot, p.hasExtPositions));
                    } else {
                        p.SendPos(Entities.SelfID, pos, p.Rot);
                        Entities.Spawn(p, p);
                    }
                    p.Message("Your spawnpoint was updated.");
                }
                p.lastCheckpointIndex = index;
                return true;
            }
            
            // allow activating other blocks (e.g. /mb message above it)
            return false;
        }
    }
}
