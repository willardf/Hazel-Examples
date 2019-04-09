using Hazel;
using Hazel.Udp;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;

namespace HazelServer
{
    internal enum PlayerMessageTags
    {
        CreateGame,
        JoinGame,
        PlayerJoined,
        PlayersInGame,

        // Possible other tags (out of scope)
        GameData, // - I'm including this one to show to a client might want consistent updates

        // List games - Pretty straightforward
        // Leave game - Be sure to clean up the game, migrate host, etc.
    }

    internal enum ErrorCodes
    {
        AlreadyInGame = -1,
        GameNotFound = -2
    }

    internal class ServerProgram
    {
        private const int ServerPort = 23456;
        private ConcurrentDictionary<int, GameData> AllGames = new ConcurrentDictionary<int, GameData>();

        private bool amService;

        private static void Main(string[] args)
        {
            bool amService = false;
            if (args.Length > 0) bool.TryParse(args[0], out amService);

            ServerProgram server = new ServerProgram(amService);
            server.Run();
        }

        public ServerProgram(bool amService)
        {
            this.amService = amService;
        }

        public void AddGame(GameData game)
        {
            // Usually do this with error checking, but simplified for example
            this.AllGames.TryAdd(game.Id, game);
        }

        public bool TryGetGame(int gameId, out GameData game)
        {
            return this.AllGames.TryGetValue(gameId, out game);
        }

        private void Run()
        {
            using (var udpServer = new UdpConnectionListener(new IPEndPoint(IPAddress.Any, ServerPort), IPMode.IPv4))
            {
                udpServer.NewConnection += HandleNewConnection;
                udpServer.Start();

                while (true)
                {
                    if (amService)
                    {
                        // When running as a service, the main thread really doesn't need to do anything
                        // But it can be really useful to poll for configuration changes or log periodic statistics
                        Thread.Sleep(60000);

                        // For example, if you suspect you have a MessageReader/Writer leak, try outputting these counters.
                        // If the NumberInUse and NumberCreated keep going up, you probably forgot to recycle. (Or maybe you have a deadlock!)
                        // If you try to recycle a pooled object twice, the pool will throw. (Which sucks, but tends to be very easy to debug.)
                        // If you pool something that didn't come from the pool, the pool will throw.
                        // I may make the exceptions only happen in debug builds someday since they do have perf cost. (Very, very small)
                        Console.WriteLine($"Readers: {MessageReader.ReaderPool.NumberInUse}/{MessageReader.ReaderPool.NumberCreated}/{MessageReader.ReaderPool.Size}");
                        Console.WriteLine($"Writers: {MessageWriter.WriterPool.NumberInUse}/{MessageWriter.WriterPool.NumberCreated}/{MessageWriter.WriterPool.Size}");
                    }
                    else
                    {
                        Console.WriteLine("Press any key to exit");
                        Console.ReadKey(true);
                        break;
                    }
                }
            }
        }

        // From here down, you must be thread-safe!
        private void HandleNewConnection(NewConnectionEventArgs obj)
        {
            try
            {
                if (obj.HandshakeData.Length <= 0)
                {
                    // If the handshake is invalid, let's disconnect them!
                    return;
                }

                // Make sure this client version is compatible with this server and/or other clients!
                var clientVersion = obj.HandshakeData.ReadInt32();
                
                var player = new Player(this, clientVersion);
                obj.Connection.DataReceived += player.HandleMessage;
                obj.Connection.Disconnected += HandleDisconnect;
            }
            finally
            {
                // Always recycle messages!
                obj.HandshakeData.Recycle();
            }
        }

        private void HandleDisconnect(object sender, DisconnectedEventArgs e)
        {
            // There's actually nothing to do in this simple case!
            // If HandleDisconnect is called, then dispose is also guaranteed to be called.
            // Feel free to log e.Reason, clean up anything associated with a player disconnecting, etc.
        }
    }
}
