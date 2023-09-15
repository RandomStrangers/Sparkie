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
using System.Text;
using System.Text.RegularExpressions;
using SuperNova.Modules.Relay2;
using Sharkbite.Irc;

namespace SuperNova.Modules.Relay2.GlobalIRC 
{
    public enum GlobalIRCControllerVerify { None, HalfOp, OpChannel };
    
    /// <summary> Manages a connection to a GlobalIRC server, and handles associated events. </summary>
    public sealed class GlobalIRCBot: RelayBot2 
    {
        internal Connection conn;
        string nick;
        GlobalIRCNickList nicks;
        bool ready;
        
        public override string RelayName { get { return "GlobalIRC"; } }
        public override bool Enabled  { get { return Server.Config.UseGlobalIRC; } }
        public override string UserID { get { return conn == null ? null : conn.Nick; } }
        
        public override void LoadControllers() {
            Controllers = PlayerList.Load("ranks/GlobalIRC_Controllers.txt");
        }
        
        public GlobalIRCBot() {
            nicks     = new GlobalIRCNickList();
            nicks.bot = this;
        }
        
        
        static char[] newline = { '\n' };
        public override void DoSendMessage(string channel, string message) {
            if (!ready) return;
            
            // IRC messages can't have \r or \n in them
            //  https://stackoverflow.com/questions/13898584/insert-line-breaks-into-an-irc-message
            if (message.IndexOf('\n') == -1) {
                conn.SendMessage(channel, message);
                return;
            }
            
            string[] parts = message.Split(newline, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts) 
            {
                conn.SendMessage(channel, part.Replace("\r", ""));
            }
        }
        
        public void Raw(string message) {
            if (!Enabled || !Connected) return;
            conn.SendRaw(message);
        }

        void Join(string channel) {
            if (String.IsNullOrEmpty(channel)) return;
            conn.SendJoin(channel);
        }


        public override bool CanReconnect { get { return canReconnect; } }

        public override void DoConnect() {
            ready = false;
            nick  = Server.Config.GlobalIRCNick.Replace(" ", "");
            
            if (conn == null) conn = new Connection(new UTF8Encoding(false));
            conn.Hostname = Server.Config.GlobalIRCServer;
            conn.Port     = Server.Config.GlobalIRCPort;
            conn.UseSSL   = Server.Config.GlobalIRCSSL;
            
            conn.Nick     = nick;
            conn.UserName = nick;
            conn.RealName = Server.SoftwareNameVersioned;
            HookIRCEvents();
            
            bool usePass = Server.Config.GlobalIRCIdentify && Server.Config.GlobalIRCPassword.Length > 0;
            conn.ServerPassword = usePass ? Server.Config.GlobalIRCPassword : "*";
            conn.Connect();
        }

        public override void DoReadLoop() {
            conn.ReceiveIRCMessages();
        }

        public override void DoDisconnect(string reason) {
            nicks.Clear();
            try {
                conn.Disconnect(reason);
            } catch {
                // no point logging disconnect failures
            }
            UnhookIRCEvents();
        }

        public override void UpdateConfig() {
            Channels     = Server.Config.GlobalIRCChannels.SplitComma();
            OpChannels   = Server.Config.GlobalIRCOpChannels.SplitComma();
            IgnoredUsers = Server.Config.GlobalIRCIgnored.SplitComma();
            LoadBannedCommands();
        }
        
        
        static readonly string[] ircColors = new string[] {
            "\u000300", "\u000301", "\u000302", "\u000303", "\u000304", "\u000305",
            "\u000306", "\u000307", "\u000308", "\u000309", "\u000310", "\u000311",
            "\u000312", "\u000313", "\u000314", "\u000315",
        };
        static readonly string[] ircSingle = new string[] {
            "\u00030", "\u00031", "\u00032", "\u00033", "\u00034", "\u00035",
            "\u00036", "\u00037", "\u00038", "\u00039",
        };
        static readonly string[] ircReplacements = new string[] {
            "&f", "&0", "&1", "&2", "&c", "&4", "&5", "&6",
            "&e", "&a", "&3", "&b", "&9", "&d", "&8", "&7",
        };
        static readonly Regex ircTwoColorCode = new Regex("(\x03\\d{1,2}),\\d{1,2}");

