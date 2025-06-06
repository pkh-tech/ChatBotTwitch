using System;
using System.IO;
using Microsoft.Data.Sqlite;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

class Program
{
    static void Main(string[] args)
    {
        var username = "skunkllm";
        var accessToken = "oauth:ufv99wh0tt0a2pkaxgdtvz6bq05kl8";
        var channelName = "skunkelmusen";

        var credentials = new ConnectionCredentials(username, accessToken);
        var client = new TwitchClient();

        using (var db = new SqliteConnection("Data Source=commands.db"))
        {
            db.Open();

            var tableCmd = db.CreateCommand();
            tableCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Commands (
                    Trigger TEXT NOT NULL,
                    Response TEXT NOT NULL,
                    Channel TEXT NOT NULL,
                    PRIMARY KEY (Trigger, Channel)
                );
                CREATE TABLE IF NOT EXISTS UnknownCommands (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Trigger TEXT NOT NULL,
                    User TEXT NOT NULL,
                    Channel TEXT NOT NULL,
                    Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                CREATE TABLE IF NOT EXISTS Admins (
                    Username TEXT PRIMARY KEY
                );
                CREATE TABLE IF NOT EXISTS AiOptedIn (
                    Username TEXT PRIMARY KEY
                );
                CREATE TABLE IF NOT EXISTS CommandUsage (
                    Trigger TEXT NOT NULL,
                    Channel TEXT NOT NULL,
                    Count INTEGER DEFAULT 0,
                    PRIMARY KEY (Trigger, Channel)
                );";
            tableCmd.ExecuteNonQuery();

            var ensureAdminCmd = db.CreateCommand();
            ensureAdminCmd.CommandText = "INSERT OR IGNORE INTO Admins (Username) VALUES ('skunkelmusen')";
            ensureAdminCmd.ExecuteNonQuery();

            client.Initialize(credentials, channelName);

            client.OnConnected += (s, e) =>
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("[CONNECTED]");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("{0,-25}{1}", "bot username:", e.BotUsername);
                Console.ResetColor();
            };

            client.OnJoinedChannel += (s, e) =>
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("[JOINED CHANNEL]");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("{0,-25}{1}", "channel:", e.Channel);
                Console.ResetColor();

                client.SendMessage(e.Channel, "Hello everyone, I, SkunkLLM, have ARRIVED, prepare for CHAOS!.... to a certain degree");
            };

            client.OnMessageReceived += (s, e) =>
            {
                var message = e.ChatMessage.Message.Trim();
                var user = e.ChatMessage.Username;
                var channel = e.ChatMessage.Channel;
                var lowerUser = user.ToLower();

                bool IsAdmin()
                {
                    var cmd = db.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM Admins WHERE LOWER(Username) = LOWER($user)";
                    cmd.Parameters.AddWithValue("$user", lowerUser);
                    return Convert.ToInt64(cmd.ExecuteScalar()) > 0 || lowerUser == "skunkelmusen";
                }

                if (message.Equals("$stop", StringComparison.OrdinalIgnoreCase) && lowerUser == "skunkelmusen")
                {
                    client.SendMessage(channel, "☘️ Shutting down...");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[STOP]");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("{0,-25}{1}", "triggered by:", "skunkelmusen");
                    Console.WriteLine("{0,-25}{1}", "channel:", channel);
                    Console.ResetColor();
                    Environment.Exit(0);
                }

                if (message.Equals("$aioptin", StringComparison.OrdinalIgnoreCase))
                {
                    var cmd = db.CreateCommand();
                    cmd.CommandText = "INSERT OR IGNORE INTO AiOptedIn (Username) VALUES ($user)";
                    cmd.Parameters.AddWithValue("$user", lowerUser);
                    cmd.ExecuteNonQuery();
                    client.SendMessage(channel, $"✅ @{user} is now opted in for logging.");
                    return;
                }

                if (message.Equals("$aioptout", StringComparison.OrdinalIgnoreCase))
                {
                    var cmd = db.CreateCommand();
                    cmd.CommandText = "DELETE FROM AiOptedIn WHERE Username = $user";
                    cmd.Parameters.AddWithValue("$user", lowerUser);
                    cmd.ExecuteNonQuery();
                    client.SendMessage(channel, $"🚫 @{user} has opted out of logging.");
                    return;
                }

                if (message.StartsWith("$op ") || message.StartsWith("$deop "))
                {
                    if (lowerUser != "skunkelmusen")
                    {
                        client.SendMessage(channel, "Only skunkelmusen can manage ops.");
                        return;
                    }

                    var parts = message.Split(' ', 2);
                    if (parts.Length < 2 || !parts[1].StartsWith("@"))
                    {
                        client.SendMessage(channel, "Usage: $op @username or $deop @username");
                        return;
                    }

                    var target = parts[1].TrimStart('@').ToLower();
                    var cmd = db.CreateCommand();
                    if (message.StartsWith("$op "))
                    {
                        cmd.CommandText = "INSERT OR IGNORE INTO Admins (Username) VALUES ($user)";
                        client.SendMessage(channel, $"🔐 @{target} is now an admin.");
                    }
                    else
                    {
                        cmd.CommandText = "DELETE FROM Admins WHERE Username = $user";
                        client.SendMessage(channel, $"❌ @{target} is no longer an admin.");
                    }
                    cmd.Parameters.AddWithValue("$user", target);
                    cmd.ExecuteNonQuery();
                    return;
                }

                if (message.StartsWith("$addcommand "))
                {
                    if (!IsAdmin())
                    {
                        client.SendMessage(channel, "Only admins can add commands.");
                        return;
                    }

                    var split = message.Substring(12).Trim().Split(' ', 2);
                    if (split.Length < 2)
                    {
                        client.SendMessage(channel, "Usage: $addcommand $<trigger> <response>");
                        return;
                    }

                    var trigger = split[0];
                    var response = split[1];

                    // ➤ Trigger validation
                    if (!trigger.StartsWith("$") || trigger.Length < 2 || trigger.Contains(" "))
                    {
                        client.SendMessage(channel, $"❌ Invalid trigger: {trigger}. Must start with '$' and contain no spaces.");

                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("[ADD FAILED]");
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.WriteLine("{0,-25}{1}", "user:", user);
                        Console.WriteLine("{0,-25}{1}", "invalid trigger:", trigger);
                        Console.WriteLine("{0,-25}{1}", "channel:", channel);
                        Console.WriteLine("{0,-25}{1}", "reason:", "Trigger must start with '$' and contain no spaces.");
                        Console.ResetColor();

                        return;
                    }

                    var checkUnknown = db.CreateCommand();
                    checkUnknown.CommandText = "SELECT COUNT(*) FROM UnknownCommands WHERE Trigger = $trigger AND Channel = $channel";
                    checkUnknown.Parameters.AddWithValue("$trigger", trigger);
                    checkUnknown.Parameters.AddWithValue("$channel", channel);
                    bool wasUnknown = Convert.ToInt64(checkUnknown.ExecuteScalar()) > 0;

                    var insert = db.CreateCommand();
                    insert.CommandText = "INSERT OR REPLACE INTO Commands (Trigger, Response, Channel) VALUES ($trigger, $response, $channel)";
                    insert.Parameters.AddWithValue("$trigger", trigger);
                    insert.Parameters.AddWithValue("$response", response);
                    insert.Parameters.AddWithValue("$channel", channel);
                    insert.ExecuteNonQuery();

                    var cleanup = db.CreateCommand();
                    cleanup.CommandText = "DELETE FROM UnknownCommands WHERE Trigger = $trigger AND Channel = $channel";
                    cleanup.Parameters.AddWithValue("$trigger", trigger);
                    cleanup.Parameters.AddWithValue("$channel", channel);
                    cleanup.ExecuteNonQuery();

                    client.SendMessage(channel, $"📌 Added command: {trigger}");

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[ADD SUCCEEDED]");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("{0,-25}{1}", "user:", user);
                    Console.WriteLine("{0,-25}{1}", "command:", trigger);
                    Console.WriteLine("{0,-25}{1}", "response:", response);
                    Console.WriteLine("{0,-25}{1}", "channel:", channel);
                    Console.ResetColor();

                    if (wasUnknown)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine("[PROMOTED FROM UNKNOWN]");
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.WriteLine("{0,-25}{1}", "trigger:", trigger);
                        Console.WriteLine("{0,-25}{1}", "channel:", channel);
                        Console.ResetColor();
                    }

                    return;
                }


                if (message.StartsWith("$deletecommand "))
                {
                    if (!IsAdmin())
                    {
                        client.SendMessage(channel, "Only admins can delete commands.");
                        return;
                    }

                    var trigger = message.Substring(15).Trim();
                    var cmd = db.CreateCommand();
                    cmd.CommandText = "DELETE FROM Commands WHERE Trigger = $trigger AND Channel = $channel";
                    cmd.Parameters.AddWithValue("$trigger", trigger);
                    cmd.Parameters.AddWithValue("$channel", channel);
                    int deleted = cmd.ExecuteNonQuery();

                    client.SendMessage(channel, deleted > 0 ? $"🗑️ Deleted command: {trigger}" : $"⚠️ Command {trigger} not found.");
                    return;
                }

                var parts2 = message.Split(' ', 2);
                var triggerOnly = parts2[0];

                var lookup = db.CreateCommand();
                lookup.CommandText = "SELECT Response FROM Commands WHERE Trigger = $trigger AND Channel = $channel";
                lookup.Parameters.AddWithValue("$trigger", triggerOnly);
                lookup.Parameters.AddWithValue("$channel", channel);

                using (var reader = lookup.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var responseTemplate = reader.GetString(0);

                        if (responseTemplate.Contains("$target") && (parts2.Length < 2 || !parts2[1].StartsWith("@")))
                        {
                            client.SendMessage(channel, "⚠️ This command requires a target (e.g. $wave @someone)");
                            return;
                        }

                        var target = parts2.Length > 1 && parts2[1].StartsWith("@") ? parts2[1] : "";

                        var countCmd = db.CreateCommand();
                        countCmd.CommandText = @"
                            INSERT INTO CommandUsage (Trigger, Channel, Count)
                            VALUES ($trigger, $channel, 1)
                            ON CONFLICT(Trigger, Channel)
                            DO UPDATE SET Count = Count + 1;
                            SELECT Count FROM CommandUsage WHERE Trigger = $trigger AND Channel = $channel;";
                        countCmd.Parameters.AddWithValue("$trigger", triggerOnly);
                        countCmd.Parameters.AddWithValue("$channel", channel);
                        var count = Convert.ToInt64(countCmd.ExecuteScalar());

                        var response = responseTemplate
                            .Replace("$user", $"@{user}")
                            .Replace("$target", target)
                            .Replace("$count", count.ToString());

                        client.SendMessage(channel, response);

                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.WriteLine("[TRIGGERED]");
                        Console.WriteLine("{0,-25}{1}", "user:", user);
                        Console.WriteLine("{0,-25}{1}", "command:", message);
                        Console.WriteLine("{0,-25}{1}", "response:", response);
                        Console.WriteLine("{0,-25}{1}", "channel:", channel);
                        Console.ResetColor();
                        return;
                    }
                }

