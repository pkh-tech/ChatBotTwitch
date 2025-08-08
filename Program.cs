using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using Bot.Events;

class Program
{
    static void Main(string[] args)
    {
        var username = "skunkllm";
        var accessToken = "oauth:ufv99wh0tt0a2pkaxgdtvz6bq05kl8";
        var channelName = "ChildOfAnAndroid";

        var credentials = new ConnectionCredentials(username, accessToken);
        var client = new TwitchClient();

        using (var db = new SqliteConnection("Data Source=commands.db"))
        {
            db.Open();

            // Setup tables
            var tableCmd = db.CreateCommand();
            tableCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Commands (
                    Trigger TEXT NOT NULL,
                    Response TEXT NOT NULL,
                    Channel TEXT NOT NULL,
                    PRIMARY KEY (Trigger, Channel)
                );
                CREATE TABLE IF NOT EXISTS CommandCooldowns (
                    Trigger TEXT NOT NULL,
                    Channel TEXT NOT NULL,
                    Username TEXT NOT NULL,
                    LastUsed DATETIME NOT NULL,
                    PRIMARY KEY (Trigger, Channel, Username)
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

            // Start embedded HTTP server
            StartHttpServer(db);

            // Initialize Twitch bot
            client.Initialize(credentials, channelName);
            client.OnConnected += (s, e) => OnConnectedHandler.Handle(e);
            client.OnJoinedChannel += (s, e) => OnJoinedChannelHandler.Handle(client, e);
            client.OnMessageReceived += (s, e) => OnMessageReceivedHandler.Handle(db, client, e);

            client.Connect();
            Console.ReadLine();
        }
    }

    static void StartHttpServer(SqliteConnection db)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("https://+:44443/");
        listener.Start();

        Task.Run(() =>
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[HTTP SERVER STARTED]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("{0,-25}{1}", "url:", "https://list.ph-tech.dk/");
            Console.ResetColor();

            while (true)
            {
                var context = listener.GetContext();

                var cmd = db.CreateCommand();
                cmd.CommandText = "SELECT Trigger, Response FROM Commands ORDER BY Trigger COLLATE NOCASE";

                var builder = new StringBuilder();
                builder.Append("<html><head><title>SkunkLLM Commands</title><style>");
                builder.Append("body { font-family: sans-serif; padding: 20px; }");
                builder.Append("code { background: #eee; padding: 2px 4px; }");
                builder.Append("pre { background: #f4f4f4; padding: 10px; border-left: 4px solid #ccc; }");
                builder.Append("</style></head><body>");
                builder.Append("<h1>Available Commands</h1>");

                builder.Append("<p>You can use the following tokens inside commands:</p><ul>");
                builder.Append("<li><code>$user</code> the sender's username</li>");
                builder.Append("<li><code>$target</code> the first argument after the command</li>");
                builder.Append("<li><code>$count</code> how many times the command has been used</li>");
                builder.Append("<li><code>$random[min-max]</code> generates a random number in the given range</li>");
                builder.Append("<li><code>$selection[item1,item2,...]</code> picks a random item from the list</li>");
                builder.Append("</ul>");

                builder.Append("<h2>Example Commands</h2><pre>");
                builder.Append("$addcommand $greet Hello $user, welcome to the stream!\n\n");
                builder.Append("$addcommand $hug $user gives a warm hug to $target (hugged $count times so far)\n\n");
                builder.Append("$addcommand $slap $user slaps $target around with a large trout\n\n");
                builder.Append("$addcommand $roll $user just smoked $random[1-666] joints\n\n");
                builder.Append("$addcommand $snack $user is craving $selection[pizza, sushi, burger, salad, donuts, ramen, chocolate, tacos, ice cream]");
                builder.Append("</pre>");

                builder.Append("<h2>Command List</h2><ul>");

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var trigger = reader.GetString(0);
                        var responseText = reader.GetString(1);

                        builder.Append("<li><b>").Append(WebUtility.HtmlEncode(trigger)).Append("</b>");
                        if (!string.IsNullOrWhiteSpace(responseText))
                        {
                            builder.Append("&nbsp;<code>").Append(WebUtility.HtmlEncode(responseText)).Append("</code>");
                        }
                        builder.Append("</li>");
                    }
                }

                builder.Append("</ul><p><em>Bot hosted by SkunkLLM</em></p></body></html>");
                byte[] buffer = Encoding.UTF8.GetBytes(builder.ToString());

                context.Response.ContentType = "text/html";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
        });
    }

}
