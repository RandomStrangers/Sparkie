/*
    Copyright 2010 MCSharp team (Modified for use with MCZall/MCLawl/GoldenSparks)
    
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
using System;
using System.Collections.Generic;
using GoldenSparks.Core;
using GoldenSparks.Modules.Moderation.Notes;
using GoldenSparks.Modules.Relay.Discord;
using GoldenSparks.Modules.Relay.IRC;
using GoldenSparks.Modules.Relay1.Discord1;
using GoldenSparks.Modules.Relay1.IRC1;
using GoldenSparks.Modules.Relay2.Discord2;
using GoldenSparks.Modules.Relay2.IRC2;
using GoldenSparks.Modules.GlobalRelay.GlobalDiscord;
using GoldenSparks.Modules.GlobalRelay.GlobalIRC;
using GoldenSparks.Modules.Security;
using GoldenSparks.Scripting;
using GoldenSparks.Network;
using System.Net;

namespace GoldenSparks 
{
    /// <summary> This class provides for more advanced modification to GoldenSparks </summary>
    public abstract class Plugin 
    {
        /// <summary> Hooks into events and initalises states/resources etc </summary>
        /// <param name="auto"> True if plugin is being automatically loaded (e.g. on server startup), false if manually. </param>
        public abstract void Load(bool auto);
        
        /// <summary> Unhooks from events and disposes of state/resources etc </summary>
        /// <param name="auto"> True if plugin is being auto unloaded (e.g. on server shutdown), false if manually. </param>
        public abstract void Unload(bool auto);
        
        /// <summary> Called when a player does /Help on the plugin. Typically tells the player what this plugin is about. </summary>
        /// <param name="p"> Player who is doing /Help. </param>
        public virtual void Help(Player p) {
            p.Message("No help is available for this plugin.");
        }
        
        /// <summary> Name of the plugin. </summary>
        public abstract string name { get; }
        /// <summary> Oldest version of GoldenSparks this plugin is compatible with. </summary>
        public virtual string GoldenSparks_Version { get; }
        /// <summary> Oldest version of MCGalaxy this plugin is compatible with. </summary>
        public virtual string MCGalaxy_Version { get; }
        /// <summary> Version of this plugin. </summary>
        public virtual int build { get { return 0; } }
        /// <summary> Message to display once this plugin is loaded. </summary>
        public virtual string welcome { get { return ""; } }
        /// <summary> The creator/author of this plugin. (Your name) </summary>
        public virtual string creator { get { return ""; } }
        /// <summary> Whether or not to auto load this plugin on server startup. </summary>
        public virtual bool LoadAtStartup { get { return true; } }
        
        
        internal static List<Plugin> core = new List<Plugin>();
        public static List<Plugin> all = new List<Plugin>();
        
        public static bool Load(Plugin p, bool auto) {
            string ver = p.GoldenSparks_Version;
            if (p.GoldenSparks_Version == null && p.MCGalaxy_Version != null)
            {
                ver = p.MCGalaxy_Version;
            }
            if (p.GoldenSparks_Version != null)
            {
                ver = p.GoldenSparks_Version;
            }
            string MCGalaxy_Ver = "1.9.3.9";

            if (!string.IsNullOrEmpty(p.MCGalaxy_Version) && new Version(p.MCGalaxy_Version) > new Version(MCGalaxy_Ver))
            {
                string msg = string.Format("Plugin '{0}' cannot be loaded on this version of {1}!", p.name, Server.SoftwareName);
                throw new InvalidOperationException(msg);
            }
            try
            {

                if (!string.IsNullOrEmpty(ver) && new Version(ver) > new Version(Server.Version)) {
                    Logger.Log(LogType.Warning, "Plugin ({0}) requires a more recent version of {1}!", p.name, Server.SoftwareName);
                    return false;
                }
                all.Add(p);
                
                if (p.LoadAtStartup || !auto) {
                    p.Load(auto);
                    Logger.Log(LogType.SystemActivity, "Plugin {0} loaded...build: {1}", p.name, p.build);
                } else {
                    Logger.Log(LogType.SystemActivity, "Plugin {0} was not loaded, you can load it with /pload", p.name);
                }
                
                if (!string.IsNullOrEmpty(p.welcome)) Logger.Log(LogType.SystemActivity, p.welcome);
                return true;
            } catch (Exception ex) {
                Logger.LogError("Error loading plugin " + p.name, ex);               
                if (!string.IsNullOrEmpty(p.creator)) Logger.Log(LogType.Warning, "You can go bug {0} about it.", p.creator);
                return false;
            }
        }

        public static bool Unload(Plugin p, bool auto) {
            bool success = true;
            try {
                p.Unload(auto);
                Logger.Log(LogType.SystemActivity, "Plugin {0} was unloaded.", p.name);
            } catch (Exception ex) {
                Logger.LogError("Error unloading plugin " + p.name, ex);
                success = false;
            }
            
            all.Remove(p);
            return success;
        }

        public static void UnloadAll() {
            for (int i = 0; i < all.Count; i++) {
                Unload(all[i], true); i--;
            }
        }

        public static void LoadAll() {
            LoadCorePlugin(new CorePlugin());
            LoadCorePlugin(new NotesPlugin());
            LoadCorePlugin(new DiscordPlugin());
            LoadCorePlugin(new IRCPlugin());
            LoadCorePlugin(new DiscordPlugin1());
            LoadCorePlugin(new IRCPlugin1());
            LoadCorePlugin(new DiscordPlugin2());
            LoadCorePlugin(new IRCPlugin2());
            LoadCorePlugin(new GlobalDiscordPlugin());
            LoadCorePlugin(new GlobalIRCPlugin());
            LoadCorePlugin(new ServerURLSender());
            LoadCorePlugin(new IPThrottler());
            IScripting.AutoloadPlugins();
        }
        
        static void LoadCorePlugin(Plugin plugin) {
            plugin.Load(true);
            all.Add(plugin);
            core.Add(plugin);
        }
    }
}

