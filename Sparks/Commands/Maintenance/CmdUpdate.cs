/*
    Copyright 2011 MCForge
        
    Dual-licensed under the    Educational Community License, Version 2.0 and
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using GoldenSparks.DB;
using GoldenSparks.SQL;

namespace GoldenSparks.Commands.Maintenance
{
    public sealed class CmdUpdate : Command2
    {
        public override string name { get { return "Update"; } }
        public override string shortcut { get { return ""; } }
        public override string type { get { return CommandTypes.Moderation; } }
        public override LevelPermission defaultRank { get { return LevelPermission.Nobody; } }

        public override void Use(Player p, string message, CommandData data)
        {
            DoUpdate(p);
        }
        static void DoUpdate(Player p)
        {
            if (!CheckPerms(p))
            {
                p.Message("Only GoldenSparks or the Server Owner can update the server."); return;
            }
            Updater.PerformUpdate();
        }

        static bool CheckPerms(Player p)
        {
            if (p.IsSparkie) return true;

            if (Server.Config.OwnerName.CaselessEq("Notch")) return false;
            return p.name.CaselessEq(Server.Config.OwnerName);
        }
        public override void Help(Player p)
        {
            p.Message("&T/Update &H- Force updates the server");
        }
    }
}
