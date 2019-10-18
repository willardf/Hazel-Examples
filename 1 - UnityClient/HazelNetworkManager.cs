using Hazel;
using Hazel.Udp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

// Obviously this isn't a real Unity project, but we'll just pretend.
// I don't want to create the external buttons and stuff, so that's totally up to you.
namespace UnityClient
{
    // Maybe you want to use another lib to keep this in sync?
    // They usually don't change enough for copy-paste to be a problem, though.
    internal enum PlayerMessageTags
    {
        CreateGame,
        JoinGame,
        PlayerJoined,
        PlayersInGame,
        GameData
    }

    internal enum ErrorCodes
    {
        AlreadyInGame = -1,
        GameNotFound = -2
    }

    // Usually this kind of class should be a singleton, but everyone has 
    // their own way of doing that. So I leave it up to you.
    internal class HazelNetworkManager : MonoBehaviour
    {
        private const int ServerPort = 23456;

        // Unity gets very grumpy if you start messing with GameObjects on threads
        // other than the main one. So while sending/receiving messages can be multithreaded,
        // we need a queue to hold events until a Update/FixedUpdate method can handle them.
        public List<Action> EventQueue = new List<Action>();

        // How many seconds between batched messages
        public float MinSendInterval = .1f;

        public int GameId = -1;

        private UdpClientConnection connection;

        // This will hold a reliable and an unreliable "channel", so you can batch 
        // messages to the server every MinSendInterval seconds.
        private MessageWriter[] Streams;

        private float timer = 0;

        public void Update()
        {
            lock (this.EventQueue)
            {
                foreach (var evt in this.EventQueue)
                {
                    evt();
                }

                this.EventQueue.Clear();
            }

            this.timer += Time.fixedDeltaTime;
            if (this.timer < this.MinSendInterval)
            {
                // Unless you are making a highly competitive action game, you don't need updates
                // every frame. And many network connections cannot handle that kind of traffic.
                return;
            }

            this.timer = 0;

            foreach (var msg in this.Streams)
            {
                try
                {
                    // TODO: In hazel, I need to change this so it makes sense
                    // Right now:
                    // 7 = Tag (1) + MessageLength (2) + GameId (4)
                    // Ideally, no magic calculation, just msg.HasMessages
                    if (!msg.HasBytes(7)) continue;
                    msg.EndMessage();

                    this.connection.Send(msg);
                }
                catch 
                {
                    // Logging, probably
                }

                msg.Clear(msg.SendOption);
                msg.StartMessage((byte)PlayerMessageTags.GameData);
                msg.Write(this.GameId);
            }
        }

        // The UI should probably not allow this again until a response has been received or you disconnect
        public void CreateGame(int gameId)
        {
            if (this.connection == null) return;

            var msg = MessageWriter.Get(SendOption.Reliable);
            msg.StartMessage((byte)PlayerMessageTags.CreateGame);
            msg.EndMessage();

            try { this.connection.Send(msg); } catch { }
            msg.Recycle();
        }

        public void JoinGame(int gameId)
        {
            if (this.connection == null) return;

            var msg = MessageWriter.Get(SendOption.Reliable);
            msg.StartMessage((byte)PlayerMessageTags.JoinGame);
            msg.Write(gameId);
            msg.EndMessage();

            try { this.connection.Send(msg); } catch { }
            msg.Recycle();
        }

        public IEnumerator CoConnect()
        {
            // Don't leak connections!
            if (this.connection != null) yield break;

            // Initialize streams (once)
            if (this.Streams == null)
            {
                this.Streams = new MessageWriter[2];
                for (int i = 0; i < this.Streams.Length; ++i)
                {
                    this.Streams[i] = MessageWriter.Get((SendOption)i);
                }
            }

            // Clear any existing data, and prep them for batching
            for (int i = 0; i < this.Streams.Length; ++i)
            {
                var stream = this.Streams[i];
                stream.Clear((SendOption)i);
                stream.StartMessage((byte)PlayerMessageTags.GameData);
                stream.Write(this.GameId);
            }

            this.connection = new UdpClientConnection(new IPEndPoint(IPAddress.Loopback, ServerPort));
            this.connection.DataReceived += HandleMessage;
            this.connection.Disconnected += HandleDisconnect;

            // If you block in a Unity Coroutine, it'll hang the game!
            this.connection.ConnectAsync(GetConnectionData());

            while (this.connection != null && this.connection.State != ConnectionState.Connected)
            {
                yield return null;
            }
        }

        // Remember this is on a new thread.
        private void HandleDisconnect(object sender, DisconnectedEventArgs e)
        {
            lock (this.EventQueue)
            {
                this.EventQueue.Clear();
                // Maybe something like:
                // this.EventQueue.Add(ChangeToMainMenuSceneWithError(e.Reason));
            }
        }

        // I usually like to make event callbacks like this abstract, then create a 
        // game-specific subclass to implement them. This separates some of the game 
        // logic from the network logic.
        private void HandleError(ErrorCodes errorCode)
        {
            // Maybe something like:
            // ErrorPopup.Instance.ShowError(errorCode);
        }

        private void HandleJoinGame()
        {

        }

        private void HandleMessage(DataReceivedEventArgs obj)
        {
            try
            {
                while (obj.Message.Position < obj.Message.Length)
                {
                    // Remember from the server code that sub-messages aren't pooled,
                    // they share the parent message's buffer. So don't recycle them!
                    var msg = obj.Message.ReadMessage();
                    switch ((PlayerMessageTags)msg.Tag)
                    {
                        case PlayerMessageTags.JoinGame:
                        case PlayerMessageTags.CreateGame:
                            HandleJoinGameResponse(msg);
                            break;

                        // Not implemented:
                        case PlayerMessageTags.PlayerJoined:
                            // Display that someone joined!
                        case PlayerMessageTags.PlayersInGame:
                            // Display who is already here!
                        case PlayerMessageTags.GameData:
                            // Handle that data!
                            break;
                    }
                }
            }
            catch
            {
                // Error logging
            }
            finally
            {

            }
        }

        // Turns out in this simple example, both creating a game and joining a game have the same
        // reaction from the server. Usually the creator of the game might have more reponsibilities like
        // spawning objects, but in this case, I leave it to your imagination!
        private void HandleJoinGameResponse(MessageReader msg)
        {
            int idOrError = msg.ReadInt32();
            if (idOrError < 0)
            {
                lock (this.EventQueue)
                {
                    this.EventQueue.Add(() => HandleError((ErrorCodes)idOrError));
                }
            }
            else
            {
                // So bad. Use a better locking pattern to protect this.GameId.
                lock (this)
                {
                    if (this.GameId == -1)
                    {
                        this.GameId = idOrError;
                        lock (this.EventQueue)
                        {
                            this.EventQueue.Add(() => HandleJoinGame());
                        }
                    }
                    else
                    {
                        // This is a pretty bad state. The client might be in two games? Or it just thinks it is...
                        // I would disconnect fully, reset the scene, and let everyone clean up.
                        // Probably want to enqueue something to alert the player as well.
                        // Ideally this state never ever happens though.
                    }
                }
            }
        }

        private static byte[] GetConnectionData()
        {
            // A version code. Could be anything though.
            return new byte[] { 1, 0, 0, 0 };
        }
    }
}
