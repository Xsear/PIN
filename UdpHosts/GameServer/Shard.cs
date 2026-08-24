using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using GameServer.Entities;
using GameServer.Entities.Outpost;
using GameServer.Physics;
using GameServer.Systems.Admin;
using GameServer.Systems.Aptitude;
using GameServer.Systems.CharacterLifecycle;
using GameServer.Systems.Chat;
using GameServer.Systems.Combat;
using GameServer.Systems.Encounters;
using GameServer.Systems.EntityManager;
using GameServer.Systems.MovementRelay;
using GameServer.Systems.NpcDeath;
using GameServer.Systems.PlayerRespawn;
using GameServer.Systems.ProjectileSim;
using GameServer.Systems.SystemEvents;
using GameServer.Systems.WeaponSim;
using Shared.Common;
using Shared.Udp;

namespace GameServer;

public class Shard : IShard
{
    private const double _networkTickRate = 1.0 / 20.0;

    private readonly double _gameTickMs;
    private readonly TickStats _tickStats = new();
    private long _startTime;
    private double _lastNetTick;
    private ushort _lastEntityRefId;

    public Shard(double gameTickRate, ulong instanceId, GameServerSettings settings, IPacketSender sender, Serilog.ILogger logger)
    {
        _lastEntityRefId = 0;
        // gameTickRate is the tick period in seconds (e.g. 1/60)
        _gameTickMs = gameTickRate * 1000.0;
        InstanceId = instanceId;
        Settings = settings;
        ZoneId = settings.ZoneId;
        Sender = sender;
        Logger = logger;
        Clients = new ConcurrentDictionary<uint, INetworkPlayer>();
        Entities = new ConcurrentDictionary<ulong, IEntity>();
        Encounters = new ConcurrentDictionary<ulong, IEncounter>();
        Outposts = new ConcurrentDictionary<uint, IDictionary<uint, OutpostEntity>>();
        EventBus = new EventBus();
        var debugCallbacks = new DebugProjectileHitCallbacks(this);
        Physics = new PhysicsEngine(EventBus, Settings.ZoneId, Settings.MapsPath, Settings.AssetDBPath, Settings.LoadMapsCollision, debugCallbacks, false, Settings.CachePath, Settings.ForceReloadZone, _tickStats);
        AI = new AIEngine();
        Movement = new MovementRelay(this);
        Abilities = new AbilitySystem(this);
        EntityMan = new EntityManager(this);
        EncounterMan = new EncounterManager(this);
        WeaponSim = new WeaponSim(this);
        ProjectileSim = new ProjectileSim(this, debugCallbacks);
        Chat = new ChatService(this, EventBus);
        Admin = new AdminService(this);
        var npcDeathRules = new StandardNpcDeathRules();
        Damage = new DamageSystem(EventBus, this, npcDeathRules);
        Combat = new CombatSim(EventBus, EntityMan, Damage, this);
        CharacterLifecycle = new CharacterLifecycleService(this, EventBus, new StandardCharacterLifecycleRules());
        PlayerRespawn = new PlayerRespawnService(this, EventBus, new StandardPlayerRespawnRules(), CharacterLifecycle);
        NpcDeath = new NpcDeathService(this, EventBus, npcDeathRules);
        EntityRefMap = new ConcurrentDictionary<ushort, Tuple<IEntity, Enums.GSS.Controllers>>();
    }

    public DateTime StartTime => DateTimeExtensions.Epoch.AddSeconds(_startTime);
    public IDictionary<ulong, IEntity> Entities { get; protected set; }
    public IDictionary<ulong, IEncounter> Encounters { get; protected set; }
    public IDictionary<uint, IDictionary<uint, OutpostEntity>> Outposts { get; protected set; }
    public IDictionary<uint, INetworkPlayer> Clients { get; }
    public EventBus EventBus { get; }
    public PhysicsEngine Physics { get; }
    public AIEngine AI { get; }
    public MovementRelay Movement { get; }
    public EntityManager EntityMan { get; }
    public EncounterManager EncounterMan { get; }
    public AbilitySystem Abilities { get; }
    public ProjectileSim ProjectileSim { get; }
    public WeaponSim WeaponSim { get; }
    public ChatService Chat { get; }
    public AdminService Admin { get; }
    public DamageSystem Damage { get; }
    public CombatSim Combat { get; }
    public CharacterLifecycleService CharacterLifecycle { get; }
    public PlayerRespawnService PlayerRespawn { get; }
    public NpcDeathService NpcDeath { get; }
    public ulong InstanceId { get; }
    public uint ZoneId { get; private set; }
    public ulong CurrentTimeLong { get; private set; }
    public uint CurrentTime => unchecked((uint)CurrentTimeLong);
    public ushort CurrentShortTime => unchecked((ushort)CurrentTime);
    public IDictionary<ushort, Tuple<IEntity, Enums.GSS.Controllers>> EntityRefMap { get; }
    public Serilog.ILogger Logger { get; }
    public GameServerSettings Settings { get; }
    public TickStats TickStats => _tickStats;
    private IPacketSender Sender { get; }

    public void Run(CancellationToken ct)
    {
        Utils.RunThread(RunThread, ct);
    }

