using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Eclipse;

// Makes him heavy. His bones block physics but can't be pushed. Ragdolls are left out.
internal static class Body
{
    private const string SkelKey = "eclipse.skeleton";
    private const PrimitiveComponent.ERBCollisionChannel Channel = PrimitiveComponent.ERBCollisionChannel.RBCC_VehicleBlocker;

    private static readonly PrimitiveComponent.ERBCollisionChannel[] Hits =
    {
        PrimitiveComponent.ERBCollisionChannel.RBCC_Default,
        PrimitiveComponent.ERBCollisionChannel.RBCC_Vehicle,
        PrimitiveComponent.ERBCollisionChannel.RBCC_FlyingVehicle,
        PrimitiveComponent.ERBCollisionChannel.RBCC_GameplayPhysics,
        PrimitiveComponent.ERBCollisionChannel.RBCC_Prop,
        PrimitiveComponent.ERBCollisionChannel.RBCC_PropDynamicChunks,
        PrimitiveComponent.ERBCollisionChannel.RBCC_MagneticDynamicObjects,
        PrimitiveComponent.ERBCollisionChannel.RBCC_MagneticProp,
    };

    private static bool _skeleton;
    private static bool _failSaid;

    public static bool Wanted(out RPawnPlayer? pawn)
    {
        pawn = null;
        if (!Opt.HeavyOn)
        {
            _nearBatmobile = false;
            return false;
        }

        if (!World.InGame || World.Pc is not { } pc || Hooks.InCinematic(pc))
            return false;
        if (World.Pawn is not RPawnPlayer p || p.bHidden || p.Health <= 0 || p.DrivenVehicle is { IsValid: true })
            return false;

        // Near the Batmobile he's normal, or his bones throw it about when he gets in.
        if (NearBatmobile(p))
        {
            _nearBatmobile = true;
            return false;
        }

        // Things he carries pass through him on the way over his head.
        if (Suit.I.Holding || Suit.I.ThrowLeaving)
            return false;

        _nearBatmobile = false;
        pawn = p;
        return true;
    }

    private const float BatmobileGap = 600f;
    private static RVehicleBatmobileBase? _batmobile;
    private static Every _batmobileLook;
    private static bool _nearBatmobile, _quiet;

    private static bool NearBatmobile(RPawnPlayer pawn)
    {
        // There's more than one in the world, so it's the nearest that counts.
        if (_batmobileLook.Elapsed(1.0))
        {
            _batmobile = null;
            float best = float.MaxValue;
            foreach (RVehicleBatmobileBase car in Game.FindObjects<RVehicleBatmobileBase>())
            {
                if (car is not { IsValid: true, IsClassDefaultObject: false, bDeleteMe: false })
                    continue;

                float d = Vector3.DistanceSquared(car.Location, pawn.Location);
                if (d < best)
                {
                    best = d;
                    _batmobile = car;
                }
            }
        }

        if (pawn.CurrentSpecialMove is RSpecialMoveInstance_DriveCar { IsValid: true })
            return true;
        return _batmobile is { IsValid: true, bDeleteMe: false } bat && Vector3.DistanceSquared(bat.Location, pawn.Location) < BatmobileGap * BatmobileGap;
    }

    // Called each frame after he's moved.
    public static void Follow()
    {
        if (!Wanted(out RPawnPlayer? pawn) || pawn == null)
        {
            Off();
            return;
        }

        NoVault();
        Skeleton(pawn);
    }

    // Clears the vault target each frame so he walks into cars instead.
    private static void NoVault()
    {
        if (World.Pc is not RPlayerController { EnvironmentChecker: { IsValid: true } env } || env.VehicleBarrier is not { IsValid: true })
            return;

        env.VehicleBarrier = null!;
    }

    private static void Skeleton(RPawnPlayer pawn)
    {
        if (pawn.Mesh is not { IsValid: true } mesh || mesh.PhysicsAsset is not { IsValid: true } asset)
        {
            if (!_failSaid)
            {
                _failSaid = true;
                Log.Info("Heavy: his suit has no physics body, so heavy can't work with it.");
            }

            return;
        }

        bool live = mesh.PhysicsAssetInstance is { IsValid: true };
        if (_skeleton && live && mesh.BlockRigidBody && mesh.RBChannel == Channel)
            return;

        Remember(mesh);

        if (!live)
        {
            mesh.SetHasPhysicsAssetInstance(true, true);
            if (mesh.PhysicsAssetInstance is not { IsValid: true })
            {
                if (!_failSaid)
                {
                    _failSaid = true;
                    Log.Info("Heavy: couldn't start his skeleton's physics (" + asset.Name + ").");
                }

                return;
            }
        }

        mesh.bUpdateKinematicBonesFromAnimation = true;
        mesh.MinDistFactorForKinematicUpdate = 0f;
        mesh.SetRBChannel(Channel);
        foreach (PrimitiveComponent.ERBCollisionChannel channel in Hits)
            mesh.SetRBCollidesWithChannel(channel, true);
        mesh.SetBlockRigidBody(true);

        _skeleton = true;
        _quiet = false;
    }

