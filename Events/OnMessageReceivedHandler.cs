using Microsoft.Data.Sqlite;
using System;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TwitchLib.Client;
using TwitchLib.Client.Enums;
using TwitchLib.Client.Enums.Internal;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Client.Extensions;


namespace Bot.Events
{
    public static class OnMessageReceivedHandler
    {
        public static async Task Handle(SqliteConnection db, TwitchClient client, OnMessageReceivedArgs e)
        {
            var message = e.ChatMessage.Message.Trim();
            var user = e.ChatMessage.Username;
            var channel = e.ChatMessage.Channel;
            var lowerUser = user.ToLower();

            // Log AI opted-in messages first to avoid database lock issues
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

            string[] parts = message.Split(' ', 2);
            string trigger = parts[0];
            string arg = parts.Length > 1 ? parts[1] : "";

            // ==== HARDCODED COMMANDS WITH VALIDATION ====

            if (trigger.Equals("$stop", StringComparison.OrdinalIgnoreCase))
            {
                var userCheck = user.ToLower();
                if (userCheck != "skunkelmusen")
                {
                    client.SendMessage(channel, "Only admins can stop me.");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(arg))
                {
                    client.SendMessage(channel, "Usage: $stop (no arguments allowed)");
                    LogError(user, channel, message, "Too many arguments.");
                    return;
                }

                client.SendMessage(channel, "☘️ Shutting down...");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[STOP]");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("{0,-25}{1}", "triggered by:", user);
                Console.WriteLine("{0,-25}{1}", "channel:", channel);
                Console.ResetColor();
                Environment.Exit(0);
            }

            if (trigger.Equals("$aioptin", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(arg))
                {
                    client.SendMessage(channel, "Usage: $aioptin");
                    LogError(user, channel, message, "Too many arguments.");
                    return;
                }

                var cmd = db.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO AiOptedIn (Username) VALUES ($user)";
                cmd.Parameters.AddWithValue("$user", lowerUser);
                cmd.ExecuteNonQuery();
                client.SendMessage(channel, $"✅ @{user} is now opted in for logging.");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("[AI OPT-IN]");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("{0,-25}{1}", "user:", user);
                Console.WriteLine("{0,-25}{1}", "channel:", channel);
                Console.ResetColor();
                return;
            }

            if (trigger.Equals("$aioptout", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(arg))
                {
                    client.SendMessage(channel, "Usage: $aioptout");
                    LogError(user, channel, message, "Too many arguments.");
                    return;
                }

                var cmd = db.CreateCommand();
                cmd.CommandText = "DELETE FROM AiOptedIn WHERE Username = $user";
                cmd.Parameters.AddWithValue("$user", lowerUser);
                cmd.ExecuteNonQuery();
                client.SendMessage(channel, $"🚫 @{user} has opted out of logging.");
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("[AI OPT-OUT]");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("{0,-25}{1}", "user:", user);
                Console.WriteLine("{0,-25}{1}", "channel:", channel);
                Console.ResetColor();
                return;
            }

            if (trigger.Equals("$op", StringComparison.OrdinalIgnoreCase) || trigger.Equals("$deop", StringComparison.OrdinalIgnoreCase))
            {
                if (lowerUser != "skunkelmusen")
                {
                    client.SendMessage(channel, "Only skunkelmusen can manage ops.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(arg) || !arg.StartsWith("@"))
                {
                    client.SendMessage(channel, "Usage: $op @username or $deop @username");
                    LogError(user, channel, message, "Invalid or missing target.");
                    return;
                }

                var target = arg.TrimStart('@').ToLower();
                var cmd = db.CreateCommand();

                if (trigger.Equals("$op", StringComparison.OrdinalIgnoreCase))
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

            if (trigger.Equals("$addcommand", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsAdmin(db, user))
                {
                    client.SendMessage(channel, "Only admins can add commands.");
                    return;
                }

                var args = arg.Split(' ', 2);
                if (args.Length < 2)
                {
                    client.SendMessage(channel, "Usage: $addcommand $<trigger> <response>");
                    LogError(user, channel, message, "Missing trigger/response.");
                    return;
                }

                var newTrigger = args[0];
                var response = args[1];

                if (!newTrigger.StartsWith("$") || newTrigger.Contains(" "))
                {
                    client.SendMessage(channel, $"❌ Invalid trigger: {newTrigger}. Must start with '$' and contain no spaces.");
                    LogError(user, channel, message, "Invalid trigger format.");
                    return;
                }

                var insert = db.CreateCommand();
                insert.CommandText = "INSERT OR REPLACE INTO Commands (Trigger, Response, Channel) VALUES ($trigger, $response, $channel)";
                insert.Parameters.AddWithValue("$trigger", newTrigger);
                insert.Parameters.AddWithValue("$response", response);
                insert.Parameters.AddWithValue("$channel", channel);
                insert.ExecuteNonQuery();

                client.SendMessage(channel, $"📌 Added command: {newTrigger}");

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[ADD SUCCEEDED]");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("{0,-25}{1}", "user:", user);
                Console.WriteLine("{0,-25}{1}", "command:", newTrigger);
                Console.WriteLine("{0,-25}{1}", "response:", response);
                Console.WriteLine("{0,-25}{1}", "channel:", channel);
                Console.ResetColor();
                return;
            }

            if (trigger.Equals("$deletecommand", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsAdmin(db, user))
                {
                    client.SendMessage(channel, "Only admins can delete commands.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(arg))
                {
                    client.SendMessage(channel, "Usage: $deletecommand $<trigger>");
                    LogError(user, channel, message, "Missing command trigger.");
                    return;
                }

                var cmd = db.CreateCommand();
                cmd.CommandText = "DELETE FROM Commands WHERE Trigger = $trigger AND Channel = $channel";
                cmd.Parameters.AddWithValue("$trigger", arg.Trim());
                cmd.Parameters.AddWithValue("$channel", channel);
                int deleted = cmd.ExecuteNonQuery();

                client.SendMessage(channel, deleted > 0 ? $"🗑️ Deleted command: {arg}" : $"⚠️ Command {arg} not found.");
                return;
            }

            if (trigger.Equals("$commands", StringComparison.OrdinalIgnoreCase))
            {
                client.SendMessage(channel, "A full List of commands can be found at: https://list.ph-tech.dk/");
                return;
            }

            if (trigger.Equals("$skunkllm", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(arg))
                {
                    client.SendMessage(channel, "Usage: $ai <message>");
                    return;
                }

                string reply;
                try
                {
                    reply = await QueryLocalLLMAsync(arg);
                }
                catch (Exception ex)
                {
                    reply = $"[AI ERROR] {ex.Message}";
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[AI ERROR]");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine(ex.ToString());
                    Console.ResetColor();
                }

                client.SendMessage(channel, reply);
                return;
            }

            // === CUSTOM COMMAND EXECUTION ===
            var lookup = db.CreateCommand();
            lookup.CommandText = "SELECT Response FROM Commands WHERE Trigger = $trigger AND Channel = $channel";
            lookup.Parameters.AddWithValue("$trigger", trigger);
            lookup.Parameters.AddWithValue("$channel", channel);

            using (var reader = lookup.ExecuteReader())
            {
                if (reader.Read())
                {
                    var cooldownCmd = db.CreateCommand();
                    cooldownCmd.CommandText = @"
                        SELECT LastUsed FROM CommandCooldowns 
                        WHERE Trigger = $trigger AND Channel = $channel AND Username = $user";
                    cooldownCmd.Parameters.AddWithValue("$trigger", trigger);
                    cooldownCmd.Parameters.AddWithValue("$channel", channel);
                    cooldownCmd.Parameters.AddWithValue("$user", lowerUser);

                    var lastUsedObj = cooldownCmd.ExecuteScalar();
                    bool canExecute = true;

                    if (lastUsedObj != null)
                    {
                        var lastUsed = DateTime.Parse(lastUsedObj.ToString());
                        var secondsSince = (DateTime.UtcNow - lastUsed).TotalSeconds;
                        if (secondsSince < 10)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("[COMMAND RATE-LIMIT]");
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.WriteLine("{0,-25}{1}", "user:", user);
                            Console.WriteLine("{0,-25}{1}", "command:", trigger);
                            Console.WriteLine("{0,-25}{1}", "seconds remaining:", (10 - secondsSince).ToString("F2"));
                            Console.WriteLine("{0,-25}{1}", "channel:", channel);
                            Console.ResetColor();
                            canExecute = false;
                        }
                    }

                    if (!canExecute)
                        return;

                    var updateCooldown = db.CreateCommand();
                    updateCooldown.CommandText = @"
                        INSERT INTO CommandCooldowns (Trigger, Channel, Username, LastUsed)
                        VALUES ($trigger, $channel, $user, $now)
                        ON CONFLICT(Trigger, Channel, Username)
                        DO UPDATE SET LastUsed = $now";
                    updateCooldown.Parameters.AddWithValue("$trigger", trigger);
                    updateCooldown.Parameters.AddWithValue("$channel", channel);
                    updateCooldown.Parameters.AddWithValue("$user", lowerUser);
                    updateCooldown.Parameters.AddWithValue("$now", DateTime.UtcNow);
                    updateCooldown.ExecuteNonQuery();

                    var responseTemplate = reader.GetString(0);
                    var args = arg;

                    if (responseTemplate.Contains("$target") && (string.IsNullOrWhiteSpace(args) || !args.StartsWith("@")))
                    {
                        client.SendMessage(channel, "⚠️ This command requires a target (e.g. $wave @someone)");
                        LogError(user, channel, message, "Missing required $target.");
                        return;
                    }

                    var countCmd = db.CreateCommand();
                    countCmd.CommandText = @"
                        INSERT INTO CommandUsage (Trigger, Channel, Count)
                        VALUES ($trigger, $channel, 1)
                        ON CONFLICT(Trigger, Channel)
                        DO UPDATE SET Count = Count + 1;
                        SELECT Count FROM CommandUsage WHERE Trigger = $trigger AND Channel = $channel;";
                    countCmd.Parameters.AddWithValue("$trigger", trigger);
                    countCmd.Parameters.AddWithValue("$channel", channel);
                    var count = Convert.ToInt64(countCmd.ExecuteScalar());

                    var response = responseTemplate
                        .Replace("$user", $"@{user}")
                        .Replace("$target", args)
                        .Replace("$count", count.ToString());

                    response = ProcessTokens(response);

                    client.SendMessage(channel, response);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[TRIGGERED]");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("{0,-25}{1}", "user:", user);
                    Console.WriteLine("{0,-25}{1}", "command:", message);
                    Console.WriteLine("{0,-25}{1}", "response:", response);
                    Console.WriteLine("{0,-25}{1}", "channel:", channel);
                    Console.ResetColor();
                    return;
                }
            }

            // === UNKNOWN COMMAND LOGGING ===
            if (trigger.StartsWith("$") || trigger.StartsWith("!"))
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
            }
        }
        private static bool IsAdmin(SqliteConnection db, string username)
        {
            var lowerUser = username.ToLower();
            var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Admins WHERE LOWER(Username) = LOWER($user)";
            cmd.Parameters.AddWithValue("$user", lowerUser);
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }

        private static void LogError(string user, string channel, string command, string reason)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[COMMAND ERROR]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("{0,-25}{1}", "user:", user);
            Console.WriteLine("{0,-25}{1}", "channel:", channel);
            Console.WriteLine("{0,-25}{1}", "command:", command);
            Console.WriteLine("{0,-25}{1}", "reason:", reason);
            Console.ResetColor();
        }

        private static string ProcessTokens(string input)
        {
            var random = new Random();

            var randomMatches = System.Text.RegularExpressions.Regex.Matches(input, @"\$random\[(\d+)-(\d+)\]");
            foreach (System.Text.RegularExpressions.Match match in randomMatches)
            {
                int min = int.Parse(match.Groups[1].Value);
                int max = int.Parse(match.Groups[2].Value);
                int value = random.Next(min, max + 1);
                input = input.Replace(match.Value, value.ToString());
            }

            var selectionMatches = System.Text.RegularExpressions.Regex.Matches(input, @"\$selection\[([^\]]+)\]");
            foreach (System.Text.RegularExpressions.Match match in selectionMatches)
            {
                var options = match.Groups[1].Value.Split(',').Select(s => s.Trim()).ToArray();
                var choice = options[random.Next(options.Length)];
                input = input.Replace(match.Value, choice);
            }

            return input;
        }

        private static async Task<string> QueryLocalLLMAsync(string input)
        {
            var client = new HttpClient();
            var payload = new
            {
                model = "nousresearch/nous-hermes-2-mistral-7b-dpo",
                messages = new[]
                {
            new { role = "system", content = "You are a relaxed Twitch helper bot. 'Sminks' means to smoke a joint. If a user asks you to tell someone something and includes a name with @ (e.g., @skunkelmusen), reply as if speaking directly to that name. Only use the name provided—never default to @skunkelmusen. If a user asks you to ask bby something, start your message with '!bby <question>'. Keep replies concise, casual, and always under 450 characters." },
            new { role = "user", content = input }
        },
                temperature = 0.7
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("http://10.5.0.2:1234/v1/chat/completions", content);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            var reply = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            if (reply.Length > 450)
                reply = reply.Substring(0, 447) + "...";

            return reply.Trim();
        }

    }
}
