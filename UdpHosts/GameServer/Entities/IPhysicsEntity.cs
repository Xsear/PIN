namespace GameServer.Entities;

public interface IPhysicsEntity : IEntity
{
    bool HasPhysicsBody { get; set; }
}

public interface ICommonPhysicsEntity : IPhysicsEntity
{
    PhysicsInfo PhysicsPoseInfo { get; set; }
}