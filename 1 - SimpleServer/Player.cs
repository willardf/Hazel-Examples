using Hazel;
using System.Threading;

namespace HazelServer
{
    internal class Player
    {
        private const int InvalidGameId = -1;

        private static int PlayerCounter = 0;

        public readonly int Id;
        public readonly int ClientVersion;
        public readonly Connection Connection;

        private readonly ServerProgram gameManager;

        private int currentGame = InvalidGameId;

        public Player(ServerProgram server, int clientVersion)
        {
            this.gameManager = server;
            this.ClientVersion = clientVersion;
            this.Id = Interlocked.Increment(ref PlayerCounter);
        }

        public void HandleMessage(DataReceivedEventArgs obj)
        {
            try
            {
                // This pattern allows us to pack and handle multiple messages
                // This creates really good packet efficiency, but watch out for MTU.
                while (obj.Message.Position < obj.Message.Length)
                {
                    // Okay, I lied. You won't need to recycle any message from ReadMessage!
                    // They share the internal MessageReader.Buffer with the parent, so there's no new buffer to pool!
                    var msg = obj.Message.ReadMessage();
                    switch ((PlayerMessageTags)msg.Tag)
                    {
                        // No expected data, returns positive 4-byte game id or negative error code
                        case PlayerMessageTags.CreateGame:
                            {
                                var message = MessageWriter.Get(SendOption.Reliable);
                                message.StartMessage((byte)PlayerMessageTags.CreateGame);

                                // Locking to protect the checking and potential assignment of this.CurrentGame
                                // There are ways of doing this with less locking, but given that the client app
                                // Shouldn't allow the spamming of this tag, it shouldn't create much contention.
                                lock (this)
                                {
                                    if (this.currentGame != InvalidGameId)
                                    {
                                        message.Write((int)ErrorCodes.AlreadyInGame);
                                    }
                                    else
                                    {
                                        GameData game = new GameData();
                                        this.currentGame = game.Id;
                                        game.AddPlayer(this);
                                        message.Write(game.Id);

                                        gameManager.AddGame(game);
                                    }
                                }

                                message.EndMessage();

                                try
                                {
                                    obj.Sender.Send(message);
                                }
                                catch { }

                                // You don't always have to recycle in a finally, just be *very* sure you are recycling
                                message.Recycle();
                            }
                            break;

                        // Expected format: 4-byte game id, returns the same game id or a negative error
                        case PlayerMessageTags.JoinGame:
                            {
                                var message = MessageWriter.Get(SendOption.Reliable);
                                message.StartMessage((byte)PlayerMessageTags.JoinGame);

                                lock (this)
                                {
                                    if (this.currentGame != InvalidGameId)
                                    {
                                        message.Write((int)ErrorCodes.AlreadyInGame);
                                    }
                                    else
                                    {
                                        var gameId = msg.ReadInt32();
                                        if (this.gameManager.TryGetGame(gameId, out var game))
                                        {
                                            this.currentGame = gameId;
                                            game.AddPlayer(this);
                                            message.Write(gameId);
                                        }
                                        else
                                        {
                                            message.Write((int)ErrorCodes.GameNotFound);
                                        }
                                    }
                                }

                                message.EndMessage();
                                try
                                {
                                    obj.Sender.Send(message);
                                }
                                catch { }

                                message.Recycle();
                            }
                            break;
                    }
                }
            }
            catch
            {
                // Usually some error logging and/or handling here.
            }
            finally
            {
                obj.Message.Recycle();
            }
        }
    }
}