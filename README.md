# Hazel-Examples

Okay, this first example is a little too complicated and a little rushed, but it sets up a server and a client, allows the client to create a game, or join a known, existing game.

It should demonstrate lots of best practices, and I've tried to comment near the few bad ones.

In general, you should:

1. Clone the repo
2. Fix the reference in the server code (Reference Hazel.dll)
3. Copy Hazel.dll and HazelNetworkManager into Unity
4. Make a GameObject with HazelNetworkManager
5. Call CoConnect as a Coroutine
6. Call JoinGame or CreateGame

I'll clean this up sometime later. I promise. But also, it's not meant to be a full server demo. This should be more than enough to get a user started passing information across the internet.