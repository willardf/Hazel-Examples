using Hazel;
using System.Collections.Generic;
using System.Threading;

namespace HazelServer
{
    internal class GameData
    {
        private static int GameCounter = 0;

        public readonly int Id;

        private List<Player> PlayerList = new List<Player>();

        public GameData()
        {
            this.Id = Interlocked.Increment(ref GameCounter);
        }

        // Let's tell the existing players about the new player
        // And tell the new player about all the existing players
        public void AddPlayer(Player newPlayer)
        {
            var msg = MessageWriter.Get(SendOption.Reliable);
            msg.StartMessage((byte)PlayerMessageTags.PlayerJoined);
            msg.WritePacked(newPlayer.Id);
            msg.EndMessage();
            this.Broadcast(msg);

            lock (this.PlayerList)
            {
                msg.Clear(SendOption.Reliable);
                msg.StartMessage((byte)PlayerMessageTags.PlayersInGame);
                foreach (var player in this.PlayerList)
                {
                    msg.WritePacked(player.Id);
                }
                msg.EndMessage();

                this.PlayerList.Add(newPlayer);
            }

            try
            {
                newPlayer.Connection.Send(msg);
            }
            catch { }
        }

        public void Broadcast(MessageWriter msg)
        {
            // It's possible to create this method entirely lock-free, but too tricky 
            // for this example! Even a ReaderWriterLockSlim would be an improvement.
            lock (this.PlayerList)
            {
                foreach (var player in this.PlayerList)
                {
                    try
                    {
                        player.Connection.Send(msg);
                    }
                    catch
                    {
                        // Maybe you want to disconnect the player if you can't send?
                    }
                }
            }
        }
    }
}