using ConfigLib;
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
}
