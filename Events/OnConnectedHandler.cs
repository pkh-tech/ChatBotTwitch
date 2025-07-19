using System;
using TwitchLib.Client.Events;

namespace Bot.Events
{
    public static class OnConnectedHandler
    {
        public static void Handle(OnConnectedArgs e)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[CONNECTED]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("{0,-25}{1}", "bot username:", e.BotUsername);
            Console.ResetColor();
        }
    }
}