    public void NetworkTick(double deltaTime, ulong currentTime, CancellationToken ct)
    {
        // Handle timeout, reliable retransmission, normal rx/tx
        var t = Stopwatch.GetTimestamp();
        foreach (var client in Clients.Values)
        {
            client.NetworkTick(deltaTime, currentTime, ct);
        }

        _tickStats.Accumulate(TickPhase.Net, ref t);
    }

    public bool Tick(double deltaTime, ulong currentTime, CancellationToken ct)
    {
        CurrentTimeLong = currentTime;

        var t = Stopwatch.GetTimestamp();

        AI.Tick(deltaTime, currentTime, ct);
        _tickStats.Accumulate(TickPhase.Ai, ref t);

        Physics.Tick(deltaTime, currentTime, ct);
        _tickStats.Accumulate(TickPhase.Physics, ref t);

        EntityMan.Tick(deltaTime, currentTime, ct);
        _tickStats.Accumulate(TickPhase.EntityMan, ref t);

        EncounterMan.Tick(deltaTime, currentTime, ct);
        _tickStats.Accumulate(TickPhase.Encounter, ref t);

        Abilities.Tick(deltaTime, currentTime, ct);
        _tickStats.Accumulate(TickPhase.Abilities, ref t);

        WeaponSim.Tick(deltaTime, currentTime, ct);
        _tickStats.Accumulate(TickPhase.Weapon, ref t);

        ProjectileSim.Tick(deltaTime, currentTime, ct);
        _tickStats.Accumulate(TickPhase.Projectile, ref t);

        Damage.Tick(deltaTime, currentTime, ct);
        _tickStats.Accumulate(TickPhase.Damage, ref t);

        CharacterLifecycle.Tick(deltaTime, currentTime, ct);
        _tickStats.Accumulate(TickPhase.Lifecycle, ref t);

        PlayerRespawn.Tick(deltaTime, currentTime, ct);
        _tickStats.Accumulate(TickPhase.Respawn, ref t);

        EventBus.Flush();
        _tickStats.Accumulate(TickPhase.EventBus, ref t);

        return true;
    }

    public bool MigrateOut(INetworkPlayer player)
    {
        if (Clients.ContainsKey(player.SocketId))
        {
            if (Entities.ContainsKey(player.CharacterId))
            {
                EntityMan.Remove(player.CharacterId);
            }

            Clients.Remove(player.SocketId);
            Admin.ClearPlayer(player);
            return true;
        }

        return false;
    }

    public bool MigrateIn(INetworkPlayer player)
    {
        if (Clients.ContainsKey(player.SocketId))
        {
            return true;
        }

        player.Init(this);

        Clients.Add(player.SocketId, player);

        return true;
    }

    public async Task<bool> SendAsync(Memory<byte> packet, IPEndPoint endPoint)
    {
        return await Sender.SendAsync(packet, endPoint);
    }

    public ushort AssignNewRefId(IEntity entity, Enums.GSS.Controllers controller)
    {
        while (EntityRefMap.ContainsKey(unchecked(++_lastEntityRefId)) || _lastEntityRefId is 0 or 0xffff)
        {
        }

        EntityRefMap.Add(_lastEntityRefId, new Tuple<IEntity, Enums.GSS.Controllers>(entity, controller));

        return unchecked(_lastEntityRefId++);
    }

    public ulong GetNextGuid(byte type = (byte)Enums.GSS.Controllers.Generic)
    {
        return GuidService.GetNext(this, type);
    }

    protected virtual bool ShouldNetworkTick(double deltaTime, ulong currentTime)
    {
        return deltaTime >= _networkTickRate;
    }

    private void RunThread(CancellationToken ct)
    {
        _startTime = (long)DateTime.Now.UnixTimestamp();
        _lastNetTick = 0;

        var stopwatch = new Stopwatch();
        var lastTime = 0.0;
        var nextTick = _gameTickMs;

        stopwatch.Start();

        var tickEnd = Stopwatch.GetTimestamp();

        while (!ct.IsCancellationRequested)
        {
            var now = stopwatch.Elapsed.TotalMilliseconds;

            if (now < nextTick)
            {
                var remainingMs = (int)(nextTick - now);
                if (remainingMs > 2)
                {
                    Thread.Sleep(remainingMs - 1);
                }
                else
                {
                    Thread.SpinWait(10);
                }

                continue;
            }

            // Time spent since the last tick finished: sleep imprecision, GC pauses, thread starvation
            _tickStats.AddGap(tickEnd);

            var tickStart = Stopwatch.GetTimestamp();

            var currentUnixTimestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var currentTime = unchecked((ulong)now);
            var delta = now - lastTime;

            if (ShouldNetworkTick(currentTime - _lastNetTick, currentUnixTimestamp))
            {
                NetworkTick(currentTime - _lastNetTick, currentUnixTimestamp, ct);
                _lastNetTick = currentTime;
            }

            if (!Tick(delta, currentUnixTimestamp, ct))
            {
                break;
            }

            _tickStats.Accumulate(TickPhase.Total, ref tickStart);
            _tickStats.TryReport(Logger, InstanceId, Entities.Count, Clients.Count);

            tickEnd = Stopwatch.GetTimestamp();
            lastTime = now;

            // Advance to the next tick; after a stall, don't try to catch up
            if (nextTick <= now)
            {
                nextTick = now + _gameTickMs;
            }
            else
            {
                nextTick += _gameTickMs;
            }
        }

        stopwatch.Stop();
    }
}