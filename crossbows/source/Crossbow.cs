using CombatOverhaul;
using CombatOverhaul.Animations;
using CombatOverhaul.Implementations;
using CombatOverhaul.Inputs;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.RangedSystems.Aiming;
using OpenTK.Mathematics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using YamlDotNet.Serialization;

namespace Crossbows;

public enum CrossbowState
{
    Unloaded,
    Draw,
    Drawn,
    Load,
    Loaded,
    Aimed,
    Shooting,
    AimedEmpty
}

public class CrossbowStats : WeaponStats
{
    public string DrawAnimation { get; set; } = "";
    public string DrawnAnimation { get; set; } = "";
    public string LoadAnimation { get; set; } = "";
    public string ReleaseAnimation { get; set; } = "";
    public string AimAnimation { get; set; } = "";
    public string LoadedAnimation { get; set; } = "";
    public float DrawSpeedPenalty { get; set; } = -0.1f;
    public float LoadSpeedPenalty { get; set; } = -0.1f;

    public string DrawTpAnimation { get; set; } = "";
    public string LoadTpAnimation { get; set; } = "";
    public string AimTpAnimation { get; set; } = "";

    public AimingStatsJson Aiming { get; set; } = new();
    public float BoltDamageMultiplier { get; set; } = 1;
    public float BoltDamageStrength { get; set; } = 1;
    public float BoltVelocity { get; set; } = 1;
    public string BoltWildcard { get; set; } = "*bolt-*";
    public string DrawRequirement { get; set; } = "";
    public float Zeroing { get; set; } = 1.5f;
    public float[] DispersionMOA { get; set; } = [0, 0];
    public bool CancelReloadOnInAir { get; set; } = true;
    public bool CancelReloadMounted { get; set; } = true;

    public float ReloadAnimationSpeed { get; set; } = 1;
}

public class CrossbowClient : RangeWeaponClient
{
    public CrossbowClient(ICoreClientAPI api, Item item, AmmoSelector selector) : base(api, item)
    {
        Attachable = item.GetCollectibleBehavior<AnimatableAttachable>(withInheritance: true) ?? throw new Exception("Crossbow should have AnimatableAttachable behavior.");
        BoltTransform = new(item.Attributes["BoltTransform"].AsObject<ModelTransformNoDefaults>(), ModelTransform.BlockDefaultTp());
        AimingSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().AimingSystem ?? throw new Exception();
        Settings = api.ModLoader.GetModSystem<CrossbowsModSystem>().Settings ?? throw new Exception();
        Stats = item.Attributes.AsObject<CrossbowStats>();
        AimingStats = Stats.Aiming.ToStats();
        AmmoSelector = selector;
    }

    public override void OnSelected(ItemSlot slot, EntityPlayer player, bool mainHand, ref int state)
    {
        Attachable.ClearAttachments(player.EntityId);
        AttachmentSystem.SendClearPacket(player.EntityId);
        AttachmentSystem.SendSwitchModelPacket(player.EntityId, false);

        bool drawn = slot.Itemstack.Attributes.GetBool("crossbow-drawn", defaultValue: false);
        if (drawn)
        {
            state = (int)CrossbowState.Drawn;
            AnimationBehavior?.Play(mainHand, Stats.LoadedAnimation, category: "string", weight: 0.001f);
            TpAnimationBehavior?.Play(mainHand, Stats.LoadedAnimation, category: "string", weight: 0.001f);
        }
        else
        {
            state = (int)CrossbowState.Unloaded;
        }

        BoltLoaded = false;
    }
    public override void OnDeselected(EntityPlayer player, bool mainHand, ref int state)
    {
        Attachable.ClearAttachments(player.EntityId);
        AttachmentSystem.SendSwitchModelPacket(player.EntityId, false);
        Attachable.SetSwitchModels(player.EntityId, false);
        AttachmentSystem.SendClearPacket(player.EntityId);
        AimingAnimationController?.Stop(mainHand);
        AnimationBehavior?.StopAllVanillaAnimations(mainHand);
        AimingSystem.StopAiming();
        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
        BoltLoaded = false;
    }
    public override void OnRegistered(ActionsManagerPlayerBehavior behavior, ICoreClientAPI api)
    {
        base.OnRegistered(behavior, api);
        AimingAnimationController = new(AimingSystem, AnimationBehavior, AimingStats);
    }

