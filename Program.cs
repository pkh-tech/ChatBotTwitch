using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

class Program
{
    static bool IsAdmin(string username)
    {
        if (username.ToLower() == "skunkelmusen") return true;

        using (var connection = new SqliteConnection("Data Source=commands.db"))
        {
            connection.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Admins WHERE LOWER(Username) = LOWER($user)";
            cmd.Parameters.AddWithValue("$user", username);
            var result = cmd.ExecuteScalar();
            long count = Convert.ToInt64(result);
            return count > 0;
        }
    }

    static void Main(string[] args)
    {
        // 💣 Midlertidig: slet databasen ved opstart (fjern denne linje senere!)
        /*if (File.Exists("commands.db"))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[DB FOUND]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("{0,-25}{1}", "existing file:", "commands.db");
            Console.WriteLine("{0,-25}{1}", "action:", "Deleting old database");
            Console.ResetColor();

            File.Delete("commands.db");

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[DB DELETED]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("{0,-25}{1}", "status:", "commands.db deleted");
            Console.ResetColor();
        }*/

        var username = "skunkllm";
        var accessToken = "oauth:ufv99wh0tt0a2pkaxgdtvz6bq05kl8";
        var channelName = "skunkelmusen";

        var credentials = new ConnectionCredentials(username, accessToken);
        var client = new TwitchClient();

        using (var connection = new SqliteConnection($"Data Source=commands.db"))
        {
            connection.Open();

            var tableCmd = connection.CreateCommand();
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
                );";
            tableCmd.ExecuteNonQuery();

            var ensureAdminCmd = connection.CreateCommand();
            ensureAdminCmd.CommandText = "INSERT OR IGNORE INTO Admins (Username) VALUES ('skunkelmusen')";
            ensureAdminCmd.ExecuteNonQuery();
        }

        client.Initialize(credentials, channelName);

        client.OnConnected += (sender, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[CONNECTED]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("{0,-25}{1}", "bot username:", e.BotUsername);
            Console.ResetColor();

            client.SendMessage(channelName, $"✅ Connected to channel {channelName}");
        };

        client.OnJoinedChannel += (sender, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[JOINED CHANNEL]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("{0,-25}{1}", "channel:", e.Channel);
            Console.ResetColor();

            client.SendMessage(e.Channel, "Hello everyone, I, SkunkLLM, have ARRIVED, prepare for CHAOS!.... to a certain degree");
        };

        client.OnMessageReceived += (sender, e) =>
        {
            var message = e.ChatMessage.Message.Trim();
            var user = e.ChatMessage.Username;
            var channel = e.ChatMessage.Channel;

            // $stop (hardcoded)
            if (message == "$stop" && user.ToLower() == "skunkelmusen")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FORCED SHUTDOWN]");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("{0,-25}{1}", "triggered by:", "skunkelmusen");
                Console.WriteLine("{0,-25}{1}", "channel:", channel);
                Console.ResetColor();