        public override string ParseMessage(string input) {
            // get rid of background color component of some IRC color codes.
            input = ircTwoColorCode.Replace(input, "$1");
            StringBuilder sb = new StringBuilder(input);
            
            for (int i = 0; i < ircColors.Length; i++) {
                sb.Replace(ircColors[i], ircReplacements[i]);
            }
            for (int i = 0; i < ircSingle.Length; i++) {
                sb.Replace(ircSingle[i], ircReplacements[i]);
            }
            SimplifyCharacters(sb);
            
            // remove misc formatting chars
            sb.Replace(BOLD,      "");
            sb.Replace(ITALIC,    "");
            sb.Replace(UNDERLINE, "");
            
            sb.Replace("\x03", "&f"); // color reset
            sb.Replace("\x0f", "&f"); // reset
            return sb.ToString();
        }

        public override string ConvertMessage(string message) {
            if (String.IsNullOrEmpty(message.Trim())) message = ".";
            const string resetSignal = "\x03\x0F";
            
            message = base.ConvertMessage(message);
            message = message.Replace("%S", "&f"); // TODO remove
            message = message.Replace("&S", "&f");
            message = message.Replace("&f", resetSignal);
            message = ToIRCColors(message);
            return message;
        }

        static string ToIRCColors(string input) {
            input = Colors.Escape(input);
            input = LineWrapper.CleanupColors(input, true, false);
            
            StringBuilder sb = new StringBuilder(input);
            for (int i = 0; i < ircColors.Length; i++) {
                sb.Replace(ircReplacements[i], ircColors[i]);
            }
            return sb.ToString();
        }


        public override bool CheckController(string userID, ref string error) {
            bool foundAtAll = false;
            foreach (string chan in Channels) {
                if (nicks.VerifyNick(chan, userID, ref error, ref foundAtAll)) return true;
            }
            foreach (string chan in OpChannels) {
                if (nicks.VerifyNick(chan, userID, ref error, ref foundAtAll)) return true;
            }
            
            if (!foundAtAll) {
                error = "You are not on the bot's list of users for some reason, please leave and rejoin.";
            }
            return false;
        }

        void HookIRCEvents() {
            // Regster events for incoming
            conn.Listener.OnNick += OnNick;
            conn.Listener.OnRegistered += OnRegistered;
            conn.Listener.OnAction += OnAction;
            conn.Listener.OnPublic += OnPublic;
            conn.Listener.OnPrivate += OnPrivate;
            conn.Listener.OnError += OnError;
            conn.Listener.OnQuit += OnQuit;
            conn.Listener.OnJoin += OnJoin;
            conn.Listener.OnPart += OnPart;
            conn.Listener.OnChannelModeChange += OnChannelModeChange;
            conn.Listener.OnNames += OnNames;
            conn.Listener.OnKick += OnKick;
            conn.Listener.OnKill += OnKill;
            conn.Listener.OnPrivateNotice += OnPrivateNotice;
        }

        void UnhookIRCEvents() {
            // Regster events for incoming
            conn.Listener.OnNick -= OnNick;
            conn.Listener.OnRegistered -= OnRegistered;
            conn.Listener.OnAction -= OnAction;
            conn.Listener.OnPublic -= OnPublic;
            conn.Listener.OnPrivate -= OnPrivate;
            conn.Listener.OnError -= OnError;
            conn.Listener.OnQuit -= OnQuit;
            conn.Listener.OnJoin -= OnJoin;
            conn.Listener.OnPart -= OnPart;
            conn.Listener.OnChannelModeChange -= OnChannelModeChange;
            conn.Listener.OnNames -= OnNames;
            conn.Listener.OnKick -= OnKick;
            conn.Listener.OnKill -= OnKill;
            conn.Listener.OnPrivateNotice -= OnPrivateNotice;
        }

        
        void OnAction(UserInfo user, string channel, string description) {
            MessageInGame(user.Nick, string.Format("&I(GlobalIRC) * {0} {1}", user.Nick, description));
        }
        