    protected AimingAnimationController? AimingAnimationController;
    protected readonly AnimatableAttachable Attachable;
    protected readonly ClientAimingSystem AimingSystem;
    protected readonly ModelTransform BoltTransform;
    protected readonly CrossbowStats Stats;
    protected readonly AimingStats AimingStats;
    protected readonly AmmoSelector AmmoSelector;
    protected readonly CrossbowsSettings Settings;
    protected bool BoltLoaded = false;

    protected const string PlayerStatsMainHandCategory = "CombatOverhaul:held-item-mainhand";
    protected const string PlayerStatsOffHandCategory = "CombatOverhaul:held-item-offhand";

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Draw(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)CrossbowState.Unloaded) return false;
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;
        if (!CheckOffhandEmpty(player)) return false;
        if (!CheckDrawRequirement(player)) return false;

        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(slot.Itemstack);

        AnimationBehavior?.Play(mainHand, Stats.DrawAnimation, callback: () => DrawAnimationCallback(slot, mainHand, player), animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat) * stackStats.ReloadSpeed * Stats.ReloadAnimationSpeed);
        TpAnimationBehavior?.Play(mainHand, Stats.DrawAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat) * stackStats.ReloadSpeed * Stats.ReloadAnimationSpeed);
        AttachmentSystem.SendSwitchModelPacket(player.EntityId, true);
        Attachable.SetSwitchModels(player.EntityId, true);
        if (TpAnimationBehavior == null) AnimationBehavior?.PlayVanillaAnimation(Stats.DrawTpAnimation, mainHand);

        state = (int)CrossbowState.Draw;

        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory, Stats.DrawSpeedPenalty);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Load(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)CrossbowState.Drawn) return false;
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;

        ItemSlot? boltSlot = GetBoltSlot(player);

        if (boltSlot == null)
        {
            Api.TriggerIngameError(this, "noBoltsAvailable", Lang.Get("maltiezcrossbows:requirement-ammo"));
            return false;
        }

        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(slot.Itemstack);

        Attachable.SetAttachment(player.EntityId, "bolt", boltSlot.Itemstack, BoltTransform);
        AttachmentSystem.SendAttachPacket(player.EntityId, "bolt", boltSlot.Itemstack, BoltTransform);

        AnimationBehavior?.Play(mainHand, Stats.LoadAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat) * stackStats.ReloadSpeed * Stats.ReloadAnimationSpeed, callback: () => LoadAnimationCallback(slot, mainHand, player));
        TpAnimationBehavior?.Play(mainHand, Stats.LoadAnimation, animationSpeed: GetAnimationSpeed(player, Stats.ProficiencyStat) * stackStats.ReloadSpeed * Stats.ReloadAnimationSpeed);
        if (TpAnimationBehavior == null) AnimationBehavior?.PlayVanillaAnimation(Stats.LoadTpAnimation, mainHand);

        state = (int)CrossbowState.Load;

        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory, Stats.LoadSpeedPenalty);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Aim(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)CrossbowState.Loaded) return false;
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;

        AnimationBehavior?.Play(mainHand, Stats.AimAnimation);
        TpAnimationBehavior?.Play(mainHand, Stats.AimAnimation);
        if (TpAnimationBehavior == null) AnimationBehavior?.PlayVanillaAnimation(Stats.AimTpAnimation, mainHand);

        state = (int)CrossbowState.Aimed;

        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(slot.Itemstack);
        AimingStats aimingStats = AimingStats.Clone();
        aimingStats.AimDifficulty *= stackStats.AimingDifficulty;
        aimingStats.CursorType = Enum.Parse<AimingCursorType>(Settings.AimingCursorType);

        AimingSystem.StartAiming(aimingStats);
        AimingSystem.AimingState = WeaponAimingState.FullCharge;

        AimingAnimationController?.Play(mainHand);

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Released)]
    protected virtual bool Ease(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);

        switch ((CrossbowState)state)
        {
            case CrossbowState.Draw:
                state = (int)CrossbowState.Unloaded;
                AnimationBehavior?.PlayReadyAnimation(mainHand);
                TpAnimationBehavior?.PlayReadyAnimation(mainHand);
                AnimationBehavior?.StopVanillaAnimation(Stats.DrawTpAnimation, mainHand);
                AttachmentSystem.SendSwitchModelPacket(player.EntityId, false);
                Attachable.SetSwitchModels(player.EntityId, false);
                return false;

            case CrossbowState.Load:
                state = (int)CrossbowState.Drawn;
                AnimationBehavior?.PlayReadyAnimation(mainHand);
                TpAnimationBehavior?.PlayReadyAnimation(mainHand);
                AnimationBehavior?.StopVanillaAnimation(Stats.LoadTpAnimation, mainHand);
                Attachable.ClearAttachments(player.EntityId);
                AttachmentSystem.SendClearPacket(player.EntityId);
                return false;

            case CrossbowState.AimedEmpty:
            case CrossbowState.Aimed:
                state = BoltLoaded ? (int)CrossbowState.Loaded : (int)CrossbowState.Unloaded;
                AnimationBehavior?.PlayReadyAnimation(mainHand);
                TpAnimationBehavior?.PlayReadyAnimation(mainHand);
                AnimationBehavior?.StopVanillaAnimation(Stats.AimTpAnimation, mainHand);
                AimingAnimationController?.Stop(mainHand);
                AimingSystem.StopAiming();
                return false;

            default:
                return false;
        }
    }

    [ActionEventHandler(EnumEntityAction.LeftMouseDown, ActionState.Pressed)]
    protected virtual bool Shoot(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (state != (int)CrossbowState.Aimed) return false;
        if (InteractionsTester.PlayerTriesToInteract(player, mainHand, eventData)) return false;

        ItemSlot? boltSlot = GetBoltSlot(player);
        if (boltSlot == null) return false;

        if (!BoltLoaded) return false;

        AnimationBehavior?.Stop("string");
        TpAnimationBehavior?.Stop("string");
        AnimationBehavior?.Play(
            mainHand,
            Stats.ReleaseAnimation,
            weight: 1000,
            callback: () => ReleaseAnimationCallback(slot, mainHand, player),
            callbackHandler: callbackCode => ReleaseAnimationCallbackHandler(callbackCode, slot, mainHand, player));
        TpAnimationBehavior?.Play(
            mainHand,
            Stats.ReleaseAnimation,
            weight: 1000);

        BoltLoaded = false;

        state = (int)CrossbowState.Shooting;

        return true;
    }

    [ActionEventHandler(EnumEntityAction.RightMouseDown, ActionState.Active)]
    protected virtual bool Cancel(ItemSlot slot, EntityPlayer player, ref int state, ActionEventData eventData, bool mainHand, AttackDirection direction)
    {
        if (player.MountedOn?.Entity == null && !player.OnGround && Stats.CancelReloadOnInAir)
        {
            switch ((CrossbowState)state)
            {
                case CrossbowState.Draw:
                    PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
                    state = (int)CrossbowState.Unloaded;
                    AnimationBehavior?.PlayReadyAnimation(mainHand);
                    TpAnimationBehavior?.PlayReadyAnimation(mainHand);
                    AnimationBehavior?.StopVanillaAnimation(Stats.DrawTpAnimation, mainHand);
                    Api.TriggerIngameError(this, "reloadInTheAir", Lang.Get("maltiezcrossbows:requirement-not-in-the-air"));
                    return false;
                default:
                    return false;
            }
        }

        if (player.MountedOn?.Entity != null && Stats.CancelReloadMounted)
        {
            switch ((CrossbowState)state)
            {
                case CrossbowState.Draw:
                    PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
                    state = (int)CrossbowState.Unloaded;
                    AnimationBehavior?.PlayReadyAnimation(mainHand);
                    TpAnimationBehavior?.PlayReadyAnimation(mainHand);
                    AnimationBehavior?.StopVanillaAnimation(Stats.DrawTpAnimation, mainHand);
                    Api.TriggerIngameError(this, "reloadMounted", Lang.Get("maltiezcrossbows:requirement-not-mounted"));
                    return false;
                default:
                    return false;
            }
        }

        return false;
    }

    protected virtual void DrawCallback(bool success)
    {
        if (success)
        {
            PlayerBehavior?.SetState((int)CrossbowState.Drawn, mainHand: true);
        }
        else
        {
            PlayerBehavior?.SetState((int)CrossbowState.Unloaded, mainHand: true);
        }
        AnimationBehavior?.PlayReadyAnimation();
        TpAnimationBehavior?.PlayReadyAnimation();
    }
    protected virtual bool DrawAnimationCallback(ItemSlot slot, bool mainHand, EntityPlayer player)
    {
        RangedWeaponSystem.Load(slot, mainHand, DrawCallback);
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);
        AnimationBehavior?.StopVanillaAnimation(Stats.DrawTpAnimation, mainHand: true);
        AnimationBehavior?.Play(mainHand, Stats.LoadedAnimation, category: "string", weight: 0.001f);
        TpAnimationBehavior?.Play(mainHand, Stats.LoadedAnimation, category: "string", weight: 0.001f);
        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
        AttachmentSystem.SendSwitchModelPacket(player.EntityId, false);
        Attachable.SetSwitchModels(player.EntityId, false);
        return true;
    }
    protected virtual void LoadCallback(bool success)
    {
        if (success)
        {
            PlayerBehavior?.SetState((int)CrossbowState.Loaded, mainHand: true);
        }
        else
        {
            PlayerBehavior?.SetState((int)CrossbowState.Drawn, mainHand: true);
        }
    }
    protected virtual bool LoadAnimationCallback(ItemSlot slot, bool mainHand, EntityPlayer player)
    {
        ItemSlot? boltSlot = GetBoltSlot(player);
        if (boltSlot == null) return true;

        BoltLoaded = true;
        RangedWeaponSystem.Reload(slot, boltSlot, 1, mainHand, LoadCallback);
        AnimationBehavior?.PlayReadyAnimation(mainHand);
        TpAnimationBehavior?.PlayReadyAnimation(mainHand);
        AnimationBehavior?.StopVanillaAnimation(Stats.LoadTpAnimation, mainHand: true);
        PlayerBehavior?.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
        return true;
    }
    protected virtual void ShootCallback(bool success)
    {

    }
    protected virtual void ReleaseAnimationCallbackHandler(string callbackCode, ItemSlot slot, bool mainHand, EntityPlayer player)
    {
        switch (callbackCode)
        {
            case "shoot":
                {
                    Vintagestory.API.MathTools.Vec3d position = player.LocalEyePos + player.Pos.XYZ;
                    Vector3 targetDirection = AimingSystem.TargetVec;

                    targetDirection = ClientAimingSystem.Zeroing(targetDirection, Stats.Zeroing);

                    RangedWeaponSystem.Shoot(slot, 1, new((float)position.X, (float)position.Y, (float)position.Z), new(targetDirection.X, targetDirection.Y, targetDirection.Z), mainHand, ShootCallback);

                    Attachable.ClearAttachments(player.EntityId);
                    AttachmentSystem.SendClearPacket(player.EntityId);
                }
                break;
        }
    }
    protected virtual bool ReleaseAnimationCallback(ItemSlot slot, bool mainHand, EntityPlayer player)
    {
        CrossbowState state = (CrossbowState?)PlayerBehavior?.GetState(mainHand) ?? CrossbowState.Shooting;
        if (state == CrossbowState.Shooting)
        {
            PlayerBehavior?.SetState((int)CrossbowState.AimedEmpty, mainHand);

            if (PlayerBehavior?.ActionListener.IsActive(EnumEntityAction.RightMouseDown) == false)
            {
                PlayerBehavior.SetState((int)CrossbowState.Unloaded, mainHand);
                PlayerBehavior.SetStat("walkspeed", mainHand ? PlayerStatsMainHandCategory : PlayerStatsOffHandCategory);
                AnimationBehavior?.PlayReadyAnimation(mainHand);
                TpAnimationBehavior?.PlayReadyAnimation(mainHand);
                AnimationBehavior?.StopVanillaAnimation(Stats.AimTpAnimation, mainHand);
                AimingAnimationController?.Stop(mainHand);
                AimingSystem.StopAiming();
            }
        }

        return true;
    }
    protected virtual bool CheckOffhandEmpty(EntityPlayer player)
    {
        if (!player.LeftHandItemSlot.Empty)
        {
            Api.TriggerIngameError(this, "offhandMustBeEmpty", Lang.Get("maltiezcrossbows:requirement-empty-offhand"));
            return false;
        }
        return true;
    }
    protected virtual bool CheckDrawRequirement(EntityPlayer player)
    {
        if (Stats.DrawRequirement == "") return true;

        ItemSlot? requirement = null;

        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (WildcardUtil.Match(Stats.DrawRequirement, slot.Itemstack.Item.Code.ToString()))
            {
                requirement = slot;
                return false;
            }

            return true;
        });

        if (requirement == null)
        {
            Api.TriggerIngameError(this, "missingSpanningTool", Lang.Get("maltiezcrossbows:requirement-spanning-tool"));
        }

        return requirement != null;
    }
    protected virtual ItemSlot? GetBoltSlot(EntityPlayer player)
    {
        ItemSlot? boltSlot = null;

        player.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Item == null) return true;

            if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(AmmoSelector.SelectedAmmo, slot.Itemstack.Item.Code.ToString()))
            {
                boltSlot = slot;
                return false;
            }

            return true;
        });

        if (boltSlot == null)
        {
            player.WalkInventory(slot =>
            {
                if (slot?.Itemstack?.Item == null) return true;

                if (slot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(Stats.BoltWildcard, slot.Itemstack.Item.Code.ToString()))
                {
                    boltSlot = slot;
                    return false;
                }

                return true;
            });
        }

        return boltSlot;
    }
}


