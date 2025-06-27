using System.Configuration;
using Autofac;
using AutofacSerilogIntegration;
using GameServer.Aptitude;
using GameServer.Physics;
using GameServer.Systems.Chat;
using GameServer.Systems.Encounters;
using Serilog;
using Shared.Common;
using Shared.Udp;
using SDB = FauFau.Formats.StaticDB;

namespace GameServer;

public class GameServerModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterLogger();
        RegisterTypes(builder);
        RegisterInstances(builder);
        base.Load(builder);
    }

    private static void RegisterTypes(ContainerBuilder builder)
    {
        builder.RegisterType<GameServerSettings>().SingleInstance();
        builder.RegisterType<SDB>().SingleInstance();
        builder.RegisterType<GameServer>().AsSelf().As<IPacketSender>().SingleInstance();
        builder.RegisterType<ShardFactory>().As<IShardFactory>().SingleInstance();
        builder.RegisterType<Shard>().SingleInstance();
        builder.RegisterType<PhysicsEngine>();
        builder.RegisterType<MovementRelay>();
        builder.RegisterType<AbilitySystem>();
        builder.RegisterType<EntityManager>();
        builder.RegisterType<EncounterManager>();
        builder.RegisterType<WeaponSim>();
        builder.RegisterType<ProjectileSim>();
        builder.RegisterType<ChatService>();
        builder.RegisterType<AdminService>();
    }

    private static void RegisterInstances(ContainerBuilder builder)
    {
        builder.Register(ctx =>
        {
            var settings = new GameServerSettings();

            if (ConfigurationManager.AppSettings["Port"] != null)
            {
                settings.Port = ushort.Parse(ConfigurationManager.AppSettings["Port"]);
            }

            if (ConfigurationManager.AppSettings["GrpcChannelAddress"] != null)
            {
                settings.GrpcChannelAddress = ConfigurationManager.AppSettings["GrpcChannelAddress"];
            }

            if (ConfigurationManager.AppSettings["StaticDBPath"] != null)
            {
                settings.StaticDBPath = ConfigurationManager.AppSettings["StaticDBPath"];
            }

            if (ConfigurationManager.AppSettings["ZoneId"] != null)
            {
                settings.ZoneId = uint.Parse(ConfigurationManager.AppSettings["ZoneId"]);
            }

            if (ConfigurationManager.AppSettings["MapsPath"] != null)
            {
                settings.MapsPath = ConfigurationManager.AppSettings["MapsPath"];

                if (ConfigurationManager.AppSettings["LoadMapsCollision"] != null)
                {
                    if (bool.TryParse(ConfigurationManager.AppSettings["LoadMapsCollision"], out bool value))
                    {
                        settings.LoadMapsCollision = value;
                    }
                    else
                    {
                        Log.Error("Cannot parse LoadMapsCollision setting value");
                    }
                }
            }

            if (ConfigurationManager.AppSettings["LoadZoneEntities"] != null)
            {
                if (bool.TryParse(ConfigurationManager.AppSettings["LoadZoneEntities"], out bool value))
                {
                    settings.LoadZoneEntities = value;
                }
                else
                {
                    Log.Error("Cannot parse LoadZoneEntities setting value");
                }
            }

            return settings;
        })
        .As<GameServerSettings>().SingleInstance();

        builder.Register(ctx =>
        {
            var loggerConfig = new LoggerConfiguration()
            .ReadFrom.AppSettings()
            .WriteTo.Console(theme: SerilogTheme.Custom, outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u4} {SourceContext}] {Message:lj}{NewLine}{Exception}");

            var settings = ctx.Resolve<GameServerSettings>();

            if (settings.LogLevel.HasValue)
            {
                loggerConfig = loggerConfig.MinimumLevel.Is(settings.LogLevel.Value);
            }

            var logger = loggerConfig.CreateLogger();
            Log.Logger = logger;
            return logger;
        })
        .As<ILogger>().SingleInstance();

        builder.Register(ctx =>
        {
            var settings = ctx.Resolve<GameServerSettings>();
            Log.Information("Opening SDB from {Path}", settings.StaticDBPath);
            var sdb = new SDB();
            sdb.Read(settings.StaticDBPath);

            return sdb;
        })
        .As<SDB>().SingleInstance();
    }
}