                if (message.StartsWith("$") || message.StartsWith("!"))
                {
                    var insert = db.CreateCommand();
                    insert.CommandText = "INSERT INTO UnknownCommands (Trigger, User, Channel) VALUES ($trigger, $user, $channel)";
                    insert.Parameters.AddWithValue("$trigger", message);
                    insert.Parameters.AddWithValue("$user", user);
                    insert.Parameters.AddWithValue("$channel", channel);
                    insert.ExecuteNonQuery();

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[UNKNOWN COMMAND]");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("{0,-25}{1}", "user:", user);
                    Console.WriteLine("{0,-25}{1}", "input:", message);
                    Console.WriteLine("{0,-25}{1}", "channel:", channel);
                    Console.ResetColor();
                    return;
                }

                var logCheck = db.CreateCommand();
                logCheck.CommandText = "SELECT COUNT(*) FROM AiOptedIn WHERE Username = $user";
                logCheck.Parameters.AddWithValue("$user", lowerUser);
                if (Convert.ToInt64(logCheck.ExecuteScalar()) > 0)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine("[CHAT LOG]");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("{0,-25}{1}", "user:", user);
                    Console.WriteLine("{0,-25}{1}", "message:", message);
                    Console.WriteLine("{0,-25}{1}", "channel:", channel);
                    Console.ResetColor();
                }
            };

            client.Connect();
            Console.ReadLine();
        }
    }
}
