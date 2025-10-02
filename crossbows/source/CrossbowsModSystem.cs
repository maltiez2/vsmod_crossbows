using ConfigLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Crossbows;

public class CrossbowsSettings
{
    public string AimingCursorType { get; set; } = "Fixed";
}

public class CrossbowsModSystem : ModSystem
{
    public CrossbowsSettings Settings { get; set; } = new();

    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("Crossbows:Crossbow", typeof(CrossbowItem));
        api.RegisterItemClass("Crossbows:MagazineCrossbow", typeof(MagazineCrossbowItem));

        if (api.ModLoader.IsModEnabled("configlib"))
        {
            SubscribeToConfigChange(api);
        }
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api is ICoreClientAPI clientApi)
        {
            CheckStatusClientSide(clientApi);
        }
    }

    private void SubscribeToConfigChange(ICoreAPI api)
    {
        ConfigLibModSystem system = api.ModLoader.GetModSystem<ConfigLibModSystem>();

        system.SettingChanged += (domain, config, setting) =>
        {
            if (domain != "maltiezcrossbows") return;

            setting.AssignSettingValue(Settings);
        };

        system.ConfigsLoaded += () =>
        {
            system.GetConfig("maltiezcrossbows")?.AssignSettingsValues(Settings);
        };
    }

    private void CheckStatusClientSide(ICoreClientAPI api)
    {
        bool immersiveFirstPersonMode = api.Settings.Bool["immersiveFpMode"];
        if (immersiveFirstPersonMode)
        {
            CombatOverhaul.Utils.LoggerUtil.Error(api, this, $"Immersive first person mode is enabled. It is not supported. Turn this setting off.");
            AnnoyPlayer(api, "(Crossbows) Immersive first person mode is enabled. It is not supported. Turn this setting off to prevent this message.", () => api.Settings.Bool["immersiveFpMode"]);
        }
    }
    private void AnnoyPlayer(ICoreClientAPI api, string message, System.Func<bool> continueDelegate)
    {
        api.World.RegisterCallback(_ =>
        {
            api.TriggerIngameError(this, "error", message);
            if (continueDelegate()) AnnoyPlayer(api, message, continueDelegate);
        }, 5000);
    }
}