    // How his mesh is before anything is changed, kept for as long as it's the same mesh.
    private static void Remember(SkeletalMeshComponent mesh)
    {
        if (Shared.Get(SkelKey) is not long[] { Length: >= 8 } had || had[6] != (long)mesh.Ptr)
            Shared.Set(SkelKey, Snapshot(mesh));
    }

    // Read from the mesh's archetype when it can be, since after a reload his mesh may already be heavy.
    private static long[] Snapshot(SkeletalMeshComponent mesh)
    {
        SkeletalMeshComponent from = mesh.ObjectArchetype is SkeletalMeshComponent { IsValid: true } normal ? normal : mesh;
        long mask = 0;
        for (int i = 0; i < Hits.Length; i++)
        {
            if (Collides(from, Hits[i]))
                mask |= 1L << i;
        }

        return new[]
        {
            mesh.PhysicsAssetInstance is { IsValid: true } ? 1L : 0L,
            from.BlockRigidBody ? 1L : 0L,
            (long)from.RBChannel,
            from.bUpdateKinematicBonesFromAnimation ? 1L : 0L,
            mask,
            BitConverter.SingleToInt32Bits(from.MinDistFactorForKinematicUpdate),
            (long)mesh.Ptr,
            ReferenceEquals(from, mesh) ? 0L : 1L,
        };
    }

    private static bool Collides(SkeletalMeshComponent mesh, PrimitiveComponent.ERBCollisionChannel channel)
    {
        ref PrimitiveComponent.FRBCollisionChannelContainer c = ref mesh.RBCollideWithChannels;
        return channel switch
        {
            PrimitiveComponent.ERBCollisionChannel.RBCC_Default => c.Default,
            PrimitiveComponent.ERBCollisionChannel.RBCC_Vehicle => c.Vehicle,
            PrimitiveComponent.ERBCollisionChannel.RBCC_FlyingVehicle => c.FlyingVehicle,
            PrimitiveComponent.ERBCollisionChannel.RBCC_GameplayPhysics => c.GameplayPhysics,
            PrimitiveComponent.ERBCollisionChannel.RBCC_Prop => c.Prop,
            PrimitiveComponent.ERBCollisionChannel.RBCC_PropDynamicChunks => c.PropDynamicChunks,
            PrimitiveComponent.ERBCollisionChannel.RBCC_MagneticDynamicObjects => c.MagneticDynamicObjects,
            PrimitiveComponent.ERBCollisionChannel.RBCC_MagneticProp => c.MagneticProp,
            _ => false,
        };
    }

    private static void SkeletonOff(RPawnPlayer? pawn)
    {
        _skeleton = false;
        _quiet = false;
        long[]? saved = Shared.Get(SkelKey) as long[];
        if (saved is not { Length: >= 8 } || pawn?.Mesh is not { IsValid: true } mesh || saved[6] != (long)mesh.Ptr)
            return;

        mesh.SetRBChannel((PrimitiveComponent.ERBCollisionChannel)saved[2]);
        for (int i = 0; i < Hits.Length; i++)
            mesh.SetRBCollidesWithChannel(Hits[i], ((saved[4] >> i) & 1) != 0);
        mesh.SetBlockRigidBody(saved[1] != 0);
        mesh.bUpdateKinematicBonesFromAnimation = saved[3] != 0;
        mesh.MinDistFactorForKinematicUpdate = BitConverter.Int32BitsToSingle((int)saved[5]);
        if (saved[0] == 0 && mesh.PhysicsAssetInstance is { IsValid: true })
            mesh.SetHasPhysicsAssetInstance(false, false);
    }

    // Put back once when heavy goes off, or it fights the Batmobile's quiet channel.
    private static void Off(bool force = false)
    {
        RPawnPlayer? pawn = World.Pawn as RPawnPlayer;
        if (pawn is { IsValid: false })
            pawn = null;
        bool quietNow = _nearBatmobile && pawn != null;
        if (force || _skeleton || (_quiet && !quietNow))
            SkeletonOff(pawn);
        if (quietNow)
            Quiet(pawn!);
    }

    // Near the Batmobile his bones must not touch it, or getting in throws it about.
    private static void Quiet(RPawnPlayer pawn)
    {
        if (pawn.Mesh is not { IsValid: true } mesh)
            return;

        // Turning blocking off didn't take. A channel nothing collides with does.
        if (mesh.RBChannel == PrimitiveComponent.ERBCollisionChannel.RBCC_Nothing)
            return;

        mesh.SetRBChannel(PrimitiveComponent.ERBCollisionChannel.RBCC_Nothing);
        mesh.SetRBCollidesWithChannel(PrimitiveComponent.ERBCollisionChannel.RBCC_Vehicle, false);
        _quiet = true;
    }

    // After a script reload, undoes what the last load left on him.
    public static void Recover() => Off(force: true);

    public static void Forget()
    {
        Off();
        Shared.Set(SkelKey, null);
        _nearBatmobile = false;
        _failSaid = false;
    }
}
