using System;
using TwitchLib.Client;
using TwitchLib.Client.Events;

namespace Bot.Events
{
    public static class OnJoinedChannelHandler
    {
        public static void Handle(TwitchClient client, OnJoinedChannelArgs e)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[JOINED CHANNEL]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("{0,-25}{1}", "channel:", e.Channel);
            Console.ResetColor();

            client.SendMessage(e.Channel, "Hello everyone, I, SkunkLLM, have ARRIVED, prepare for CHAOS!.... to a certain degree");
        }
    }
}