public class CrossbowServer : RangeWeaponServer
{
    public CrossbowServer(ICoreServerAPI api, Item item) : base(api, item)
    {
        _projectileSystem = api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerProjectileSystem ?? throw new Exception();
        _stats = item.Attributes.AsObject<CrossbowStats>();
    }

    public override bool Reload(IServerPlayer player, ItemSlot slot, ItemSlot? ammoSlot, ReloadPacket packet)
    {
        if (ammoSlot?.Itemstack?.Item != null && ammoSlot.Itemstack.Item.HasBehavior<ProjectileBehavior>() && WildcardUtil.Match(_stats.BoltWildcard, ammoSlot.Itemstack.Item.Code.ToString()))
        {
            _boltSlots[player.Entity.EntityId] = (ammoSlot.Inventory, ammoSlot.Inventory.GetSlotId(ammoSlot));
            return true;
        }

        if (ammoSlot == null)
        {
            slot.Itemstack.Attributes.SetBool("crossbow-drawn", true);
            slot.MarkDirty();
            return true;
        }

        return false;
    }

    public override bool Shoot(IServerPlayer player, ItemSlot slot, ShotPacket packet, Entity shooter)
    {
        if (!_boltSlots.ContainsKey(player.Entity.EntityId)) return false;

        (InventoryBase inventory, int slotId) = _boltSlots[player.Entity.EntityId];

        if (inventory.Count <= slotId) return false;

        ItemSlot? boltSlot = inventory[slotId];

        if (boltSlot?.Itemstack == null || boltSlot.Itemstack.StackSize < 1) return false;

        ProjectileStats? stats = boltSlot.Itemstack.Item.GetCollectibleBehavior<ProjectileBehavior>(true)?.GetStats(boltSlot.Itemstack);

        if (stats == null)
        {
            _boltSlots.Remove(player.Entity.EntityId);
            return false;
        }

        ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(slot.Itemstack);

        Vector3d playerVelocity = new(player.Entity.ServerPos.Motion.X, player.Entity.ServerPos.Motion.Y, player.Entity.ServerPos.Motion.Z);

        ProjectileSpawnStats spawnStats = new()
        {
            ProducerEntityId = player.Entity.EntityId,
            DamageMultiplier = _stats.BoltDamageMultiplier * stackStats.DamageMultiplier,
            DamageStrength = _stats.BoltDamageStrength + stackStats.DamageTierBonus,
            Position = new Vector3d(packet.Position[0], packet.Position[1], packet.Position[2]),
            Velocity = GetDirectionWithDispersion(packet.Velocity, [_stats.DispersionMOA[0] * stackStats.DispersionMultiplier, _stats.DispersionMOA[1] * stackStats.DispersionMultiplier]) * _stats.BoltVelocity * stackStats.ProjectileSpeed + playerVelocity,
        };

        _projectileSystem.Spawn(packet.ProjectileId[0], stats, spawnStats, boltSlot.TakeOut(1), slot.Itemstack, shooter);

        boltSlot.MarkDirty();

        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, 1 + stats.AdditionalDurabilityCost);
        slot.Itemstack.Attributes.SetBool("crossbow-drawn", false);
        slot.MarkDirty();
        return true;
    }

    private readonly Dictionary<long, (InventoryBase, int)> _boltSlots = [];
    private readonly ProjectileSystemServer _projectileSystem;
    private readonly CrossbowStats _stats;
}

