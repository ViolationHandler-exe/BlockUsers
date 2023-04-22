using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("BlockUsers", "CreepaTime", "1.0.0")]
    [Description("BlockUsers is a system and API managing blocked users lists")]
    internal class BlockUsers : CovalencePlugin
    {
        #region Configuration

        private Configuration config;

        public class Configuration
        {
            [JsonProperty("Blocked Users list cache time (0 to disable)")]
            public int CacheTime = 0;

            [JsonProperty("Maximum number of blocked users (0 to disable)")]
            public int MaxBlockedUsers = 30;

            [JsonProperty("Cooldown for block command in seconds (0 to disable)")]
            public int BlockDelay = 0;

            [JsonProperty("Use permission system")]
            public bool UsePermissions = false;

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                if (!config.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            LogWarning($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion Configuration

        #region Stored Data

        private readonly Dictionary<string, HashSet<string>> reverseData = new Dictionary<string, HashSet<string>>();
        private Dictionary<string, PlayerData> blockedData;

        private static readonly DateTime Epoch = new DateTime(1970, 1, 1);

        private class PlayerData
        {
            public string Name { get; set; } = string.Empty;
            public HashSet<string> BlockedUsers { get; set; } = new HashSet<string>();
            public Dictionary<string, int> Cached { get; set; } = new Dictionary<string, int>();
            public Dictionary<string, DateTime> LastCalled { get; set; } = new Dictionary<string, DateTime>();

            public bool IsCached(string playerId)
            {
                int time;
                if (!Cached.TryGetValue(playerId, out time))
                {
                    return false;
                }

                if (time >= (int)DateTime.UtcNow.Subtract(Epoch).TotalSeconds)
                {
                    return true;
                }

                Cached.Remove(playerId);
                return false;
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, blockedData);
        }

        #endregion Stored Data

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["AlreadyOnBlockedList"] = "{0} is already blocked.",
                ["CannotBlockSelf"] = "You cannot block yourself.",
				["CommandBlock"] = "block",
                ["BlockedUserAdded"] = "{0} is now blocked.",
                ["BlockedUserRemoved"] = "{0} was removed from your blocked list.",
                ["BlockedList"] = "Blocked Players {0}:\n{1}.",
                ["BlockedListFull"] = "Your blocked users list is full.",
                ["NoBlockedUsers"] = "You do not have any blocked users.",
                ["NotAllowed"] = "You are not allowed to use the '{0}' command.",
                ["NoPlayersFound"] = "No players found with name or ID '{0}'.",
                ["NotOnBlockedList"] = "{0} not found on your blocked list.",
                ["Delay"] = "Wait {0} more seconds before using the '/{1}' command.",
                ["PlayerNotFound"] = "Player '{0}' was not found",
                ["PlayersFound"] = "Multiple players were found, please specify: {0}.",
                ["PlayersOnly"] = "Command '{0}' can only be used by players.",
                ["UsageBlockedUsers"] = "Usage /{0} <add|remove|list> <player name or id> or /{0} list."
            }, this);
        }

        #endregion Localization

        #region Initialization

        private const string permUse = "blockusers.use";

        private void Init()
        {
            AddLocalizedCommand(nameof(CommandBlock));

            permission.RegisterPermission(permUse, this);

            try
            {
                blockedData = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, PlayerData>>(Name);
            }
            catch
            {
                blockedData = new Dictionary<string, PlayerData>();
            }
            foreach (KeyValuePair<string, PlayerData> data in blockedData)
            {
                foreach (string blockedId in data.Value.BlockedUsers)
                {
                    AddBlockedUserReverse(data.Key, blockedId);
                }
            }
        }

        #endregion Initialization

        #region Add/Remove Blocked Users

        private bool AddBlockedUser(string playerId, string blockedId)
        {
            if (!string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(blockedId))

            {
                PlayerData playerData = GetPlayerData(playerId);
                if (playerData.BlockedUsers.Count >= config.MaxBlockedUsers || !playerData.BlockedUsers.Add(blockedId))
                {
                    return false;
                }

                AddBlockedUserReverse(playerId, blockedId);
                SaveData();

                Interface.Oxide.CallHook("OnBlockedUserAdded", playerId, blockedId);
                return true;
            }

            return false;
        }

        private bool AddBlockedUser(ulong playerId, ulong blockedId)
        {
            return AddBlockedUser(playerId.ToString(), blockedId.ToString());
        }


        private void AddBlockedUserReverse(string playerId, string blockedId)
        {
            HashSet<string> blockedUsers;
            if (!reverseData.TryGetValue(blockedId, out blockedUsers))
            {
                reverseData[blockedId] = blockedUsers = new HashSet<string>();
            }

            blockedUsers.Add(playerId);
        }


        private bool RemoveBlockedUser(string playerId, string blockedId)
        {
            if (!string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(blockedId))
            {
                PlayerData playerData = GetPlayerData(playerId);
                if (!playerData.BlockedUsers.Remove(blockedId))
                {
                    return false;
                }

                HashSet<string> blockedUsers;
                if (reverseData.TryGetValue(blockedId, out blockedUsers))
                {
                    blockedUsers.Remove(playerId);
                }

                if (config.CacheTime > 0)
                {
                    playerData.Cached[blockedId] = (int)DateTime.UtcNow.Subtract(Epoch).TotalSeconds + config.CacheTime;
                }

                SaveData();

                Interface.Oxide.CallHook("OnBlockedUserRemoved", playerId, blockedId);
                return true;
            }

            return false;
        }

        private bool RemoveBlockedUser(ulong playerId, ulong blockedId)
        {
            return RemoveBlockedUser(playerId.ToString(), blockedId.ToString());
        }

        #endregion Add/Remove Blocked Users

        #region Blocked Users Checks

        private bool HasBlockedUser(string playerId, string blockedUserId)
        {
            if (!string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(blockedUserId))
            {
                return GetPlayerData(playerId).BlockedUsers.Contains(blockedUserId);
            }

            return false;
        }

        private bool HasBlockedUser(ulong playerId, ulong blockedUserId)
        {
            return HasBlockedUser(playerId.ToString(), blockedUserId.ToString());
        }

        private bool HadBlockedUser(string playerId, string blockedUserId)
        {
            if (!string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(blockedUserId))
            {
                PlayerData playerData = GetPlayerData(playerId);
                return playerData.BlockedUsers.Contains(blockedUserId) || playerData.IsCached(blockedUserId);
            }

            return false;
        }

        private bool HadBlockedUser(ulong playerId, ulong blockedUserId)
        {
            return HadBlockedUser(playerId.ToString(), blockedUserId.ToString());
        }

        private bool AreBlockedUsers(string playerId, string blockedUserId)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(blockedUserId))
            {
                return false;
            }

            return GetPlayerData(playerId).BlockedUsers.Contains(blockedUserId) && GetPlayerData(blockedUserId).BlockedUsers.Contains(playerId);
        }

        private bool AreBlockedUsers(ulong playerId, ulong blockedUserId)
        {
            return AreBlockedUsers(playerId.ToString(), blockedUserId.ToString());
        }

        private bool WereBlockedUsers(string playerId, string blockedUserId)
        {
            if (!string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(blockedUserId))
            {
                PlayerData playerData = GetPlayerData(playerId);
                PlayerData BlockedUserData = GetPlayerData(blockedUserId);
                return (playerData.BlockedUsers.Contains(blockedUserId) || playerData.IsCached(blockedUserId)) && (BlockedUserData.BlockedUsers.Contains(playerId) || BlockedUserData.IsCached(playerId));
            }

            return false;
        }

        private bool WereBlockedUsers(ulong playerId, ulong blockedUserId)
        {
            return WereBlockedUsers(playerId.ToString(), blockedUserId.ToString());
        }

        private bool IsBlockedUser(string playerId, string blockedUserId)
        {
            if (!string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(blockedUserId))
            {
                return GetPlayerData(blockedUserId).BlockedUsers.Contains(playerId);
            }

            return false;
        }

        private bool IsBlockedUser(ulong playerId, ulong blockedUserId)
        {
            return IsBlockedUser(playerId.ToString(), blockedUserId.ToString());
        }

        private bool WasBlockedUser(string playerId, string blockedUserId)
        {
            if (!string.IsNullOrEmpty(playerId) && !string.IsNullOrEmpty(blockedUserId))
            {
                PlayerData playerData = GetPlayerData(blockedUserId);
                return playerData.BlockedUsers.Contains(playerId) || playerData.IsCached(playerId);
            }

            return false;
        }

        private bool WasBlockedUser(ulong playerId, ulong blockedUserId)
        {
            return WasBlockedUser(playerId.ToString(), blockedUserId.ToString());
        }

        private int GetMaxBlockedUsers()
        {
            return config.MaxBlockedUsers;
        }

        #endregion Blocked Users Checks

        #region Blocked Users Lists

        private string[] GetBlockedUsers(string playerId)
        {
            return GetPlayerData(playerId).BlockedUsers.ToArray();
        }

        private ulong[] GetBlockedUsers(ulong playerId)
        {
            return GetPlayerData(playerId.ToString()).BlockedUsers.Select(ulong.Parse).ToArray();
        }

        private string[] GetBlockedUsersList(string playerId)
        {
            PlayerData playerData = GetPlayerData(playerId);
            List<string> players = new List<string>();

            foreach (string blockedUserId in playerData.BlockedUsers)
            {
                players.Add(GetPlayerData(blockedUserId).Name);
            }

            return players.ToArray();
        }

        private string[] GetBlockedUsersList(ulong playerId)
        {
            return GetBlockedUsersList(playerId.ToString());
        }

        private string[] IsBlockedUsersOf(string playerId)
        {
            HashSet<string> blockedUsers;
            return reverseData.TryGetValue(playerId, out blockedUsers) ? blockedUsers.ToArray() : new string[0];
        }

        private ulong[] IsBlockedUsersOf(ulong playerId)
        {
            return IsBlockedUsersOf(playerId.ToString()).Select(ulong.Parse).ToArray();
        }

        #endregion Blocked Users Lists

        #region Commands

        private void CommandBlock(IPlayer player, string command, string[] args)
        {
            if (player.IsServer)
            {
                Message(player, "PlayersOnly", command);
                return;
            }

            if (config.UsePermissions && player.HasPermission("block.use"))
            {
                Message(player, "NotAllowed", command);
                return;
            }
//            if(config.BlockDelay > 0) {
//                var timestamp = DateTime.UtcNow;
//                var time = new TimeSpan();
//
//                PlayerData playerData = GetPlayerData(player.Id);
//                try {
//                    time = timestamp - playerData.LastCalled[player.Id];
//                } catch {
//                    playerData.LastCalled[player.Id] = timestamp;
//                }
//                if (time.Seconds < config.BlockDelay) {
//                    Message(player, "Delay", $"{time.Seconds}.{time.Milliseconds}", command);
//                    return;
//                }
//                playerData.LastCalled[player.Id] = timestamp;
//            }


            if (args.Length <= 0 || args.Length == 1 && !args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                Message(player, "UsageBlockedUsers", command);
                return;
            }

            if(config.BlockDelay > 0) {
                var timestamp = DateTime.UtcNow;
                TimeSpan time;
                DateTime span;
                PlayerData playerData = GetPlayerData(player.Id);
                if (playerData.LastCalled.TryGetValue(player.Id, out span)){
                    time = timestamp - span;
                    if (config.BlockDelay > time.Seconds) {
                        Message(player, "Delay", $"{time.Seconds}.{time.Milliseconds}", command);
                        return;
                    }
                }
                playerData.LastCalled[player.Id] = timestamp;
            }

            switch (args[0].ToLower())
            {
                    case "list":
                        string[] blockedList = GetBlockedUsersList(player.Id);
                        if (blockedList.Length > 0)
                        {
                            Message(player, "BlockedList", $"{blockedList.Length}/{config.MaxBlockedUsers}", string.Join(", ", blockedList));
                        }
                        else
                        {
                            Message(player, "NoBlockedUsers");
                        }

                        return;

                    case "+":
                    case "add":
                        IPlayer target = FindPlayer(args[1], player);
                        if (target == null)
                        {
                            return;
                        }

                        if (player.Id == target.Id)
                        {
                            Message(player, "CannotBlockSelf");
                            return;
                        }

                        PlayerData playerData = GetPlayerData(player.Id);
                        if (playerData.BlockedUsers.Count >= config.MaxBlockedUsers)
                        {
                            Message(player, "BlockedListFull");
                            return;
                        }

                        if (playerData.BlockedUsers.Contains(target.Id))
                        {
                            Message(player, "AlreadyOnBlockedList", target.Name);
                            return;
                        }

                        AddBlockedUser(player.Id, target.Id);
                        Message(player, "BlockedUserAdded", target.Name);
                        return;

                    case "-":
                    case "remove":
                        string blockedUser = FindBlockedUser(args[1]);
                        if (string.IsNullOrEmpty(blockedUser))
                        {
                            Message(player, "NotOnBlockedList", args[1]);
                            return;
                        }

                        bool removed = RemoveBlockedUser(player.Id, blockedUser.ToString());
                        Message(player, removed ? "BlockedUserRemoved" : "NotOnBlockedList", args[1]);
                        return;
                }
        }

        private void SendHelpText(object obj)
        {
            IPlayer player = players.FindPlayerByObj(obj);
            if (player != null)
            {
                Message(player, "HelpText");
            }
        }

        #endregion Commands

        #region Helpers

        private string FindBlockedUser(string nameOrId)
        {
            if (!string.IsNullOrEmpty(nameOrId))
            {
                foreach (KeyValuePair<string, PlayerData> playerData in blockedData)
                {
                    if (playerData.Key.Equals(nameOrId) || playerData.Value.Name.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return playerData.Key;
                    }
                }
            }

            return string.Empty;
        }

        private PlayerData GetPlayerData(string playerId)
        {
            PlayerData playerData;
            if (!blockedData.TryGetValue(playerId, out playerData))
            {
                blockedData[playerId] = playerData = new PlayerData();
            }

            IPlayer player = players.FindPlayerById(playerId);
            if (player != null)
            {
                playerData.Name = player.Name;
            }

            return playerData;
        }

        private IPlayer FindPlayer(string playerNameOrId, IPlayer player)
        {
            IPlayer[] foundPlayers = players.FindPlayers(playerNameOrId).ToArray();
            if (foundPlayers.Length > 1)
            {
                Message(player, "PlayersFound", string.Join(", ", foundPlayers.Select(p => p.Name).Take(10).ToArray()).Truncate(60));
                return null;
            }

            IPlayer target = foundPlayers.Length == 1 ? foundPlayers[0] : null;
            if (target == null)
            {
                Message(player, "NoPlayersFound", playerNameOrId);
                return null;
            }

            return target;
        }

        private void AddLocalizedCommand(string command)
        {
            foreach (string language in lang.GetLanguages(this))
            {
                Dictionary<string, string> messages = lang.GetMessages(language, this);
                foreach (KeyValuePair<string, string> message in messages)
                {
                    if (message.Key.Equals(command))
                    {
                        if (!string.IsNullOrEmpty(message.Value))
                        {
                            AddCovalenceCommand(message.Value, command);
                        }
                    }
                }
            }
        }

        private string GetLang(string langKey, string playerId = null, params object[] args)
        {
            return string.Format(lang.GetMessage(langKey, this, playerId), args);
        }

        private void Message(IPlayer player, string textOrLang, params object[] args)
        {
            if (player.IsConnected)
            {
                string message = GetLang(textOrLang, player.Id, args);
                player.Reply(message != textOrLang ? message : textOrLang);
            }
        }

        #endregion Helpers
    }
}