        void OnJoin(UserInfo user, string channel) {
            conn.SendNames(channel);
            AnnounceJoinLeave(user.Nick, "joined", channel);
        }
        
        void OnPart(UserInfo user, string channel, string reason) {
            nicks.OnLeftChannel(user, channel);
            if (user.Nick == nick) return;
            AnnounceJoinLeave(user.Nick, "left", channel);
        }

        void AnnounceJoinLeave(string nick, string verb, string channel) {
            Logger.Log(LogType.RelayActivity, "{0} {1} channel {2}", nick, verb, channel);
            string which = OpChannels.CaselessContains(channel) ? " operator" : "";
            MessageInGame(nick, string.Format("&I(GlobalIRC) {0} {1} the{2} channel", nick, verb, which));
        }

        void OnQuit(UserInfo user, string reason) {
            // Old bot was disconnected, try to reclaim it
            if (user.Nick == nick) conn.SendNick(nick);
            nicks.OnLeft(user);
            
            if (user.Nick == nick) return;
            Logger.Log(LogType.RelayActivity, user.Nick + " left GlobalIRC");
            MessageInGame(user.Nick, "&I(GlobalIRC) " + user.Nick + " left");
        }

        void OnError(ReplyCode code, string message) {
            Logger.Log(LogType.RelayActivity, "GlobalIRC Error: " + message);
        }

        void OnPrivate(UserInfo user, string message) {
            RelayUser2 rUser = new RelayUser2();
            rUser.ID        = user.Nick;
            rUser.Nick      = user.Nick;
            HandleDirectMessage(rUser, user.Nick, message);
        }        

        void OnPublic(UserInfo user, string channel, string message) {
            RelayUser2 rUser = new RelayUser2();
            rUser.ID        = user.Nick;
            rUser.Nick      = user.Nick;
            HandleChannelMessage(rUser, channel, message);
        }
        
        void OnRegistered() {
            OnReady();
            Authenticate();
            JoinChannels();
        }
        
        void JoinChannels() {
            Logger.Log(LogType.RelayActivity, "Joining GlobalIRC channels...");
            foreach (string chan in Channels)   { Join(chan); }
            foreach (string chan in OpChannels) { Join(chan); }
            ready = true;
        }
        
        void OnPrivateNotice(UserInfo user, string notice) {
            if (!notice.CaselessStarts("You are now identified")) return;
            JoinChannels();
        }
        
        void Authenticate() {
            string nickServ = Server.Config.GlobalIRCNickServName;
            if (nickServ.Length == 0) return;
            
            if (Server.Config.GlobalIRCIdentify && Server.Config.GlobalIRCPassword.Length > 0) {
                Logger.Log(LogType.RelayActivity, "Identifying with " + nickServ);
                conn.SendMessage(nickServ, "IDENTIFY " + Server.Config.GlobalIRCPassword);
            }
        }

        void OnNick(UserInfo user, string newNick) {
            // We have successfully reclaimed our nick, so try to sign in again.
            if (newNick == nick) Authenticate();
            if (newNick.Trim().Length == 0) return;
            
            nicks.OnChangedNick(user, newNick);
            MessageInGame(user.Nick, "&I(GlobalIRC) " + user.Nick + " &Sis now known as &I" + newNick);
        }
        
        void OnNames(string channel, string[] _nicks, bool last) {
            nicks.UpdateFor(channel, _nicks);
        }
        
        void OnChannelModeChange(UserInfo who, string channel) {
            conn.SendNames(channel);
        }
        
        void OnKick(UserInfo user, string channel, string kickee, string reason) {
            nicks.OnLeftChannel(user, channel);
            
            if (reason.Length > 0) reason = " (" + reason + ")";
            Logger.Log(LogType.RelayActivity, "{0} kicked {1} from GlobalIRC{2}", user.Nick, kickee, reason);
            MessageInGame(user.Nick, "&I(GlobalIRC) " + user.Nick + " kicked " + kickee + reason);
        }
        
        void OnKill(UserInfo user, string nick, string reason) {
            nicks.OnLeft(user);
        }
        
        
        public const string BOLD      = "\x02";
        public const string ITALIC    = "\x1D";
        public const string UNDERLINE = "\x1F";
    }
}