public sealed class CrossbowItem : Item, IHasWeaponLogic, IHasRangedWeaponLogic, IHasMoveAnimations
{
    public CrossbowClient? ClientLogic { get; private set; }
    public CrossbowServer? ServerLogic { get; private set; }

    public AnimationRequestByCode IdleAnimation { get; private set; }
    public AnimationRequestByCode ReadyAnimation { get; private set; }
    public AnimationRequestByCode WalkAnimation { get; private set; }
    public AnimationRequestByCode RunAnimation { get; private set; }
    public AnimationRequestByCode SwimAnimation { get; private set; }
    public AnimationRequestByCode SwimIdleAnimation { get; private set; }

    IClientWeaponLogic? IHasWeaponLogic.ClientLogic => ClientLogic;
    IServerRangedWeaponLogic? IHasRangedWeaponLogic.ServerWeaponLogic => ServerLogic;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI clientAPI)
        {
            _stats = Attributes.AsObject<CrossbowStats>();
            IdleAnimation = new(_stats.IdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            ReadyAnimation = new(_stats.ReadyAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            WalkAnimation = new(_stats.WalkAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            RunAnimation = new(_stats.RunAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            SwimAnimation = new(_stats.SwimAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            SwimIdleAnimation = new(_stats.SwimIdleAnimation, 1, 1, "main", TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), false);
            
            _clientApi = clientAPI;
            _ammoSelector = new(clientAPI, _stats.BoltWildcard);

            ClientLogic = new(clientAPI, this, _ammoSelector);
        }

        if (api is ICoreServerAPI serverAPI)
        {
            ServerLogic = new(serverAPI, this);
        }
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (_stats != null && _stats.ProficiencyStat != "")
        {
            string description = Lang.Get("combatoverhaul:iteminfo-proficiency", Lang.Get($"combatoverhaul:proficiency-{_stats.ProficiencyStat}"));
            dsc.AppendLine(description);
        }

        if (_stats != null)
        {
            ItemStackRangedStats stackStats = ItemStackRangedStats.FromItemStack(inSlot.Itemstack);

            dsc.AppendLine(Lang.Get("combatoverhaul:iteminfo-range-weapon-damage", _stats.BoltDamageMultiplier * stackStats.DamageMultiplier, _stats.BoltDamageStrength + stackStats.DamageTierBonus));
            dsc.AppendLine("");
        }
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        WorldInteraction[] interactions = base.GetHeldInteractionHelp(inSlot);

        return interactions
            .Append(_ammoSelectionInteraction)
            .Append(_aimAndLoadInteraction)
            .Append(_shootInteraction)
            .ToArray();
    }

    public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
    {
        if (_clientApi?.World.Player.Entity.EntityId == byPlayer.Entity.EntityId)
        {
            return _ammoSelector?.GetToolMode(slot, byPlayer, blockSelection) ?? 0;
        }

        return 0;
    }
    public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        if (_clientApi?.World.Player.Entity.EntityId == forPlayer.Entity.EntityId)
        {
            return _ammoSelector?.GetToolModes(slot, forPlayer, blockSel) ?? Array.Empty<SkillItem>();
        }

        return [];
    }
    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
    {
        if (_clientApi?.World.Player.Entity.EntityId == byPlayer.Entity.EntityId)
        {
            _ammoSelector?.SetToolMode(slot, byPlayer, blockSelection, toolMode);
        }
    }

    public override int GetRemainingDurability(ItemStack itemstack)
    {
        int durability = base.GetRemainingDurability(itemstack);
        int maxDurability = GetMaxDurability(itemstack);
        if (durability > maxDurability)
        {
            SetDurability(itemstack, maxDurability);
        }
        return maxDurability;
    }

    private AmmoSelector? _ammoSelector;
    private ICoreClientAPI? _clientApi;
    private CrossbowStats? _stats;
    private static readonly WorldInteraction _ammoSelectionInteraction = new()
    {
        ActionLangCode = Lang.Get("combatoverhaul:interaction-ammoselection"),
        HotKeyCodes = new string[1] { "toolmodeselect" },
        MouseButton = EnumMouseButton.None
    };
    private static readonly WorldInteraction _aimAndLoadInteraction = new()
    {
        ActionLangCode = Lang.Get("maltiezcrossbows:interaction-load-and-aim"),
        MouseButton = EnumMouseButton.Right,
    };
    private static readonly WorldInteraction _shootInteraction = new()
    {
        ActionLangCode = Lang.Get("maltiezcrossbows:interaction-shoot"),
        MouseButton = EnumMouseButton.Left,
    };
}