                client.SendMessage(channel, "💀 Bot shutting down...");
                Environment.Exit(0);
            }

            // $op @user
            if (message.StartsWith("$op "))
            {
                var parts = message.Split(' ', 2);
                if (parts.Length < 2 || !parts[1].StartsWith("@"))
                {
                    client.SendMessage(channel, "Usage: $op @username");
                    return;
                }

                if (!IsAdmin(user))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[ACCESS DENIED]");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("{0,-25}{1}", "user:", user);
                    Console.WriteLine("{0,-25}{1}", "attempted:", message);
                    Console.WriteLine("{0,-25}{1}", "reason:", "not an admin");
                    Console.ResetColor();
                    client.SendMessage(channel, "Only admins can use $op.");
                    return;
                }

                var target = parts[1].TrimStart('@');

                using (var connection = new SqliteConnection("Data Source=commands.db"))
                {
                    connection.Open();
                    var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = "INSERT OR IGNORE INTO Admins (Username) VALUES ($user)";
                    insertCmd.Parameters.AddWithValue("$user", target);
                    insertCmd.ExecuteNonQuery();
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[OP GRANTED]");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("{0,-25}{1}", "granted by:", user);
                Console.WriteLine("{0,-25}{1}", "new admin:", target);
                Console.ResetColor();

                client.SendMessage(channel, $"🔐 @{target} is now an admin.");
                return;
            }

            if (message.StartsWith("$addcommand "))
            {
                if (!IsAdmin(user))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[ACCESS DENIED]");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("{0,-25}{1}", "user:", user);
                    Console.WriteLine("{0,-25}{1}", "command:", "$addcommand");
                    Console.WriteLine("{0,-25}{1}", "reason:", "not an admin");
                    Console.ResetColor();
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

                using (var connection = new SqliteConnection("Data Source=commands.db"))
                {
                    connection.Open();

                    var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = @"
                        INSERT OR REPLACE INTO Commands (Trigger, Response, Channel)
                        VALUES ($trigger, $response, $channel)";
                    insertCmd.Parameters.AddWithValue("$trigger", trigger);
                    insertCmd.Parameters.AddWithValue("$response", response);
                    insertCmd.Parameters.AddWithValue("$channel", channel);
                    insertCmd.ExecuteNonQuery();

                    var cleanupCmd = connection.CreateCommand();
                    cleanupCmd.CommandText = "DELETE FROM UnknownCommands WHERE Trigger = $trigger AND Channel = $channel";
                    cleanupCmd.Parameters.AddWithValue("$trigger", trigger);
                    cleanupCmd.Parameters.AddWithValue("$channel", channel);
                    int removed = cleanupCmd.ExecuteNonQuery();

                    if (removed > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("[PROMOTED FROM UNKNOWN]");
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.WriteLine("{0,-25}{1}", "command:", trigger);
                        Console.WriteLine("{0,-25}{1}", "channel:", channel);
                        Console.WriteLine("{0,-25}{1}", "status:", "Now registered as functional");
                        Console.ResetColor();
                    }
                }

                client.SendMessage(channel, $"📌 Added command: {trigger}");

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[ADD SUCCEEDED]");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("{0,-25}{1}", "user:", user);
                Console.WriteLine("{0,-25}{1}", "command:", trigger);
                Console.WriteLine("{0,-25}{1}", "response:", response);
                Console.WriteLine("{0,-25}{1}", "channel:", channel);
                Console.ResetColor();
                return;
            }

            if (message.StartsWith("$deletecommand "))
            {
                if (!IsAdmin(user))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[ACCESS DENIED]");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("{0,-25}{1}", "user:", user);
                    Console.WriteLine("{0,-25}{1}", "command:", "$deletecommand");
                    Console.WriteLine("{0,-25}{1}", "reason:", "not an admin");
                    Console.ResetColor();
                    client.SendMessage(channel, "Only admins can delete commands.");
                    return;
                }

                var triggerToDelete = message.Substring(15).Trim();

                using (var connection = new SqliteConnection("Data Source=commands.db"))
                {
                    connection.Open();

                    var deleteCmd = connection.CreateCommand();
                    deleteCmd.CommandText = "DELETE FROM Commands WHERE Trigger = $trigger AND Channel = $channel";
                    deleteCmd.Parameters.AddWithValue("$trigger", triggerToDelete);
                    deleteCmd.Parameters.AddWithValue("$channel", channel);

                    int rowsAffected = deleteCmd.ExecuteNonQuery();

                    if (rowsAffected > 0)
                    {
                        client.SendMessage(channel, $"🗑️ Deleted command: {triggerToDelete}");

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("[DELETE SUCCEEDED]");
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.WriteLine("{0,-25}{1}", "user:", user);
                        Console.WriteLine("{0,-25}{1}", "command:", triggerToDelete);
                        Console.WriteLine("{0,-25}{1}", "channel:", channel);
                        Console.ResetColor();
                    }
                    else
                    {
                        client.SendMessage(channel, $"⚠️ Command {triggerToDelete} not found.");

                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("[DELETE FAILED]");
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.WriteLine("{0,-25}{1}", "user:", user);
                        Console.WriteLine("{0,-25}{1}", "attempted:", triggerToDelete);
                        Console.WriteLine("{0,-25}{1}", "channel:", channel);
                        Console.WriteLine("{0,-25}{1}", "reason:", "not found");
                        Console.ResetColor();
                    }
                }

                return;
            }

            using (var connection = new SqliteConnection("Data Source=commands.db"))
            {
                connection.Open();
                var lookupCmd = connection.CreateCommand();
                lookupCmd.CommandText = "SELECT Response FROM Commands WHERE Trigger = $trigger AND Channel = $channel";
                lookupCmd.Parameters.AddWithValue("$trigger", message);
                lookupCmd.Parameters.AddWithValue("$channel", channel);

                using (var reader = lookupCmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var response = reader.GetString(0);
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

                if (message.StartsWith("$"))
                {
                    var logCmd = connection.CreateCommand();
                    logCmd.CommandText = @"
                        INSERT INTO UnknownCommands (Trigger, User, Channel)
                        VALUES ($trigger, $user, $channel)";
                    logCmd.Parameters.AddWithValue("$trigger", message);
                    logCmd.Parameters.AddWithValue("$user", user);
                    logCmd.Parameters.AddWithValue("$channel", channel);
                    logCmd.ExecuteNonQuery();

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[UNKNOWN COMMAND]");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("{0,-25}{1}", "user:", user);
                    Console.WriteLine("{0,-25}{1}", "input:", message);
                    Console.WriteLine("{0,-25}{1}", "channel:", channel);
                    Console.ResetColor();
                }
            }
        };

        client.Connect();
        Console.ReadLine();
    }
}
