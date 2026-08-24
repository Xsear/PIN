using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Serilog;

namespace Shared.Udp;

public abstract class PacketServer : IPacketSender
{
    public const int MTU = 1400;

    protected readonly ILogger Logger;

    protected readonly Socket ServerSocket;
    protected readonly IPEndPoint ListenEndpoint;
    protected BufferBlock<Packet?> IncomingPackets;
    protected BufferBlock<Packet?> OutgoingPackets;
    protected CancellationTokenSource Source;

    protected PacketServer(ushort port, ILogger logger)
    {
        Logger = logger.ForContext<PacketServer>();
        ListenEndpoint = new IPEndPoint(IPAddress.Any, port);
        ServerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    }

    public bool IsRunning { get; protected set; }

    // TODO: Move to separate thread? add console/rcon handling here?
    // FIXME: Move timing to GameServer
    public void Run()
    {
        Source = new CancellationTokenSource();
        var ct = Source.Token;

        IncomingPackets = new BufferBlock<Packet?>();
        OutgoingPackets = new BufferBlock<Packet?>();

        var listenThread = Utils.RunThread(ListenThreadAsync, ct);
        var runThread = Utils.RunThread(ServerRunThreadAsync, ct);
        var sendThread = Utils.RunThread(SendThreadAsync, ct);

        Startup(ct);

        IsRunning = true;

        while (IsRunning)
        {
            // TODO: Handle Command
            if (Console.IsInputRedirected)
            {
                Thread.Sleep(100);
            }
            else
            {
                var line = Console.ReadLine();
                HandleCommand(line);
            }
        }

        if (!Source.IsCancellationRequested)
        {
            Source.Cancel();
        }

        try
        {
            ServerSocket.Close();
        }
        catch (Exception)
        {
            // Socket already closed
        }

        Shutdown(ct);
    }

    public async Task<bool> SendAsync(Memory<byte> packet, IPEndPoint endPoint)
    {
        return await OutgoingPackets.SendAsync(new Packet(endPoint, packet));
    }

    protected virtual void HandleCommand(string line)
    {
        if (line.Trim().StartsWith("exit"))
        {
            IsRunning = false;
            Source.Cancel();
        }
    }

    protected abstract void HandlePacket(Packet p, CancellationToken ct);
    protected virtual void Startup(CancellationToken ct)
    {
    }

    protected virtual async void ServerRunThreadAsync(CancellationToken ct)
    {
        try
        {
            Packet? p;
            while ((p = await IncomingPackets.ReceiveAsync(ct)) != null)
            {
                try
                {
                    HandlePacket(p.Value, ct);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error handling packet from {0}", p.Value.RemoteEndpoint);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
    }

    protected virtual void Shutdown(CancellationToken ct)
    {
    }

    private async void ListenThreadAsync(CancellationToken ct)
    {
        ServerSocket.Blocking = true;
        ServerSocket.DontFragment = true;
        TrySetBufferSize(SocketOptionName.ReceiveBuffer, MTU * 1000);
        TrySetBufferSize(SocketOptionName.SendBuffer, MTU * 1000);
        ServerSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
        ServerSocket.Bind(ListenEndpoint);

        Logger.Information("Listening on {0}", ListenEndpoint);

        var buffer = new byte[MTU * 10];
        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        Thread.CurrentThread.Priority = ThreadPriority.Highest;

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                // Sockets don't support async yet :( Blocking here bc the win api will yield and wait better on the native side than we can here
                int numberOfBytesReceived;
                if ((numberOfBytesReceived = ServerSocket.ReceiveFrom(buffer, SocketFlags.None, ref remoteEndPoint)) > 0)
                {
                    // Should probably change to ArrayPool<byte>, but can't return a Memory<byte> :(
                    // TODO: Move Endpoint and Memory<byte> management to Packet (constructor + destructor)
                    var buf = new byte[numberOfBytesReceived];
                    buffer.AsSpan()[..numberOfBytesReceived].CopyTo(buf);
                    _ = await IncomingPackets.SendAsync(new Packet((IPEndPoint)remoteEndPoint, buf, DateTime.Now), ct);

                    // Not 100% sure this needs to be cleared?
                    remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                if (ct.IsCancellationRequested)
                {
                    break; // Socket closed during shutdown
                }

                // Transient UDP errors (e.g. ICMP port unreachable from a dead peer) must not kill the listener
                Logger.Warning(ex, "Listen error {0}; continuing.", ex.SocketErrorCode);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error {0}", "listenThread");
            }
        }
    }

    private void TrySetBufferSize(SocketOptionName option, int size)
    {
        try
        {
            ServerSocket.SetSocketOption(SocketOptionLevel.Socket, option, size);
        }
        catch (SocketException ex)
        {
            Logger.Warning("Could not set {Option} to {Size} bytes ({Error}); keeping kernel default.", option, size, ex.SocketErrorCode);
        }
    }

    private async void SendThreadAsync(CancellationToken ct)
    {
        Thread.CurrentThread.Priority = ThreadPriority.Highest;

        try
        {
            while (true)
            {
                Packet? packet;
                while ((packet = await OutgoingPackets.ReceiveAsync(ct)) != null)
                {
                    try
                    {
                        _ = ServerSocket.SendTo(packet.Value.PacketData.Span, SocketFlags.None, packet.Value.RemoteEndpoint);
                    }
                    catch (SocketException ex)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            throw; // Socket closed during shutdown - let the outer handler stop the thread
                        }

                        // Transient UDP errors (e.g. ECONNREFUSED from an ICMP port unreachable of a dead peer) must not kill the sender
                        Logger.Warning(ex, "Send to {0} failed ({1}); dropping packet.", packet.Value.RemoteEndpoint, ex.SocketErrorCode);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
        catch (SocketException)
        {
            // Socket closed during shutdown
        }
    }
}